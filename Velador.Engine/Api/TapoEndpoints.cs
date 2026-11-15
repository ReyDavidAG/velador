using Velador.Engine.Tapo;

namespace Velador.Engine.Api;

public static class TapoEndpoints
{
	static readonly Dictionary<string, string> Modes = new() { ["infrared"] = "inf_night_vision", ["white"] = "wtl_night_vision", ["smart"] = "md_night_vision" };

	public sealed record LightRequest(bool On);

	public sealed record NightRequest(string Mode);

	public sealed record FlipRequest(bool On);

	public sealed record WifiRequest(string Ssid, string? Password);

	public sealed record PictureRequest(string Name, string Value);

	static readonly Dictionary<string, string> Toggles = new() { ["wdr"] = "wide_dynamic", ["hlc"] = "high_light_compensation", ["backlight"] = "backlight", ["dehaze"] = "dehaze" };
	static readonly Dictionary<string, string> Levels = new() { ["brightness"] = "luma", ["contrast"] = "contrast", ["sharpness"] = "sharpness", ["saturation"] = "saturation" };

	static string? Text(System.Text.Json.Nodes.JsonNode? node) => node?.GetValue<string>();

	static int Number(System.Text.Json.Nodes.JsonNode? node) => int.TryParse(Text(node), out var n) ? n : 0;

	static IEnumerable<string> Options(System.Text.Json.Nodes.JsonNode capability, string key) =>
		capability[key]?.AsArray().Select(n => n!.GetValue<string>()) ?? [];

	static async Task<object> PictureView(TapoClient tapo, CancellationToken ct)
	{
		var (common, toggles, main, capability) = await tapo.PictureAsync(ct);
		return new
		{
			wdr = Text(common["wide_dynamic"]) == "on",
			hlc = Text(common["high_light_compensation"]) == "on",
			backlight = Text(common["backlight"]) == "on",
			dehaze = Text(common["dehaze"]) == "on",
			brightness = Number(common["luma"]),
			contrast = Number(common["contrast"]),
			sharpness = Number(common["sharpness"]),
			saturation = Number(common["saturation"]),
			ldc = Text(toggles["ldc"]) == "on",
			bitrate = Number(main["bitrate"]),
			bitrateType = Text(main["bitrate_type"]),
			quality = Number(main["quality"]),
			frameRate = Number(main["frame_rate"]) & 0xFFFF,
			resolution = Text(main["resolution"]),
			codec = Text(main["encode_type"]),
			options = new
			{
				bitrates = Options(capability, "bitrates").Select(int.Parse).Order(),
				frameRates = Options(capability, "frame_rates").Select(f => int.Parse(f) & 0xFFFF).Order(),
				qualities = Options(capability, "qualitys").Select(int.Parse).Order(),
				bitrateTypes = Options(capability, "bitrate_types"),
			},
		};
	}

	static async Task<IResult> SetPicture(string id, PictureRequest request, TapoLinks links, CancellationToken ct) => await Run(links, id, async tapo =>
	{
		var value = request.Value?.Trim() ?? "";
		if (Toggles.TryGetValue(request.Name, out var toggle) && value is "on" or "off") await tapo.SetCommonAsync(toggle, value, ct);
		else if (Levels.TryGetValue(request.Name, out var level) && int.TryParse(value, out var amount) && amount is >= 0 and <= 100) await tapo.SetCommonAsync(level, amount.ToString(System.Globalization.CultureInfo.InvariantCulture), ct);
		else if (request.Name == "ldc" && value is "on" or "off") await tapo.SetImageAsync("ldc", value, ct);
		else if (request.Name is "bitrate" or "bitrateType" or "quality" or "frameRate")
		{
			var (_, _, _, capability) = await tapo.PictureAsync(ct);
			var (key, list, sent) = request.Name switch
			{
				"bitrate" => ("bitrate", "bitrates", value),
				"bitrateType" => ("bitrate_type", "bitrate_types", value),
				"quality" => ("quality", "qualitys", value),
				_ => ("frame_rate", "frame_rates", int.TryParse(value, out var fps) ? (0x10000 + fps).ToString(System.Globalization.CultureInfo.InvariantCulture) : ""),
			};
			if (!Options(capability, list).Contains(sent)) return Results.BadRequest(new { error = "La cámara no admite ese valor." });
			await tapo.SetVideoAsync(key, sent, ct);
		}
		else return Results.BadRequest(new { error = "Ese ajuste no existe o el valor no es válido." });
		return Results.Ok(await PictureView(tapo, ct));
	});

