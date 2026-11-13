using System.Collections.Concurrent;
using System.Globalization;
using Velador.Core;
using Velador.Core.Vision;
using Velador.Engine.Continuity;
using Velador.Engine.Onvif;
using Velador.Engine.Sqlite;

namespace Velador.Engine.Vision;

public sealed record VisionSnapshot(
	string Status,
	string? Detail,
	double Fps,
	bool Following,
	bool ManualHold,
	int? TargetId,
	bool Coasting,
	bool Returning,
	PtzSpeed Command,
	IReadOnlyList<TrackState> Tracks,
	long AtMs);

public sealed class CameraVision
{
	public Camera Camera { get; set; } = null!;
	public string Status { get; set; } = "off";
	public string? Detail { get; set; }
	public double Fps { get; set; }
	public IReadOnlyList<TrackState> Tracks { get; set; } = [];
	public long TracksAtMs { get; set; }
	public int? TargetId { get; set; }
	public long ManualUntilMs { get; set; }
	public FollowController Follow { get; set; } = new();
	public PtzSpeed Wanted { get; set; } = PtzSpeed.Zero;
	public float[]? LastGray { get; set; }
	public long LastGrayMs { get; set; }
	public TaskCompletionSource<bool> NextFrame { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
	public bool Busy { get; set; }
	public bool Coasting { get; set; }
	public PanTilt? Home { get; set; }
	public long CaptureHomeAtMs { get; set; }
	public long LastTargetMs { get; set; }
	public bool AtHome { get; set; } = true;
	public bool ReturnHome { get; set; }
	public long ReturnedAtMs { get; set; }
}

public sealed class VisionService(
	EngineSettings settings,
	EnginePaths paths,
	MediaPorts ports,
	CameraLinks links,
	EngineDatabase database,
	RecordingControl recording,
	TimeProvider time,
	ILogger<VisionService> log) : BackgroundService
{
	public const int GrayWidth = 320;
	public const int GrayHeight = 180;
	const long IdleFrameMs = 330;
	const long ActiveForMs = 5000;
	const long PersonGoneMs = 4000;
	const long MinSightingMs = 800;
	const long ManualHoldMs = 10_000;
	const long ReturnAfterMs = 8_000;
	const long HomeAfterManualMs = 1_500;

	readonly ConcurrentDictionary<string, CameraVision> _cameras = new();
	readonly Lock _settingsLock = new();
	PersonDetector? _detector;
	string? _detectorFailure;
	FaceDetector? _faces;
	bool _facesTried;

	FaceDetector? Faces()
	{
		if (_facesTried) return _faces;
		_facesTried = true;
		if (EnginePaths.FaceModel() is not { } model) return null;
		try
		{
			_faces = new FaceDetector(model);
		}
		catch (Exception e)
		{
			log.LogError(e, "The face model did not load");
		}
		return _faces;
	}

	long Now => time.GetUtcNow().ToUnixTimeMilliseconds();

	public string? Device => _detector?.Device;

	public VisionSnapshot Snapshot(string cameraId)
	{
		var v = _cameras.GetValueOrDefault(cameraId);
		var following = IsFollowing(cameraId);
		if (v is null) return new VisionSnapshot(settings.DetectPeople ? "starting" : "off", null, 0, following, false, null, false, false, PtzSpeed.Zero, [], Now);
		var fresh = Now - v.TracksAtMs < 2000 ? v.Tracks : [];
		return new VisionSnapshot(v.Status, v.Detail, Math.Round(v.Fps, 1), following, Now < v.ManualUntilMs, v.TargetId, v.Coasting, Now - v.ReturnedAtMs < 4000, v.Wanted, fresh, v.TracksAtMs);
	}

	public bool IsFollowing(string cameraId)
	{
		lock (_settingsLock) return settings.Follow.Contains(cameraId);
	}

	public void SetFollow(string cameraId, bool on)
	{
		lock (_settingsLock)
		{
			settings.Follow.Remove(cameraId);
			if (on) settings.Follow.Add(cameraId);
			settings.Save(paths);
		}
		if (_cameras.TryGetValue(cameraId, out var v))
		{
			v.Follow.Reset();
			v.TargetId = null;
			v.ManualUntilMs = 0;
			v.CaptureHomeAtMs = on ? Now : 0;
			v.AtHome = true;
			v.ReturnHome = false;
		}
	}

	public void ManualMove(string cameraId)
	{
		if (!_cameras.TryGetValue(cameraId, out var v)) return;
		v.ManualUntilMs = Now + ManualHoldMs;
		if (IsFollowing(cameraId)) v.CaptureHomeAtMs = Now + HomeAfterManualMs;
		v.AtHome = true;
		v.ReturnHome = false;
	}

	public PtzCalibration? Calibration(string cameraId)
	{
		lock (_settingsLock) return settings.Calibrations.GetValueOrDefault(cameraId);
	}

	public (double X, double Y) ToCamera(Camera camera, double x, double y)
	{
		if (Calibration(camera.Id) is { } c) return (c.InvertX ? -x : x, c.InvertY ? -y : y);
		return (x, -y);
	}

	public (double X, double Y) ManualToCamera(Camera camera, double right, double up) =>
		Calibration(camera.Id) is null ? (right, up) : ToCamera(camera, right, -up);

	protected override async Task ExecuteAsync(CancellationToken ct)
	{
		var running = new Dictionary<string, (CancellationTokenSource Stop, Camera Camera)>();
		try
		{
			while (!ct.IsCancellationRequested)
			{
				foreach (var camera in settings.Cameras.ToList())
				{
					if (running.TryGetValue(camera.Id, out var known) && known.Camera with { Serial = camera.Serial } == camera) continue;
					known.Stop?.Cancel();
					var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
					running[camera.Id] = (stop, camera);
					var vision = _cameras.GetOrAdd(camera.Id, _ => new CameraVision());
					vision.Camera = camera;
					_ = Task.Run(() => RunAsync(vision, stop.Token), CancellationToken.None);
					_ = Task.Run(() => SendAsync(vision, stop.Token), CancellationToken.None);
				}
				foreach (var gone in running.Keys.Where(id => settings.Cameras.All(c => c.Id != id)).ToList())
				{
					running[gone].Stop.Cancel();
					running.Remove(gone);
					_cameras.TryRemove(gone, out _);
				}
				Sweep();
				if (!_regrouped) Regroup();
				if (!_backfilled) BackfillFaces();
				await Task.Delay(TimeSpan.FromSeconds(5), ct);
			}
		}
		catch (OperationCanceledException) { }
	}

	long _sweptMs;
	bool _regrouped;
	bool _backfilled;

	void BackfillFaces()
	{
		_backfilled = true;
		if (Faces() is not { } faces) return;
		var found = 0;
		foreach (var (id, file) in database.SightingsWithoutFace())
		{
			try
			{
				var (w, h, bgr) = Png.ToBgr(File.ReadAllBytes(file));
				var top = h / 2;
				var face = top >= 32 ? faces.Best(bgr[..(w * top * 3)], w, top) : null;
				if (face is null || face.Value.Box.W * w < 14)
				{
					database.SetFace(id, "");
					continue;
				}
				var frame = new BgrFrame(bgr, w, h, 0);
				var side = Math.Max(face.Value.Box.W * w, face.Value.Box.H * top) * 1.7;
				var fx = (int)Math.Clamp(face.Value.Box.Cx * w - side / 2, 0, w - 1);
				var fy = (int)Math.Clamp(face.Value.Box.Cy * top - side / 2, 0, h - 1);
				var fs = (int)Math.Min(side, Math.Min(w - fx, h - fy));
				var faceFile = Path.ChangeExtension(file, null) + "_face.png";
				File.WriteAllBytes(faceFile, Png.FromBgr(frame.Crop(fx, fy, fs, fs), fs, fs));
				database.SetFace(id, faceFile);
				found++;
			}
			catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or IndexOutOfRangeException or ArgumentException)
			{
				database.SetFace(id, "");
			}
		}
		if (found > 0) log.LogInformation("Found {Count} faces in saved sightings", found);
	}

