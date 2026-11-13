using Velador.Core;
using Velador.Engine.MediaMtx;
using Velador.Engine.Sqlite;

namespace Velador.Engine.Continuity;

public sealed record PauseState(bool Paused, long? ResumeAtMs);

public sealed class RecordingControl(EngineSettings settings, EnginePaths paths, EngineDatabase database, MediaMtxClient media, TimeProvider time, ILogger<RecordingControl> log) : BackgroundService
{
	public const int MaxMinutes = 24 * 60;

	readonly Lock _lock = new();

	long Now => time.GetUtcNow().ToUnixTimeMilliseconds();

	public PauseState State(string cameraId)
	{
		lock (_lock) return settings.Paused.TryGetValue(cameraId, out var at) ? new PauseState(true, at > 0 ? at : null) : new PauseState(false, null);
	}

	public bool IsPaused(string cameraId)
	{
		lock (_lock) return settings.Paused.ContainsKey(cameraId);
	}

	public async Task<PauseState> PauseAsync(Camera camera, int? minutes, CancellationToken ct)
	{
		var now = Now;
		lock (_lock)
		{
			settings.Paused[camera.Id] = minutes is > 0 ? now + minutes.Value * 60_000L : 0;
			settings.Save(paths);
		}
		if (database.OpenGapOf(camera.Id) is { Cause: not GapCause.Paused }) database.CloseGap(camera.Id, now);
		database.OpenGap(camera.Id, new RecordedGap(now, null, GapCause.Paused));
		await ApplyAsync(camera, record: false, ct);
		log.LogInformation("Recording paused for {Camera}", camera.Id);
		return State(camera.Id);
	}

	public async Task<PauseState> ResumeAsync(Camera camera, CancellationToken ct)
	{
		lock (_lock)
		{
			if (settings.Paused.Remove(camera.Id)) settings.Save(paths);
		}
		if (database.OpenGapOf(camera.Id) is { Cause: GapCause.Paused }) database.CloseGap(camera.Id, Now);
		await ApplyAsync(camera, record: true, ct);
		log.LogInformation("Recording resumed for {Camera}", camera.Id);
		return State(camera.Id);
	}

	async Task ApplyAsync(Camera camera, bool record, CancellationToken ct)
	{
		foreach (var stream in new[] { Camera.MainStream, Camera.MinorStream })
			if (!await media.RecordAsync(camera.MediaPath(stream), record, ct))
				log.LogWarning("The media server did not take the recording change for {Path}; it applies on its next start", camera.MediaPath(stream));
	}

	protected override async Task ExecuteAsync(CancellationToken ct)
	{
		using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5), time);
		try
		{
			while (await timer.WaitForNextTickAsync(ct))
			{
				List<string> due;
				lock (_lock)
				{
					foreach (var gone in settings.Paused.Keys.Where(id => settings.Cameras.All(c => c.Id != id)).ToList()) settings.Paused.Remove(gone);
					due = settings.Paused.Where(p => p.Value > 0 && p.Value <= Now).Select(p => p.Key).ToList();
				}
				foreach (var id in due)
					if (settings.Cameras.FirstOrDefault(c => c.Id == id) is { } camera) await ResumeAsync(camera, ct);
			}
		}
		catch (OperationCanceledException) { }
	}
}
