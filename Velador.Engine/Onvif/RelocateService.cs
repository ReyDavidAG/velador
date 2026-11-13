using System.Net.Sockets;
using Velador.Core;
using Velador.Engine.Continuity;
using Velador.Engine.Secrets;

namespace Velador.Engine.Onvif;

public sealed class RelocateService(EngineSettings settings, EnginePaths paths, ContinuityService continuity, CameraLinks links, ISecretStore secrets, HttpClient http, TimeProvider time, ILogger<RelocateService> log)
	: BackgroundService
{
	const long LostForMs = 30_000;

	protected override async Task ExecuteAsync(CancellationToken ct)
	{
		while (!ct.IsCancellationRequested)
		{
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
			await Task.Delay(TimeSpan.FromSeconds(20), ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
		}
	}

	async Task LookAfterAsync(Camera camera, CancellationToken ct)
	{
		var guard = continuity.Guard(camera.Id);
		if (guard.Online && guard.OpenGap is null)
		{
			if (camera.Serial.Length == 0 && links.For(camera.Id) is { } onvif && await onvif.SerialAsync(ct) is { Length: > 0 } serial) Replace(camera, camera with { Serial = serial }, restart: false);
			return;
		}
		var now = time.GetUtcNow().ToUnixTimeMilliseconds();
		if (camera.Serial.Length == 0 || guard.Online || guard.OpenGap is not { } gap || now - gap.StartMs < LostForMs) return;
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
