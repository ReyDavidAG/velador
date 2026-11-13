using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Velador.Core;

namespace Velador.Engine.Continuity;

public interface INetworkProbe
{
	NetworkNow? Current();
}

public sealed class SystemNetworkProbe : INetworkProbe
{
	public NetworkNow? Current()
	{
		var gateway = NetworkInterface.GetAllNetworkInterfaces()
			.Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
			.SelectMany(n => n.GetIPProperties().GatewayAddresses)
			.Select(g => g.Address)
			.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(System.Net.IPAddress.Any))
			?.ToString();
		if (gateway is null) return null;
		var mac = KnownNetworks.MacFrom(Run("arp", OperatingSystem.IsWindows() ? ["-a", gateway] : ["-n", gateway]), gateway);
		var ssid = OperatingSystem.IsWindows()
			? KnownNetworks.SsidFrom(Run("netsh", ["wlan", "show", "interfaces"]))
			: OperatingSystem.IsMacOS() ? Ssid() : null;
		return new NetworkNow(mac ?? $"gw:{gateway}", ssid ?? $"Red {gateway}");
	}

	static string? Ssid()
	{
		foreach (var device in new[] { "en0", "en1" })
			if (KnownNetworks.SsidFrom(Run("/usr/sbin/networksetup", ["-getairportnetwork", device])) is { } ssid) return ssid;
		return null;
	}

	static string Run(string program, string[] args)
	{
		try
		{
			using var process = Process.Start(new ProcessStartInfo(program, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true })!;
			var output = process.StandardOutput.ReadToEnd();
			process.WaitForExit(3000);
			return output;
		}
		catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
		{
			return "";
		}
	}
}
