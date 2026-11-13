using Velador.Core;
using Velador.Engine.MediaMtx;
using Velador.Engine.Power;
using Velador.Engine.Sqlite;

namespace Velador.Engine.Continuity;

public sealed record CameraGuard(string CameraId, bool Online, RecordedGap? OpenGap, long? GuardSinceMs);

public sealed class ContinuityService(
	EngineSettings settings,
	EngineDatabase database,
	MediaMtxClient media,
	MediaSupervisor supervisor,
	RecordingControl recording,
	IReachability reachability,
	IPowerGuard power,
	TimeProvider time,
	ILogger<ContinuityService> log) : BackgroundService
{
	const string LastAlive = "last_alive_ms";
	const string CleanStop = "clean_stop";

	readonly Dictionary<string, Watchdog> _dogs = [];
	readonly Dictionary<string, bool> _online = [];
	readonly HashSet<string> _paused = [];
	long _lastBeatMs;

	long Now => time.GetUtcNow().ToUnixTimeMilliseconds();

	public static string WatchedSince(string cameraId) => $"watched_since:{cameraId}";

	public CameraGuard Guard(string cameraId) =>
		new(cameraId, _online.GetValueOrDefault(cameraId), database.OpenGapOf(cameraId), database.LastGapEnd(cameraId) ?? database.State(WatchedSince(cameraId)));

	public override Task StartAsync(CancellationToken ct)
	{
		var startupGap = Outage.AtStartup(database.State(LastAlive), Now, Environment.TickCount64, database.State(CleanStop) == 1, supervisor.Adopted);
		foreach (var camera in settings.Cameras)
		{
			_dogs[camera.Id] = new Watchdog();
			if (startupGap is not null) database.OpenGap(camera.Id, startupGap);
		}
		if (startupGap is not null) log.LogWarning("The engine was away since {Since}: {Cause}", startupGap.StartMs, startupGap.Cause);
		database.SetState(CleanStop, 0);
		_lastBeatMs = Now;
		database.SetState(LastAlive, _lastBeatMs);
		power.KeepAwake(settings.KeepAwake && settings.Cameras.Count > 0);
		return base.StartAsync(ct);
	}

	protected override async Task ExecuteAsync(CancellationToken ct)
	{
		using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), time);
		try
		{
			while (await timer.WaitForNextTickAsync(ct))
			{
				try
				{
					Beat();
					await WatchAsync(ct);
				}
				catch (Exception e) when (e is not OperationCanceledException)
				{
					log.LogError(e, "Continuity tick failed");
				}
			}
		}
		catch (OperationCanceledException) { }
	}

	public override async Task StopAsync(CancellationToken ct)
	{
		await base.StopAsync(ct);
		database.SetState(LastAlive, Now);
		database.SetState(CleanStop, 1);
		power.Dispose();
	}

	void Beat()
	{
		var now = Now;
		if (Outage.BetweenBeats(_lastBeatMs, now) is { } slept)
		{
			log.LogWarning("The machine slept from {From} to {To}", slept.StartMs, now);
			foreach (var camera in settings.Cameras)
			{
				database.OpenGap(camera.Id, slept);
				if (_dogs.TryGetValue(camera.Id, out var dog)) dog.Resync(now);
			}
		}
		if (now - _lastBeatMs >= Outage.HeartbeatMs || now < _lastBeatMs)
		{
			_lastBeatMs = now;
			database.SetState(LastAlive, now);
		}
	}

	async Task WatchAsync(CancellationToken ct)
	{
		power.KeepAwake(settings.KeepAwake && settings.Cameras.Count > 0);
		if (settings.Cameras.Count == 0) return;
		var paths = await media.PathsAsync(ct);
		var restart = false;
		foreach (var camera in settings.Cameras.ToList())
		{
			var path = paths?.FirstOrDefault(p => p.Name == camera.MediaPath(Camera.MainStream));
			var online = path?.Online ?? false;
			_online[camera.Id] = online;
			if (online && path!.InboundBytes > 0 && database.State(WatchedSince(camera.Id)) is null) database.SetState(WatchedSince(camera.Id), Now);
			var paused = recording.IsPaused(camera.Id);
			if (!paused && _paused.Remove(camera.Id)) _dogs.Remove(camera.Id);
			if (paused) _paused.Add(camera.Id);
			if (!_dogs.TryGetValue(camera.Id, out var dog)) dog = _dogs[camera.Id] = new Watchdog();
			var stalled = dog.InGap || !online;
			var cameraUp = !stalled || await reachability.CameraAsync(camera.Host, camera.RtspPort, ct);
			var gatewayUp = cameraUp || await reachability.GatewayAsync(ct);
			var verdict = dog.Observe(new WatchdogSample(Now, paths is not null, online, path?.InboundBytes ?? 0, cameraUp, gatewayUp));
			restart |= verdict.RestartMedia;
			if (paused) continue;
			if (verdict.Opened is { } opened)
			{
				database.OpenGap(camera.Id, opened);
				log.LogWarning("Gap opened for {Camera}: {Cause}", camera.Id, opened.Cause);
			}
			if (verdict.ClosedAtMs is { } closed)
			{
				database.CloseGap(camera.Id, closed);
				log.LogInformation("Gap closed for {Camera}", camera.Id);
			}
			else if (online && !dog.InGap && database.OpenGapOf(camera.Id) is not null && path!.InboundBytes > 0)
			{
				database.CloseGap(camera.Id, Now);
			}
		}
		if (restart) supervisor.Restart();
	}
}
