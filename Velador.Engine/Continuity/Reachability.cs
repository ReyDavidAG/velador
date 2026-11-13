using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Velador.Engine.Continuity;

public interface IReachability
{
	Task<bool> CameraAsync(string host, int port, CancellationToken ct);
	Task<bool> GatewayAsync(CancellationToken ct);
}

public sealed class NetworkReachability : IReachability
{
	public async Task<bool> CameraAsync(string host, int port, CancellationToken ct)
	{
		using var client = new TcpClient();
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
		timeout.CancelAfter(TimeSpan.FromSeconds(1));
		try
		{
			await client.ConnectAsync(host, port, timeout.Token);
			return true;
		}
		catch (Exception e) when (e is SocketException or OperationCanceledException)
		{
			return false;
		}
	}

	public async Task<bool> GatewayAsync(CancellationToken ct)
	{
		var gateway = NetworkInterface.GetAllNetworkInterfaces()
			.Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
			.SelectMany(n => n.GetIPProperties().GatewayAddresses)
			.Select(g => g.Address)
			.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
		if (gateway is null) return false;
		try
		{
			using var ping = new Ping();
			return (await ping.SendPingAsync(gateway, TimeSpan.FromSeconds(1), cancellationToken: ct)).Status == IPStatus.Success;
		}
		catch (Exception e) when (e is PingException or InvalidOperationException or PlatformNotSupportedException)
		{
			return true;
		}
	}
}