	public static void MapTapo(this RouteGroupBuilder api)
	{
		api.MapGet("/cameras/{id}/extras", (string id, TapoLinks links, CancellationToken ct) => Run(links, id, async tapo =>
		{
			var light = await tapo.LightAsync(ct);
			var image = await tapo.ImageAsync(ct);
			var modes = await tapo.NightModesAsync(ct);
			var mode = image["night_vision_mode"]?.GetValue<string>();
			return Results.Ok(new
			{
				light = new { on = light.On, restSeconds = light.RestSeconds },
				nightMode = Modes.FirstOrDefault(m => m.Value == mode).Key ?? "infrared",
				nightModes = Modes.Where(m => modes.Contains(m.Value)).Select(m => m.Key),
				flipped = image["flip_type"]?.GetValue<string>() is { } flip && flip != "off",
			});
		}));
		api.MapGet("/cameras/{id}/image", (string id, TapoLinks links, CancellationToken ct) => Run(links, id, async tapo => Results.Ok(await PictureView(tapo, ct))));
		api.MapPut("/cameras/{id}/image", SetPicture);
		api.MapGet("/cameras/{id}/image/raw", (string id, TapoLinks links, CancellationToken ct) => Run(links, id, async tapo =>
		{
			var queries = new (string Name, string Method, string Json)[]
			{
				("image", "getLdc", "{\"image\":{\"name\":[\"switch\",\"common\"]}}"),
				("video", "getVideoQualities", "{\"video\":{\"name\":[\"main\",\"minor\"]}}"),
				("videoCapability", "getVideoCapability", "{\"video_capability\":{\"name\":\"main\"}}"),
				("imageCapability", "getImageCapability", "{\"image_capability\":{\"name\":[\"switch\",\"common\",\"supplement_lamp\"]}}"),
				("osd", "getOsd", "{\"OSD\":{\"name\":[\"date\",\"week\",\"font\"],\"table\":[\"label_info\"]}}"),
			};
			var answers = new Dictionary<string, object?>();
			foreach (var (name, method, json) in queries)
			{
				try
				{
					answers[name] = await tapo.QueryAsync(method, json, ct);
				}
				catch (TapoException e)
				{
					answers[name] = new { error = e.Message, code = e.Code };
				}
			}
			return Results.Ok(answers);
		}));
		api.MapPost("/cameras/{id}/light", (string id, LightRequest request, TapoLinks links, CancellationToken ct) =>
			Run(links, id, async tapo => Results.Ok(new { on = await tapo.SetLightAsync(request.On, ct) })));
		api.MapPut("/cameras/{id}/night", (string id, NightRequest request, TapoLinks links, CancellationToken ct) =>
			Modes.TryGetValue(request.Mode, out var mode)
				? Run(links, id, async tapo =>
				{
					await tapo.SetImageAsync("night_vision_mode", mode, ct);
					return Results.NoContent();
				})
				: Task.FromResult(Results.BadRequest(new { error = "Ese modo no existe." })));
		api.MapPut("/cameras/{id}/flip", (string id, FlipRequest request, TapoLinks links, CancellationToken ct) => Run(links, id, async tapo =>
		{
			await tapo.SetImageAsync("flip_type", request.On ? "center" : "off", ct);
			return Results.NoContent();
		}));
		api.MapGet("/cameras/{id}/wifi", (string id, TapoLinks links, CancellationToken ct) => Run(links, id, async tapo =>
			Results.Ok((await tapo.ScanAsync(ct)).OrderByDescending(n => n.Rssi).Select(n => new { n.Ssid, signal = n.Rssi, secured = n.Auth != 0 }))));
		api.MapPost("/cameras/{id}/wifi", (string id, WifiRequest request, TapoLinks links, CancellationToken ct) =>
			string.IsNullOrWhiteSpace(request.Ssid)
				? Task.FromResult(Results.BadRequest(new { error = "Falta el nombre de la red." }))
				: Run(links, id, async tapo =>
				{
					await tapo.JoinAsync(request.Ssid, request.Password ?? "", ct);
					return Results.Accepted();
				}));
	}

	static async Task<IResult> Run(TapoLinks links, string id, Func<TapoClient, Task<IResult>> work)
	{
		if (links.For(id) is not { } tapo) return Results.NotFound(new { error = "Falta la contraseña de la cuenta Tapo de esta cámara." });
		try
		{
			return await work(tapo);
		}
		catch (TapoException e)
		{
			if (e.Code is -40411 or -40404) links.Forget(id);
			return Results.Json(new { error = e.Message }, statusCode: StatusCodes.Status502BadGateway);
		}
	}
}
