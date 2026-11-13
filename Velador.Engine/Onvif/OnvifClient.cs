using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Velador.Core;

namespace Velador.Engine.Onvif;

public sealed record PanTilt(double X, double Y);

public sealed partial class OnvifClient(HttpClient http, Camera camera, string password, TimeProvider time)
{
	const string Media = "http://www.onvif.org/ver10/media/wsdl";
	const string Ptz = "http://www.onvif.org/ver20/ptz/wsdl";
	const string Events = "http://www.onvif.org/ver10/events/wsdl";
	const string Schema = "http://www.onvif.org/ver10/schema";

	string? _profile;

	string Service => $"http://{camera.Host}:{camera.OnvifPort}/onvif/service";

	public static string Envelope(string user, string password, string body, byte[] nonce, DateTimeOffset now)
	{
		var created = now.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
		var digest = Convert.ToBase64String(SHA1.HashData([.. nonce, .. Encoding.UTF8.GetBytes(created), .. Encoding.UTF8.GetBytes(password)]));
		return $"""
			<s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope"><s:Header><Security xmlns="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd"><UsernameToken><Username>{System.Security.SecurityElement.Escape(user)}</Username><Password Type="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-username-token-profile-1.0#PasswordDigest">{digest}</Password><Nonce EncodingType="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary">{Convert.ToBase64String(nonce)}</Nonce><Created xmlns="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd">{created}</Created></UsernameToken></Security></s:Header><s:Body>{body}</s:Body></s:Envelope>
			""";
	}

	async Task<string> CallAsync(string url, string body, TimeSpan timeout, CancellationToken ct)
	{
		using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
		limit.CancelAfter(timeout);
		var envelope = Envelope(camera.User, password, body, RandomNumberGenerator.GetBytes(16), time.GetUtcNow());
		using var content = new StringContent(envelope, Encoding.UTF8);
		content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/soap+xml; charset=utf-8");
		using var response = await http.PostAsync(url, content, limit.Token);
		var text = await response.Content.ReadAsStringAsync(limit.Token);
		if (!response.IsSuccessStatusCode) throw new HttpRequestException($"ONVIF {(int)response.StatusCode}");
		return text;
	}

	async Task<string> ProfileAsync(CancellationToken ct)
	{
		if (_profile is not null) return _profile;
		var xml = XDocument.Parse(await CallAsync(Service, $"<GetProfiles xmlns=\"{Media}\"/>", TimeSpan.FromSeconds(6), ct));
		_profile = xml.Descendants().FirstOrDefault(e => e.Name.LocalName == "Profiles")?.Attribute("token")?.Value
			?? throw new HttpRequestException("ONVIF profile not found");
		return _profile;
	}

	public async Task<string> SerialAsync(CancellationToken ct)
	{
		var xml = XDocument.Parse(await CallAsync($"http://{camera.Host}:{camera.OnvifPort}/onvif/device_service", "<GetDeviceInformation xmlns=\"http://www.onvif.org/ver10/device/wsdl\"/>", TimeSpan.FromSeconds(4), ct));
		return xml.Descendants().FirstOrDefault(e => e.Name.LocalName == "SerialNumber")?.Value.Trim() ?? "";
	}

	public async Task<string> ModelAsync(CancellationToken ct)
	{
		var xml = XDocument.Parse(await CallAsync($"http://{camera.Host}:{camera.OnvifPort}/onvif/device_service", "<GetDeviceInformation xmlns=\"http://www.onvif.org/ver10/device/wsdl\"/>", TimeSpan.FromSeconds(6), ct));
		string Field(string name) => xml.Descendants().FirstOrDefault(e => e.Name.LocalName == name)?.Value ?? "";
		return $"{Field("Model")} · firmware {Field("FirmwareVersion")}".Trim(' ', '·');
	}

	public async Task<PanTilt?> PositionAsync(CancellationToken ct)
	{
		var profile = await ProfileAsync(ct);
		var xml = XDocument.Parse(await CallAsync(Service, $"<GetStatus xmlns=\"{Ptz}\"><ProfileToken>{profile}</ProfileToken></GetStatus>", TimeSpan.FromSeconds(6), ct));
		var position = xml.Descendants().FirstOrDefault(e => e.Name.LocalName == "PanTilt" && e.Attribute("x") is not null);
		return position is null ? null : new PanTilt(Number(position.Attribute("x")!.Value), Number(position.Attribute("y")!.Value));
	}

	public async Task MoveAsync(double dx, double dy, CancellationToken ct)
	{
		var profile = await ProfileAsync(ct);
		var x = Math.Clamp(dx, -1, 1).ToString("0.###", CultureInfo.InvariantCulture);
		var y = Math.Clamp(dy, -1, 1).ToString("0.###", CultureInfo.InvariantCulture);
		await CallAsync(Service, $"<RelativeMove xmlns=\"{Ptz}\"><ProfileToken>{profile}</ProfileToken><Translation><PanTilt xmlns=\"{Schema}\" x=\"{x}\" y=\"{y}\"/></Translation></RelativeMove>", TimeSpan.FromSeconds(6), ct);
	}

	public async Task<string> SubscribeAsync(CancellationToken ct)
	{
		var xml = await CallAsync(Service, $"<CreatePullPointSubscription xmlns=\"{Events}\"><InitialTerminationTime>PT120S</InitialTerminationTime></CreatePullPointSubscription>", TimeSpan.FromSeconds(6), ct);
		var address = AddressPattern().Match(xml);
		if (!address.Success) throw new HttpRequestException("ONVIF subscription without address");
		var uri = new Uri(address.Groups[1].Value);
		return new UriBuilder(uri) { Host = camera.Host }.Uri.ToString();
	}

	public async Task<IReadOnlyList<CameraSignal>> PullAsync(string subscription, CancellationToken ct) =>
		OnvifEvents.Parse(await CallAsync(subscription, $"<PullMessages xmlns=\"{Events}\"><Timeout>PT10S</Timeout><MessageLimit>20</MessageLimit></PullMessages>", TimeSpan.FromSeconds(16), ct));

	public Task RenewAsync(string subscription, CancellationToken ct) =>
		CallAsync(subscription, "<Renew xmlns=\"http://docs.oasis-open.org/wsn/b-2\"><TerminationTime>PT120S</TerminationTime></Renew>", TimeSpan.FromSeconds(6), ct);

	static double Number(string text) => double.Parse(text, CultureInfo.InvariantCulture);

	[GeneratedRegex(@"Address>([^<]+)<")]
	private static partial Regex AddressPattern();
}
