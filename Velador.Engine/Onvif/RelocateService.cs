using System.Net.Sockets;
using Velador.Core;
using Velador.Engine.Continuity;
using Velador.Engine.Secrets;

namespace Velador.Engine.Onvif;

public sealed class RelocateService(EngineSettings settings, EnginePaths paths, ContinuityService continuity, CameraLinks links, ISecretStore secrets, INetworkProbe probe, HttpClient http, TimeProvider time, ILogger<RelocateService> log)
	: BackgroundService
{
	const long LostForMs = 30_000;

	string? _network;
	long _networkChangedMs;

	public NetworkNow? Network { get; private set; }

	protected override async Task ExecuteAsync(CancellationToken ct)
	{
		while (!ct.IsCancellationRequested)
		{
			Network = probe.Current();
			var changed = Network is not null && _network is not null && Network.Id != _network;
			if (changed)
			{
				_networkChangedMs = time.GetUtcNow().ToUnixTimeMilliseconds();
				log.LogWarning("Network changed to {Name}", Network!.Name);
				foreach (var camera in settings.Cameras.ToList())
					if (KnownNetworks.HostFor(settings.Networks.GetValueOrDefault(camera.Id) ?? [], Network.Id) is { } host && host != camera.Host)
						Replace(camera, camera with { Host = host }, restart: true);
			}
			if (Network is not null) _network = Network.Id;
			foreach (var camera in settings.Cameras.ToList())
			{
				try
				{
					await LookAfterAsync(camera, ct);
				}
				catch (Exception e) when (e is HttpRequestException or OperationCanceledException or System.Xml.XmlException or SocketException or IOException)
				{
					if (ct.IsCancellationRequested) return;
				}
			}
			await Task.Delay(TimeSpan.FromSeconds(10), ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
		}
	}

	async Task LookAfterAsync(Camera camera, CancellationToken ct)
	{
		var guard = continuity.Guard(camera.Id);
		if (guard.Online && guard.OpenGap is null)
		{
			Remember(camera);
			if (camera.Serial.Length == 0 && links.For(camera.Id) is { } onvif && await onvif.SerialAsync(ct) is { Length: > 0 } serial) Replace(camera, camera with { Serial = serial }, restart: false);
			return;
		}
		var now = time.GetUtcNow().ToUnixTimeMilliseconds();
		var justMoved = now - _networkChangedMs < 120_000;
		if (camera.Serial.Length == 0 || guard.Online || guard.OpenGap is not { } gap || (!justMoved && now - gap.StartMs < LostForMs)) return;
		if (await ReachesAsync(camera.Host, camera.RtspPort, ct)) return;
		if (secrets.Get(camera.Id) is not { Length: > 0 } password) return;
		var seen = new List<(string Host, int OnvifPort, string Serial)>();
		foreach (var found in await Discovery.ProbeAsync(TimeSpan.FromSeconds(3), ct))
		{
			if (settings.Cameras.Any(c => c.Id != camera.Id && c.Host == found.Host)) continue;
			try
			{
				var candidate = camera with { Host = found.Host, OnvifPort = found.OnvifPort };
				seen.Add((found.Host, found.OnvifPort, await new OnvifClient(http, candidate, password, time).SerialAsync(ct)));
			}
			catch (Exception e) when (e is HttpRequestException or OperationCanceledException or System.Xml.XmlException) { }
		}
		if (Camera.Relocated(camera, seen) is not { } place) return;
		log.LogWarning("Camera {Camera} moved from {Old} to {New}", camera.Id, camera.Host, place.Host);
		Replace(camera, camera with { Host = place.Host, OnvifPort = place.OnvifPort }, restart: true);
	}

	static async Task<bool> ReachesAsync(string host, int port, CancellationToken ct)
	{
		using var client = new TcpClient();
		using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
		limit.CancelAfter(TimeSpan.FromSeconds(2));
		try
		{
			await client.ConnectAsync(host, port, limit.Token);
			return true;
		}
		catch (Exception e) when (e is SocketException or OperationCanceledException)
		{
			ct.ThrowIfCancellationRequested();
			return false;
		}
	}

	void Remember(Camera camera)
	{
		if (Network is null) return;
		var known = settings.Networks.GetValueOrDefault(camera.Id) ?? [];
		var current = known.FirstOrDefault(k => k.Id == Network.Id);
		if (current is not null && current.Host == camera.Host && current.Name == Network.Name) return;
		settings.Networks[camera.Id] = KnownNetworks.Remember(known, new KnownNetwork(Network.Id, Network.Name, camera.Host, time.GetUtcNow().ToUnixTimeMilliseconds()));
		settings.Save(paths);
	}

	public bool Use(string cameraId, string networkId)
	{
		var camera = settings.Cameras.FirstOrDefault(c => c.Id == cameraId);
		if (camera is null || KnownNetworks.HostFor(settings.Networks.GetValueOrDefault(cameraId) ?? [], networkId) is not { } host) return false;
		if (host != camera.Host) Replace(camera, camera with { Host = host }, restart: true);
		return true;
	}

	public bool Forget(string cameraId, string networkId)
	{
		if (!settings.Networks.TryGetValue(cameraId, out var known) || known.RemoveAll(k => k.Id == networkId) == 0) return false;
		settings.Save(paths);
		return true;
	}

	void Replace(Camera old, Camera updated, bool restart)
	{
		var index = settings.Cameras.FindIndex(c => c.Id == old.Id);
		if (index < 0) return;
		settings.Cameras[index] = updated;
		settings.Save(paths);
		if (!restart) return;
		links.Forget(old.Id);
		settings.Revision++;
	}
}
