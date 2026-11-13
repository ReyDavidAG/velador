using System.Text.RegularExpressions;

namespace Velador.Core;

public sealed record KnownNetwork(string Id, string Name, string Host, long SeenMs);

public sealed record NetworkNow(string Id, string Name);

public static partial class KnownNetworks
{
	public const int Max = 3;

	public static List<KnownNetwork> Remember(IEnumerable<KnownNetwork> known, KnownNetwork now) =>
		[.. known.Where(k => k.Id != now.Id).Append(now).OrderByDescending(k => k.SeenMs).Take(Max)];

	public static string? HostFor(IEnumerable<KnownNetwork> known, string networkId) => known.FirstOrDefault(k => k.Id == networkId)?.Host;

	public static string? MacFrom(string arpOutput, string gateway)
	{
		foreach (var line in arpOutput.Split('\n'))
		{
			if (!line.Contains(gateway, StringComparison.Ordinal)) continue;
			var match = Mac().Match(line);
			if (match.Success) return string.Join(':', match.Value.Split(':', '-').Select(p => p.PadLeft(2, '0').ToLowerInvariant()));
		}
		return null;
	}

	public static string? SsidFrom(string output)
	{
		foreach (var raw in output.Split('\n'))
		{
			var line = raw.Trim();
			if (line.StartsWith("BSSID", StringComparison.OrdinalIgnoreCase)) continue;
			var at = line.IndexOf(':');
			if (at < 0) continue;
			var key = line[..at].Trim();
			var value = line[(at + 1)..].Trim();
			if (value.Length > 0 && (key.Equals("SSID", StringComparison.OrdinalIgnoreCase) || key.EndsWith("Wi-Fi Network", StringComparison.OrdinalIgnoreCase))) return value;
		}
		return null;
	}

	[GeneratedRegex(@"(?<![0-9a-fA-F])(?:[0-9a-fA-F]{1,2}[:-]){5}[0-9a-fA-F]{1,2}(?![0-9a-fA-F])")]
	private static partial Regex Mac();
}
