using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace Velador.Engine.Onvif;

public sealed record FoundCamera(string Host, int OnvifPort);

public static partial class Discovery
{
	public static async Task<IReadOnlyList<FoundCamera>> ProbeAsync(TimeSpan wait, CancellationToken ct)
	{
		var probe = $"""
			<e:Envelope xmlns:e="http://www.w3.org/2003/05/soap-envelope" xmlns:w="http://schemas.xmlsoap.org/ws/2004/08/addressing" xmlns:d="http://schemas.xmlsoap.org/ws/2005/04/discovery" xmlns:dn="http://www.onvif.org/ver10/network/wsdl"><e:Header><w:MessageID>uuid:{Guid.NewGuid()}</w:MessageID><w:To>urn:schemas-xmlsoap-org:ws:2005:04:discovery</w:To><w:Action>http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</w:Action></e:Header><e:Body><d:Probe><d:Types>dn:NetworkVideoTransmitter</d:Types></d:Probe></e:Body></e:Envelope>
			""";
		var found = new Dictionary<string, FoundCamera>();
		try
		{
			using var udp = new UdpClient(AddressFamily.InterNetwork);
			udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
			var bytes = Encoding.UTF8.GetBytes(probe);
			await udp.SendAsync(bytes, new IPEndPoint(IPAddress.Parse("239.255.255.250"), 3702), ct);
			using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
			limit.CancelAfter(wait);
			while (true)
			{
				var reply = await udp.ReceiveAsync(limit.Token);
				foreach (var camera in Parse(Encoding.UTF8.GetString(reply.Buffer))) found[camera.Host] = camera;
			}
		}
		catch (Exception e) when (e is OperationCanceledException or SocketException) { }
		return [.. found.Values.OrderBy(f => f.Host)];
	}

	public static IEnumerable<FoundCamera> Parse(string reply)
	{
		foreach (Match match in XAddr().Matches(reply))
			if (IPAddress.TryParse(match.Groups[1].Value, out _))
				yield return new FoundCamera(match.Groups[1].Value, match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 80);
	}

	[GeneratedRegex(@"http://(\d{1,3}(?:\.\d{1,3}){3})(?::(\d{1,5}))?/onvif/")]
	private static partial Regex XAddr();
}