	void Regroup()
	{
		_regrouped = true;
		foreach (var day in database.SightingsToRegroup().GroupBy(s => (s.CameraId, s.Day)))
		{
			var described = new List<(EngineDatabase.StoredSighting, float[])>();
			foreach (var s in day)
			{
				try
				{
					var descriptor = s.Descriptor;
					if (descriptor is null)
					{
						var (w, h, bgr) = Png.ToBgr(File.ReadAllBytes(s.File));
						descriptor = Appearance.Describe(bgr, w, h);
					}
					described.Add((s, descriptor));
				}
				catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or IndexOutOfRangeException) { }
			}
			if (described.Count == 0) continue;
			database.Regroup(day.Key.CameraId, day.Key.Day, described, PeopleGrouping.Cluster(described.Select(d => d.Item2).ToList()));
			log.LogInformation("Regrouped {Count} sightings of {Day}", described.Count, day.Key.Day);
		}
	}

	void Sweep()
	{
		if (Now - _sweptMs < 3_600_000) return;
		_sweptMs = Now;
		foreach (var file in database.RemoveSightingsBefore(Now - settings.RetentionDays * 86_400_000L))
		{
			try
			{
				File.Delete(file);
			}
			catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
		}
	}

	PersonDetector? Detector()
	{
		if (_detector is not null || _detectorFailure is not null) return _detector;
		if (EnginePaths.PersonModel() is not { } model)
		{
			_detectorFailure = "no_model";
			return null;
		}
		try
		{
			_detector = new PersonDetector(model);
		}
		catch (Exception e)
		{
			log.LogError(e, "The person model did not load");
			_detectorFailure = "model_error";
		}
		return _detector;
	}

	async Task RunAsync(CameraVision v, CancellationToken ct)
	{
		while (!ct.IsCancellationRequested)
		{
			if (!settings.DetectPeople)
			{
				v.Status = "off";
				v.Tracks = [];
				await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
				continue;
			}
			var ffmpeg = EnginePaths.Ffmpeg();
			var detector = Detector();
			if (ffmpeg is null || detector is null)
			{
				v.Status = ffmpeg is null ? "no_ffmpeg" : _detectorFailure ?? "no_model";
				await Task.Delay(TimeSpan.FromSeconds(15), ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
				continue;
			}
			v.Status = "starting";
			v.Detail = null;
			if (IsFollowing(v.Camera.Id) && v.Home is null) v.CaptureHomeAtMs = Now + 3000;
			using (var reader = FrameReader.Start(ffmpeg, $"rtsp://127.0.0.1:{ports.Rtsp}/{v.Camera.MediaPath(Camera.MinorStream)}", v.Camera.Rotation, time))
			{
				try
				{
					await ProcessAsync(v, reader, detector, ct);
				}
				catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
				catch (Exception e)
				{
					log.LogError(e, "Vision failed for {Camera}", v.Camera.Id);
					v.Detail = e.Message;
				}
				if (!ct.IsCancellationRequested && v.Status != "off")
				{
					v.Status = "reconnecting";
					v.Detail ??= reader.LastError;
				}
			}
			v.Tracks = [];
			v.Wanted = PtzSpeed.Zero;
			await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
		}
	}

	sealed class Candidate
	{
		public byte[] Crop = [];
		public int W, H;
		public double Quality, Score;
		public byte[]? Face;
		public int FaceW, FaceH;
		public double FaceQuality;
		public int Looks;
	}

	async Task ProcessAsync(CameraVision v, FrameReader reader, PersonDetector detector, CancellationToken ct)
	{
		var tracker = new Tracker();
		var best = new Dictionary<int, Candidate>();
		var spans = new Dictionary<int, (long First, long Last)>();
		long lastDetectMs = 0, lastPersonMs = 0, fpsWindowMs = Now;
		var fpsCount = 0;
		var personOpen = false;
		while (!ct.IsCancellationRequested && settings.DetectPeople)
		{
			using var lease = await reader.NextAsync(ct);
			if (lease is null) return;
			var now = lease.CapturedMs;
			var active = IsFollowing(v.Camera.Id) || v.Busy || now - lastPersonMs < ActiveForMs;
			if (v.Busy || IsFollowing(v.Camera.Id))
			{
				v.LastGray = lease.Frame.Gray(GrayWidth, GrayHeight);
				v.LastGrayMs = now;
				var tcs = v.NextFrame;
				v.NextFrame = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
				tcs.TrySetResult(true);
			}
			if (!active && now - lastDetectMs < IdleFrameMs) continue;
			lastDetectMs = now;
			var frame = lease.Frame;
			v.Status = "running";
			var tracks = tracker.Update(detector.Detect(frame), now);
			v.Tracks = tracks;
			v.TracksAtMs = now;
			fpsCount++;
			if (now - fpsWindowMs >= 2000)
			{
				v.Fps = fpsCount * 1000.0 / (now - fpsWindowMs);
				fpsCount = 0;
				fpsWindowMs = now;
			}
			var paused = recording.IsPaused(v.Camera.Id);
			if (tracks.Any(t => t.SeenNow))
			{
				lastPersonMs = now;
				if (!personOpen && !paused)
				{
					database.OpenEvent(v.Camera.Id, EventKind.Person, now);
					personOpen = true;
				}
			}
			else if (personOpen && now - lastPersonMs > PersonGoneMs)
			{
				database.CloseEvent(v.Camera.Id, EventKind.Person, now);
				personOpen = false;
			}
			foreach (var track in tracks.Where(t => t.SeenNow))
			{
				spans[track.Id] = spans.TryGetValue(track.Id, out var s) ? (s.First, now) : (now, now);
				if (paused) continue;
				Consider(best, frame, track);
			}
			foreach (var gone in spans.Keys.Where(id => tracks.All(t => t.Id != id)).ToList())
			{
				var (first, last) = spans[gone];
				spans.Remove(gone);
				if (best.Remove(gone, out var candidate) && candidate.Crop.Length > 0 && last - first >= MinSightingMs) Save(v.Camera, candidate, first, last);
			}
			Steer(v, tracks, now);
		}
		if (personOpen) database.CloseEvent(v.Camera.Id, EventKind.Person, Now);
	}

	void Consider(Dictionary<int, Candidate> best, BgrFrame frame, TrackState track)
	{
		var box = track.Box;
		var ratio = box.H / Math.Max(0.001, box.W) * frame.Height / frame.Width;
		var quality = track.Score * Math.Sqrt(box.Area) * (box.TouchesEdge() ? 0.4 : 1) * (ratio is > 1.4 and < 4.5 ? 1 : 0.6);
		if (!best.TryGetValue(track.Id, out var candidate)) best[track.Id] = candidate = new Candidate();
		if (candidate.Quality * 1.1 < quality)
		{
			var (x, y, w, h) = frame.PixelRect(box, 0.08);
			if (w >= 24 && h >= 48)
			{
				candidate.Crop = frame.Crop(x, y, w, h);
				(candidate.W, candidate.H, candidate.Quality, candidate.Score) = (w, h, quality, quality);
			}
		}
		if (candidate.Looks++ % 3 == 0) LookForFace(candidate, frame, box);
	}

	void LookForFace(Candidate candidate, BgrFrame frame, Box person)
	{
		if (person.H * frame.Height < 90 || Faces() is not { } faces) return;
		var (x, y, w, h) = frame.PixelRect(new Box(person.X - person.W * 0.1, person.Y - person.H * 0.05, person.W * 1.2, person.H * 0.5));
		if (w < 32 || h < 32) return;
		if (faces.Best(frame.Crop(x, y, w, h), w, h) is not { } face) return;
		var fw = face.Box.W * w;
		var fh = face.Box.H * h;
		var quality = face.Score * Math.Sqrt(fw * fh);
		if (fw < 14 || quality <= candidate.FaceQuality) return;
		var side = Math.Max(fw, fh) * 1.7;
		var cx = x + face.Box.Cx * w;
		var cy = y + face.Box.Cy * h;
		var fx = (int)Math.Clamp(cx - side / 2, 0, frame.Width - 1);
		var fy = (int)Math.Clamp(cy - side / 2, 0, frame.Height - 1);
		var fs = (int)Math.Min(side, Math.Min(frame.Width - fx, frame.Height - fy));
		if (fs < 16) return;
		candidate.Face = frame.Crop(fx, fy, fs, fs);
		(candidate.FaceW, candidate.FaceH, candidate.FaceQuality) = (fs, fs, quality);
	}

	void Save(Camera camera, Candidate c, long firstMs, long lastMs)
	{
		try
		{
			var day = DateTimeOffset.FromUnixTimeMilliseconds(firstMs).ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
			var dir = Path.Combine(paths.Sightings, camera.Id, day);
			Directory.CreateDirectory(dir);
			var file = Path.Combine(dir, $"{firstMs}.png");
			File.WriteAllBytes(file, Png.FromBgr(c.Crop, c.W, c.H));
			string? faceFile = null;
			if (c.Face is not null)
			{
				faceFile = Path.Combine(dir, $"{firstMs}_face.png");
				File.WriteAllBytes(faceFile, Png.FromBgr(c.Face, c.FaceW, c.FaceH));
			}
			var descriptor = Appearance.Describe(c.Crop, c.W, c.H);
			var person = PeopleGrouping.Match(descriptor, database.PeopleOn(camera.Id, day));
			database.AddSighting(camera.Id, day, person, descriptor, new TimeRange(firstMs, lastMs), file, c.Score, faceFile);
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			log.LogWarning(e, "Could not save a sighting");
		}
	}

	void Steer(CameraVision v, IReadOnlyList<TrackState> tracks, long now)
	{
		if (!IsFollowing(v.Camera.Id) || v.Busy || now < v.ManualUntilMs)
		{
			if (!v.Busy) v.Wanted = PtzSpeed.Zero;
			v.Follow.Reset();
			return;
		}
		var calibration = Calibration(v.Camera.Id);
		if (calibration is not null && (v.Follow.Tuning.GainX != calibration.GainX || v.Follow.Tuning.GainY != calibration.GainY || v.Follow.Tuning.LagS != calibration.LagS))
			v.Follow = new FollowController(new FollowTuning(GainX: calibration.GainX, GainY: calibration.GainY, LagS: calibration.LagS));
		var target = v.Follow.Pick(tracks, now);
		v.Wanted = v.Follow.Step(target, now);
		v.TargetId = target?.Id;
		v.Coasting = v.Follow.Coasting;
		if (target is not null)
		{
			v.LastTargetMs = now;
			v.AtHome = false;
		}
		else if (!v.AtHome && !v.Coasting && v.Home is not null && now - v.LastTargetMs > ReturnAfterMs)
		{
			v.AtHome = true;
			v.ReturnHome = true;
		}
	}

	async Task SendAsync(CameraVision v, CancellationToken ct)
	{
		var sent = PtzSpeed.Zero;
		long sentMs = 0;
		var continuous = true;
		while (!ct.IsCancellationRequested)
		{
			try
			{
				await Task.Delay(60, ct);
				if (v.Busy) continue;
				if (v.CaptureHomeAtMs > 0 && Now >= v.CaptureHomeAtMs && links.For(v.Camera.Id) is { } homeLink)
				{
					v.CaptureHomeAtMs = 0;
					v.Home = await homeLink.PositionAsync(ct);
				}
				if (v.ReturnHome && v.Home is { } home && links.For(v.Camera.Id) is { } returnLink)
				{
					v.ReturnHome = false;
					v.Wanted = PtzSpeed.Zero;
					v.Follow.Reset();
					await returnLink.AbsoluteMoveAsync(home, ct);
					sent = PtzSpeed.Zero;
					sentMs = Now;
					v.ReturnedAtMs = Now;
					continue;
				}
				var wanted = v.Wanted;
				var changed = Math.Abs(wanted.X - sent.X) > 0.05 || Math.Abs(wanted.Y - sent.Y) > 0.05 || wanted.IsZero != sent.IsZero;
				var keepAlive = !wanted.IsZero && Now - sentMs > 600;
				if (!changed && !keepAlive) continue;
				if (links.For(v.Camera.Id) is not { } onvif) continue;
				var (x, y) = ToCamera(v.Camera, wanted.X, wanted.Y);
				continuous = Calibration(v.Camera.Id)?.Continuous ?? continuous;
				if (wanted.IsZero) await onvif.StopAsync(ct);
				else if (continuous) await onvif.ContinuousMoveAsync(x, y, TimeSpan.FromSeconds(1), ct);
				else await onvif.MoveAsync(x * 0.06, y * 0.06, ct);
				sent = wanted;
				sentMs = Now;
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested)
			{
				break;
			}
			catch (Exception e) when (e is HttpRequestException or OperationCanceledException or System.Xml.XmlException)
			{
				if (!v.Wanted.IsZero) log.LogDebug(e, "PTZ command failed for {Camera}", v.Camera.Id);
				await Task.Delay(500, CancellationToken.None);
			}
		}
		if (!sent.IsZero && links.For(v.Camera.Id) is { } link)
		{
			try
			{
				await link.StopAsync(CancellationToken.None);
			}
			catch (Exception e) when (e is HttpRequestException or OperationCanceledException or System.Xml.XmlException) { }
		}
	}

	public async Task<PtzCalibration> CalibrateAsync(Camera camera, CancellationToken ct)
	{
		if (!_cameras.TryGetValue(camera.Id, out var v) || v.Status is not ("running" or "starting")) throw new InvalidOperationException("La detección no está corriendo: hace falta imagen para calibrar.");
		if (links.For(camera.Id) is not { } onvif) throw new InvalidOperationException("La cámara no tiene cuenta para moverla.");
		v.Busy = true;
		v.Wanted = PtzSpeed.Zero;
		CalibrationLog.Clear();
		PanTilt? home = null;
		try
		{
			await onvif.StopAsync(ct);
			home = await onvif.PositionAsync(ct);
			Note($"inicio {home}");
			await Task.Delay(SettleMs, ct);
			var continuous = true;
			(double Gain, double Lag, bool Invert)? x = null, y = null;
			try
			{
				x = await AxisAsync(v, onvif, 0.5, 0, ct);
				Note($"pan: ganancia {x.Value.Gain:0.000}, retraso {x.Value.Lag:0.00} s, invertido {x.Value.Invert}");
				y = await AxisAsync(v, onvif, 0, 0.3, ct);
				Note($"tilt: ganancia {y.Value.Gain:0.000}, retraso {y.Value.Lag:0.00} s, invertido {y.Value.Invert}");
			}
			catch (HttpRequestException e)
			{
				Note($"movimiento continuo rechazado: {e.Message}");
				continuous = false;
			}
			var result = new PtzCalibration(
				x?.Gain ?? 0.3, y?.Gain ?? 0.25,
				Math.Clamp(((x?.Lag ?? 0.8) + (y?.Lag ?? 0.8)) / 2, 0.2, 2),
				x?.Invert ?? false, y?.Invert ?? false, continuous, Now);
			lock (_settingsLock)
			{
				settings.Calibrations[camera.Id] = result;
				settings.Save(paths);
			}
			v.Follow = new FollowController(new FollowTuning(GainX: result.GainX, GainY: result.GainY, LagS: result.LagS));
			return result;
		}
		finally
		{
			try
			{
				await onvif.StopAsync(CancellationToken.None);
				if (home is not null) await onvif.AbsoluteMoveAsync(home, CancellationToken.None);
				await Task.Delay(1500, CancellationToken.None);
				Note($"regreso {await onvif.PositionAsync(CancellationToken.None)}");
			}
			catch (Exception e) when (e is HttpRequestException or OperationCanceledException or System.Xml.XmlException)
			{
				Note($"no pudo regresar: {e.Message}");
			}
			v.Busy = false;
		}
	}

	const int SettleMs = 2500;
	const int MaxLagMs = 5000;
	const int SampleMs = 1000;

	public List<string> CalibrationLog { get; } = [];

	void Note(string line)
	{
		lock (CalibrationLog) CalibrationLog.Add(line);
		log.LogInformation("Calibration: {Line}", line);
	}

	async Task<(double Gain, double Lag, bool Invert)> AxisAsync(CameraVision v, OnvifClient onvif, double sx, double sy, CancellationToken ct)
	{
		var there = await MeasureAsync(v, onvif, sx, sy, ct);
		await Task.Delay(SettleMs, ct);
		var back = await MeasureAsync(v, onvif, -sx, -sy, ct);
		await Task.Delay(SettleMs, ct);
		var speed = Math.Abs(sx + sy);
		var rate = (there.Rate + back.Rate) / 2;
		var gain = Math.Abs(rate) / speed;
		if (gain < 0.02) throw new InvalidOperationException("La imagen no se movió al girar la cámara.");
		return (gain, (there.Lag + back.Lag) / 2, rate < 0);
	}

	async Task<(double Rate, double Lag)> MeasureAsync(CameraVision v, OnvifClient onvif, double sx, double sy, CancellationToken ct)
	{
		var horizontal = sx != 0;
		var from = await onvif.PositionAsync(ct);
		var before = await GrayAsync(v, ct);
		var startMs = Now;
		await onvif.ContinuousMoveAsync(sx, sy, TimeSpan.FromSeconds(8), ct);
		var previous = before;
		double total = 0;
		long? movedMs = null;
		long steadyFromMs = 0;
		double steady = 0;
		while (true)
		{
			var (gray, atMs) = await GrayAsync(v, ct);
			if (movedMs is null && atMs - startMs > MaxLagMs) break;
			if (movedMs is not null && atMs >= steadyFromMs + SampleMs) break;
			var (dx, dy) = GlobalMotion.Shift(previous.Gray, gray, GrayWidth, GrayHeight, 0.15);
			var d = horizontal ? dx : dy;
			if (movedMs is null && atMs - startMs > 300 && Math.Abs(d) > 0.004)
			{
				movedMs = atMs;
				steadyFromMs = atMs + 300;
			}
			if (movedMs is not null && atMs >= steadyFromMs) steady += d;
			total += d;
			previous = (gray, atMs);
		}
		var endMs = previous.AtMs;
		await onvif.StopAsync(ct);
		var to = await onvif.PositionAsync(ct);
		Note($"comando ({sx}, {sy}): posición {from?.X:0.000},{from?.Y:0.000} → {to?.X:0.000},{to?.Y:0.000}; la imagen se movió a los {(movedMs is null ? "nunca" : $"{movedMs - startMs} ms")}, desplazamiento {total:0.000}, estable {steady:0.000}");
		if (movedMs is null) throw new InvalidOperationException("La imagen no se movió al girar la cámara.");
		var seconds = Math.Max(0.2, (endMs - steadyFromMs) / 1000.0);
		var motion = -steady / seconds;
		return (motion * Math.Sign(horizontal ? sx : sy), (movedMs.Value - startMs) / 1000.0);
	}

	static async Task<(float[] Gray, long AtMs)> GrayAsync(CameraVision v, CancellationToken ct)
	{
		await v.NextFrame.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
		return (v.LastGray!, v.LastGrayMs);
	}

	public override async Task StopAsync(CancellationToken ct)
	{
		await base.StopAsync(ct);
		_detector?.Dispose();
		_faces?.Dispose();
	}
}
