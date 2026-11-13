using Velador.Core;
using Velador.Engine.Secrets;
using Velador.Engine.Sqlite;

namespace Velador.Engine.Onvif;

public sealed class CameraLinks(EngineSettings settings, ISecretStore secrets, HttpClient http, TimeProvider time)
{
	readonly Dictionary<string, OnvifClient> _clients = [];
	readonly Lock _lock = new();

	public void Forget(string cameraId)
	{
		lock (_lock) _clients.Remove(cameraId);
	}

	public OnvifClient? For(string cameraId)
	{
		lock (_lock)
		{
			if (_clients.TryGetValue(cameraId, out var known)) return known;
			var camera = settings.Cameras.FirstOrDefault(c => c.Id == cameraId);
			if (camera is null || secrets.Get(cameraId) is not { Length: > 0 } password) return null;
			return _clients[cameraId] = new OnvifClient(http, camera, password, time);
		}
	}
}

public sealed class EventService(EngineSettings settings, CameraLinks links, EngineDatabase database, TimeProvider time, ILogger<EventService> log) : BackgroundService
{
	const long MaxEventMs = 120_000;

	protected override async Task ExecuteAsync(CancellationToken ct)
	{
		var watching = new Dictionary<string, (CancellationTokenSource Stop, Camera Camera)>();
		while (!ct.IsCancellationRequested)
		{
			foreach (var camera in settings.Cameras.ToList())
			{
				if (watching.TryGetValue(camera.Id, out var known) && known.Camera == camera) continue;
				known.Stop?.Cancel();
				var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
				watching[camera.Id] = (stop, camera);
				_ = WatchAsync(camera, stop.Token);
			}
			foreach (var gone in watching.Keys.Where(id => settings.Cameras.All(c => c.Id != id)).ToList())
			{
				watching[gone].Stop.Cancel();
				watching.Remove(gone);
			}
			await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
		}
	}

	long Now => time.GetUtcNow().ToUnixTimeMilliseconds();

	async Task WatchAsync(Camera camera, CancellationToken ct)
	{
		database.CloseEvents(camera.Id, Now);
		while (!ct.IsCancellationRequested)
		{
			try
			{
				if (links.For(camera.Id) is not { } onvif)
				{
					await Task.Delay(TimeSpan.FromSeconds(30), ct);
					continue;
				}
				var subscription = await onvif.SubscribeAsync(ct);
				var renewed = Now;
				while (!ct.IsCancellationRequested)
				{
					foreach (var signal in await onvif.PullAsync(subscription, ct)) Apply(camera.Id, signal);
					database.CloseEventsOlderThan(camera.Id, Now - MaxEventMs, Now);
					if (Now - renewed < 60_000) continue;
					await onvif.RenewAsync(subscription, ct);
					renewed = Now;
				}
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested)
			{
				return;
			}
			catch (Exception e) when (e is HttpRequestException or OperationCanceledException or System.Xml.XmlException or IOException)
			{
				log.LogDebug(e, "Camera events unavailable for {Camera}", camera.Id);
				database.CloseEvents(camera.Id, Now);
				await Task.Delay(TimeSpan.FromSeconds(15), ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
			}
		}
	}

	void Apply(string cameraId, CameraSignal signal)
	{
		if (signal.Active) database.OpenEvent(cameraId, signal.Kind, Now);
		else database.CloseEvent(cameraId, signal.Kind, Now);
	}
}
