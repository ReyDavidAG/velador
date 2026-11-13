using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Velador.Core;

namespace Velador.Engine.Tapo;

public sealed class TapoException(string message, int code = 0) : Exception(message)
{
	public int Code { get; } = code;
}

public sealed record WifiNetwork(string Ssid, string Bssid, int Auth, int Encryption, int Rssi);

public sealed class TapoClient(string host, string password) : IDisposable
{
	const string User = "admin";

	static readonly JsonSerializerOptions Plain = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

	readonly HttpClient _http = new(new SocketsHttpHandler
	{
		UseProxy = false,
		SslOptions = new SslClientAuthenticationOptions { RemoteCertificateValidationCallback = (_, _, _, _) => true },
		PooledConnectionLifetime = TimeSpan.FromMinutes(2),
	})
	{ Timeout = TimeSpan.FromSeconds(8) };

	readonly SemaphoreSlim _gate = new(1, 1);
	TapoKeys? _keys;
	byte[]? _sessionKey;
	byte[]? _sessionNonce;
	string _cnonce = "";
	string _stok = "";
	long _seq;
	string _publicKey = "";

	async Task<JsonNode> PostAsync(string url, string body, long? seq, string? tag, CancellationToken ct)
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(body, Encoding.UTF8) };
		request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json; charset=UTF-8");
		request.Headers.TryAddWithoutValidation("Referer", $"https://{host}:443");
		request.Headers.TryAddWithoutValidation("Accept", "application/json");
		request.Headers.TryAddWithoutValidation("User-Agent", "Tapo CameraClient Android");
		request.Headers.TryAddWithoutValidation("requestByApp", "true");
		if (seq is not null) request.Headers.TryAddWithoutValidation("Seq", seq.ToString());
		if (tag is not null) request.Headers.TryAddWithoutValidation("Tapo_tag", tag);
		using var response = await _http.SendAsync(request, ct);
		return JsonNode.Parse(await response.Content.ReadAsStringAsync(ct)) ?? throw new TapoException("La cámara respondió vacío.");
	}

	async Task<JsonNode> LoginStepAsync(object parameters, CancellationToken ct) =>
		await PostAsync($"https://{host}:443/", JsonSerializer.Serialize(new { method = "login", @params = parameters }, Plain), null, null, ct);

	async Task LoginAsync(CancellationToken ct)
	{
		var discover = await LoginStepAsync(new { sub_method = "discover" }, ct);
		var tpap = discover["result"]?["tpap"];
		if (tpap?["pake"]?.AsArray().Any(n => n!.GetValue<int>() == 2) == true)
		{
			await LoginTpapAsync(tpap, ct);
			return;
		}
		try
		{
			await LoginLegacyAsync(ct);
		}
		catch (TapoException e) when (e.Code == -40211)
		{
			await LoginTpapAsync(new JsonObject(), ct);
		}
	}

	async Task LoginTpapAsync(JsonNode tpap, CancellationToken ct)
	{
		var user = tpap["user_hash_type"]?.GetValue<int>() == 1 ? TapoCrypto.Sha256Upper(User) : TapoCrypto.Md5Upper(User).ToLowerInvariant();
		string[] passcodes = [TapoCrypto.Md5Upper(password).ToLowerInvariant(), TapoCrypto.Sha256Upper(password)];
		for (var index = 0; index < passcodes.Length; index++)
		{
			var userRandom = RandomNumberGenerator.GetBytes(32);
			var register = (await LoginStepAsync(new { sub_method = "pake_register", username = user, user_random = Convert.ToBase64String(userRandom), cipher_suites = new[] { 1 }, encryption = new[] { "aes_128_ccm" }, passcode_type = "userpw" }, ct))["result"]
				?? throw new TapoException("La cámara rechazó el inicio de sesión.");
			var client = new Spake2PlusClient(
				Convert.FromBase64String(register["dev_salt"]!.GetValue<string>()),
				Number(register["iterations"]),
				Convert.FromBase64String(register["dev_share"]!.GetValue<string>()),
				Convert.FromBase64String(register["dev_random"]!.GetValue<string>()),
				userRandom,
				Credential(passcodes[index], register["extra_crypt"]),
				P256.Unsigned(RandomNumberGenerator.GetBytes(32)));
			var share = await LoginStepAsync(new { sub_method = "pake_share", user_share = Convert.ToBase64String(client.UserShare), user_confirm = Convert.ToBase64String(client.UserConfirm) }, ct);
			if (share["result"] is not { } result)
			{
				var wait = new[] { share["error_info"], share["data"], share["result"]?["data"] }.Select(h => h?["sec_left"]).FirstOrDefault(n => n is not null);
				if (wait is not null && Number(wait) > 0) throw new TapoException($"La cámara bloqueó los intentos. Espera {Number(wait)} segundos.", -40404);
				if (share["error_code"]?.GetValue<int>() == -40401 && index + 1 < passcodes.Length) continue;
				throw new TapoException("La contraseña de la cuenta Tapo no es correcta.", -40411);
			}
			if (!client.DeviceConfirms(Convert.FromBase64String(result["dev_confirm"]!.GetValue<string>()))) throw new TapoException("La cámara no pudo comprobar su identidad.");
			(_sessionKey, _sessionNonce) = client.Session();
			_stok = result["stok"]!.GetValue<string>();
			_seq = Number(result["start_seq"]);
			return;
		}
	}

	static string Credential(string passcode, JsonNode? extra)
	{
		if (extra is null || extra["type"]?.GetValue<string>().ToLowerInvariant() != "password_shadow") return passcode;
		return Number(extra["params"]?["passwd_id"]) switch
		{
			5 => Sha256Crypt.Hash(passcode, extra["params"]?["passwd_prefix"]?.GetValue<string>() ?? ""),
			2 => Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(passcode))),
			_ => throw new TapoException("Esta cámara pide una forma de contraseña que Velador no conoce."),
		};
	}

	async Task LoginLegacyAsync(CancellationToken ct)
	{
		_cnonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
		var first = await LoginStepAsync(new { cnonce = _cnonce, encrypt_type = "3", username = User }, ct);
		var data = first["result"]?["data"];
		var nonce = data?["nonce"]?.GetValue<string>();
		var confirm = data?["device_confirm"]?.GetValue<string>();
		if (data?["sec_left"] is { } left && Number(left) > 0) throw new TapoException($"La cámara bloqueó los intentos. Espera {Number(left)} segundos.", -40404);
		if (nonce is null || confirm is null) throw new TapoException("Esta cámara no acepta el acceso de Tapo.", first["error_code"]?.GetValue<int>() ?? 0);
		var hashed = TapoCrypto.HashThatMatches(password, _cnonce, nonce, confirm) ?? throw new TapoException("La contraseña de la cuenta Tapo no es correcta.", -40411);
		var second = await LoginStepAsync(new { cnonce = _cnonce, encrypt_type = "3", digest_passwd = TapoCrypto.Digest(hashed, _cnonce, nonce), username = User }, ct);
		var result = second["result"];
		if (result?["stok"]?.GetValue<string>() is not { Length: > 0 } stok || result["start_seq"] is null) throw new TapoException("La cámara rechazó el inicio de sesión.", second["error_code"]?.GetValue<int>() ?? 0);
		_stok = stok;
		_seq = Number(result["start_seq"]);
		_keys = TapoCrypto.Keys(hashed, _cnonce, nonce);
	}

	bool LoggedIn => _keys is not null || _sessionKey is not null;

	void Drop()
	{
		_keys = null;
		_sessionKey = null;
	}

	async Task<(JsonNode? Answer, int Code)> RoundTripAsync(string inner, CancellationToken ct)
	{
		var seq = _seq++;
		if (_sessionKey is not null)
		{
			var counter = new byte[4];
			System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(counter, (uint)seq);
			byte[] body = [.. counter, .. AesCcm128.Seal(_sessionKey, [.. _sessionNonce![..8], .. counter], Encoding.UTF8.GetBytes(inner))];
			using var request = new HttpRequestMessage(HttpMethod.Post, $"https://{host}:443/stok={_stok}/ds") { Content = new ByteArrayContent(body) };
			request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
			request.Headers.TryAddWithoutValidation("Accept", "application/octet-stream");
			request.Headers.TryAddWithoutValidation("Referer", $"https://{host}:443");
			request.Headers.TryAddWithoutValidation("User-Agent", "Tapo CameraClient Android");
			request.Headers.TryAddWithoutValidation("requestByApp", "true");
			using var response = await _http.SendAsync(request, ct);
			var raw = await response.Content.ReadAsByteArrayAsync(ct);
			if (raw.Length > 0 && raw[0] == (byte)'{') return (null, JsonNode.Parse(raw)?["error_code"]?.GetValue<int>() ?? -1);
			if (raw.Length < 20) return (null, -1);
			var plain = AesCcm128.Open(_sessionKey, [.. _sessionNonce![..8], .. raw[..4]], raw[4..]);
			return plain is null ? (null, -1) : (JsonNode.Parse(plain), 0);
		}
		var legacy = $"{{\"method\": \"securePassthrough\", \"params\": {{\"request\": \"{TapoCrypto.Encrypt(_keys!, inner)}\"}}}}";
		var outer = await PostAsync($"https://{host}:443/stok={_stok}/ds", legacy, seq, TapoCrypto.Tag(_keys!, _cnonce, legacy, seq), ct);
		return outer["result"]?["response"]?.GetValue<string>() is { } encrypted
			? (JsonNode.Parse(TapoCrypto.Decrypt(_keys!, encrypted)), 0)
			: (null, outer["error_code"]?.GetValue<int>() ?? -1);
	}

	public async Task<JsonNode> CallAsync(string method, JsonNode parameters, CancellationToken ct)
	{
		await _gate.WaitAsync(ct);
		try
		{
			for (var attempt = 0; ; attempt++)
			{
				if (!LoggedIn) await LoginAsync(ct);
				var inner = new JsonObject { ["method"] = "multipleRequest", ["params"] = new JsonObject { ["requests"] = new JsonArray(new JsonObject { ["method"] = method, ["params"] = parameters.DeepClone() }) } };
				var (answer, code) = await RoundTripAsync(inner.ToJsonString(Plain), ct);
				if (answer is not null)
				{
					var one = answer["result"]?["responses"]?[0];
					code = one?["error_code"]?.GetValue<int>() ?? answer["error_code"]?.GetValue<int>() ?? 0;
					if (code == 0 && one is not null) return one["result"] ?? one;
				}
				Drop();
				if (attempt >= 1 || code is not (-40401 or -40421 or -1 or -40413)) throw new TapoException(code == -40210 ? "Esta cámara no tiene esa función." : $"La cámara respondió con el error {code}.", code);
			}
		}
		catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or CryptographicException or IOException or FormatException)
		{
			Drop();
			ct.ThrowIfCancellationRequested();
			throw new TapoException("No se pudo hablar con la cámara por el protocolo de Tapo.");
		}
		finally
		{
			_gate.Release();
		}
	}

	static JsonNode Node(string json) => JsonNode.Parse(json)!;

	public async Task<(bool On, int RestSeconds)> LightAsync(CancellationToken ct)
	{
		var status = await CallAsync("getWhitelampStatus", Node("""{"image":{"get_wtl_status":["null"]}}"""), ct);
		return (Number(status["status"]) == 1, Number(status["rest_time"]));
	}

	public async Task<bool> SetLightAsync(bool on, CancellationToken ct)
	{
		if ((await LightAsync(ct)).On == on) return on;
		await CallAsync("reverseWhitelampStatus", Node("""{"image":{"reverse_wtl_status":["null"]}}"""), ct);
		for (var i = 0; i < 6; i++)
		{
			await Task.Delay(400, ct);
			if ((await LightAsync(ct)).On == on) return on;
		}
		return !on;
	}

	public async Task<JsonNode> ImageAsync(CancellationToken ct) =>
		(await CallAsync("getWhitelampConfig", Node("""{"image":{"name":["switch"]}}"""), ct))["image"]?["switch"] ?? throw new TapoException("La cámara no entregó sus ajustes de imagen.");

	public Task SetImageAsync(string key, string value, CancellationToken ct) =>
		CallAsync(key == "night_vision_mode" ? "setNightVisionModeConfig" : "setLdc", new JsonObject { ["image"] = new JsonObject { ["switch"] = new JsonObject { [key] = value } } }, ct);

	public async Task<IReadOnlyList<string>> NightModesAsync(CancellationToken ct)
	{
		var capability = await CallAsync("getNightVisionCapability", Node("""{"image_capability":{"name":["supplement_lamp"]}}"""), ct);
		return capability["image_capability"]?["supplement_lamp"]?["night_vision_mode_range"]?.AsArray().Select(n => n!.GetValue<string>()).ToList() ?? [];
	}

	public async Task<IReadOnlyList<WifiNetwork>> ScanAsync(CancellationToken ct)
	{
		var scan = (await CallAsync("scanApList", Node("""{"onboarding":{"scan":{}}}"""), ct))["onboarding"];
		_publicKey = scan?["public_key"]?.GetValue<string>() ?? scan?["publicKey"]?.GetValue<string>() ?? "";
		var list = scan?["ap_list"]?.AsArray() ?? scan?["scan"]?["ap_list"]?.AsArray() ?? [];
		return [.. list.Select(n => new WifiNetwork(n!["ssid"]!.GetValue<string>(), n["bssid"]!.GetValue<string>(), Number(n["auth"]), Number(n["encryption"]), Number(n["rssi"]))).Where(n => n.Ssid.Length > 0)];
	}

	public async Task JoinAsync(string ssid, string wifiPassword, CancellationToken ct)
	{
		var network = (await ScanAsync(ct)).FirstOrDefault(n => n.Ssid == ssid) ?? throw new TapoException("La cámara ya no ve esa red.");
		if (network.Auth != 0 && _publicKey.Length == 0) throw new TapoException("La cámara no entregó la llave para cifrar la contraseña.");
		var connect = new JsonObject
		{
			["auth"] = network.Auth,
			["bssid"] = network.Bssid,
			["encryption"] = network.Encryption,
			["password"] = network.Auth == 0 ? "" : TapoCrypto.EncryptForCamera(_publicKey, wifiPassword),
			["rssi"] = network.Rssi,
			["ssid"] = network.Ssid,
		};
		try
		{
			await CallAsync("connectAp", new JsonObject { ["onboarding"] = new JsonObject { ["connect"] = connect } }, ct);
		}
		catch (TapoException e) when (e.Code == 0) { }
		Drop();
	}

	static int Number(JsonNode? node) => node is null ? 0 : node.GetValueKind() == JsonValueKind.String ? int.Parse(node.GetValue<string>()) : node.GetValue<int>();

	public void Dispose()
	{
		_http.Dispose();
		_gate.Dispose();
	}
}
