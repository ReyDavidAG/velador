using System.Diagnostics;
using Velador.Core;
using Velador.Engine.Secrets;
using Velador.Engine.Sqlite;

namespace Velador.Engine.MediaMtx;

public sealed class MediaSupervisor(EnginePaths paths, EngineSettings settings, Storage storage, MediaPorts ports, MediaMtxClient client, ISecretStore secrets, EngineDatabase database, TimeProvider time, ILogger<MediaSupervisor> log)
	: BackgroundService
{
	Process? _process;
	bool _ranBefore;
	GapCause _restartCause = GapCause.Engine;
	int _configured;

	public bool Adopted { get; private set; }
	public bool Missing { get; private set; }

	public override async Task StartAsync(CancellationToken ct)
	{
		Adopted = await client.PathsAsync(ct) is not null;
		_ranBefore = Adopted;
		if (Adopted) storage.Activate();
		_configured = Signature();
		await base.StartAsync(ct);
	}

	protected override async Task ExecuteAsync(CancellationToken ct)
	{
		while (!ct.IsCancellationRequested)
		{
			var running = IsRunning();
			if (running && (storage.Desired != storage.Active || Signature() != _configured))
			{
				_restartCause = storage.Desired != storage.Active ? GapCause.Disk : GapCause.Update;
				Restart();
				running = false;
			}
			if (settings.Cameras.Count > 0 && !running && await client.PathsAsync(ct) is null && !ct.IsCancellationRequested) Start();
			await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
		}
	}

	public override async Task StopAsync(CancellationToken ct)
	{
		await base.StopAsync(ct);
		if (File.Exists(paths.Handover)) return;
		Kill();
	}

	public void Restart()
	{
		log.LogWarning("Restarting the media server");
		Kill();
	}

	int Signature() => settings.Revision;

	bool IsRunning() => _process is { HasExited: false } || OwnedPid() is not null;

	void Start()
	{
		Missing = !File.Exists(paths.MediaBinary);
		if (Missing)
		{
			log.LogError("Media server binary not found at {Path}", paths.MediaBinary);
			return;
		}
		var recordings = storage.Activate();
		_configured = Signature();
		File.WriteAllText(paths.MediaConfig, MediaMtxConfig.Build(ports, recordings, settings.Cameras));
		var info = new ProcessStartInfo(paths.MediaBinary, [paths.MediaConfig])
		{
			WorkingDirectory = paths.Home,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		};
		foreach (var (key, value) in MediaMtxConfig.SourceEnvironment(settings.Cameras, c => secrets.Get(c.Id))) info.Environment[key] = value;
		_process = Process.Start(info);
		if (_process is null) return;
		Adopted = false;
		if (_ranBefore)
		{
			var now = time.GetUtcNow().ToUnixTimeMilliseconds();
			foreach (var camera in settings.Cameras) database.OpenGap(camera.Id, new RecordedGap(now - 2000, null, _restartCause));
		}
		_ranBefore = true;
		_restartCause = GapCause.Engine;
		if (!storage.OnFallback) _ = Task.Run(() =>
		{
			var moved = storage.Relocate();
			if (moved > 0) log.LogInformation("Moved {Count} recordings back to the external disk", moved);
		});
		File.WriteAllText(paths.MediaPid, _process.Id.ToString());
		_process.OutputDataReceived += (_, e) => Forward(e.Data);
		_process.ErrorDataReceived += (_, e) => Forward(e.Data);
		_process.BeginOutputReadLine();
		_process.BeginErrorReadLine();
		log.LogInformation("Media server started with pid {Pid}", _process.Id);
	}

	void Forward(string? line)
	{
		if (line is null) return;
		if (line.Contains(" ERR ") || line.Contains(" WAR ")) log.LogWarning("mediamtx: {Line}", line);
	}

	Process? OwnedPid()
	{
		if (!File.Exists(paths.MediaPid) || !int.TryParse(File.ReadAllText(paths.MediaPid), out var pid)) return null;
		try
		{
			var process = Process.GetProcessById(pid);
			return !process.HasExited && process.ProcessName.Contains("mediamtx", StringComparison.OrdinalIgnoreCase) ? process : null;
		}
		catch (ArgumentException)
		{
			return null;
		}
	}

	void Kill()
	{
		foreach (var process in new[] { _process, OwnedPid() })
		{
			try
			{
				if (process is { HasExited: false }) process.Kill();
			}
			catch (InvalidOperationException) { }
		}
		_process = null;
		File.Delete(paths.MediaPid);
	}
}
