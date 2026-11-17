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
	PtzRate Command,
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
	public PathFollower Follow { get; } = new();
	public PtzRate Wanted { get; set; } = PtzRate.Zero;
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
	public PanTilt? Position { get; set; }
	public long PositionMs { get; set; }
	public bool AtLimit { get; set; }
	public TaskCompletionSource<byte[]>? Snap { get; set; }
}

public sealed record SurveyTile(int Index, double X, double Y);

public sealed class SurveyState
{
	public bool Running { get; set; }
	public int Done { get; set; }
	public int Total { get; set; }
	public long AtMs { get; set; }
	public string? Error { get; set; }
	public double TileW { get; set; }
	public double TileH { get; set; }
	public List<SurveyTile> Tiles { get; set; } = [];
}

public sealed class VisionService(
	EngineSettings settings,
	EnginePaths paths,
	MediaPorts ports,
	CameraLinks links,
	EngineDatabase database,
	RecordingControl recording,
	FaceEnhancer enhancer,
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
	const long ReturnAfterMs = 2_500;
	const long HomeAfterManualMs = 1_500;

	readonly ConcurrentDictionary<string, CameraVision> _cameras = new();
	readonly Lock _settingsLock = new();
	readonly ConcurrentDictionary<string, SurveyState> _surveys = new();
	readonly ConcurrentDictionary<string, CancellationTokenSource> _surveyStops = new();
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
		if (v is null) return new VisionSnapshot(settings.DetectPeople ? "starting" : "off", null, 0, following, false, null, false, false, PtzRate.Zero, [], Now);
		var fresh = Now - v.TracksAtMs < 2000 ? v.Tracks : [];
		return new VisionSnapshot(v.Status, v.Detail, Math.Round(v.Fps, 1), following, Now < v.ManualUntilMs, v.TargetId, v.Coasting, Now - v.ReturnedAtMs < 4000, v.Wanted, fresh, v.TracksAtMs);
	}

	public bool AtLimit(string cameraId) => _cameras.TryGetValue(cameraId, out var v) && v.AtLimit;

	public bool Busy(string cameraId) => _cameras.TryGetValue(cameraId, out var v) && v.Busy;

	public PtzZone? Zone(string cameraId)
	{
		lock (_settingsLock) return settings.Zones.GetValueOrDefault(cameraId);
	}

	public void SetZone(string cameraId, PtzZone? zone)
	{
		lock (_settingsLock)
		{
			if (zone is null) settings.Zones.Remove(cameraId);
			else settings.Zones[cameraId] = zone;
			settings.Save(paths);
		}
		if (_cameras.TryGetValue(cameraId, out var v)) v.Follow.Reset();
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
			v.ReturnHome = on && Zone(cameraId) is not null;
			v.AtLimit = false;
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
		lock (_settingsLock) return settings.Calibrations.GetValueOrDefault(cameraId) is { Version: >= PtzCalibration.Current } c ? c : null;
	}

	PtzCalibration? Known(string cameraId)
	{
		lock (_settingsLock) return settings.Calibrations.GetValueOrDefault(cameraId);
	}

	public (double X, double Y) ManualToCamera(Camera camera, double right, double up) =>
		Calibration(camera.Id) is { } c ? (right * Math.Sign(c.ViewPerX), -up * Math.Sign(c.ViewPerY)) : (right, up);

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
				if (!_rechecked) RecheckFaces();
				if (!_backfilled) BackfillFaces();
				await Task.Delay(TimeSpan.FromSeconds(5), ct);
			}
		}
		catch (OperationCanceledException) { }
	}

	long _sweptMs;
	bool _regrouped;
	bool _backfilled;

	bool _rechecked;

	void RecheckFaces()
	{
		_rechecked = true;
		const string key = "faces_rechecked";
		if (database.State(key) >= 3) return;
		foreach (var file in database.ForgetFaces())
		{
			try
			{
				File.Delete(file);
			}
			catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
		}
		database.SetState(key, 3);
	}

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
				var face = top >= 32 && faces.Find(bgr[..(w * top * 3)], w, top, 0.7) is { } hit && Core.Vision.FaceQuality.Plausible(hit.Points.Select(p => new FacePoint(p.X * w, p.Y * top)).ToList(), hit.Box.W * w) ? new Detection(hit.Box, hit.Score) : (Detection?)null;
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
			v.Wanted = PtzRate.Zero;
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
		public readonly List<FaceMoment> Moments = [];
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
			if (v.Snap is { } snap)
			{
				v.Snap = null;
				snap.TrySetResult(Png.FromBgr(lease.Frame.Scaled(SnapWidth, SnapHeight), SnapWidth, SnapHeight));
			}
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
		var region = frame.Crop(x, y, w, h);
		if (faces.Find(region, w, h, 0.7) is not { } face) return;
		var fw = face.Box.W * w;
		var fh = face.Box.H * h;
		var points = face.Points.Select(p => new FacePoint(p.X * w, p.Y * h)).ToList();
		if (fw < 10 || !Core.Vision.FaceQuality.Plausible(points, fw)) return;
		var fx0 = (int)Math.Clamp(face.Box.X * w, 0, w - 2);
		var fy0 = (int)Math.Clamp(face.Box.Y * h, 0, h - 2);
		var fw0 = (int)Math.Clamp(fw, 2, w - fx0);
		var fh0 = (int)Math.Clamp(fh, 2, h - fy0);
		var sharp = Core.Vision.FaceQuality.Sharpness(new BgrFrame(region, w, h, 0).Crop(fx0, fy0, fw0, fh0), fw0, fh0);
		var quality = Core.Vision.FaceQuality.Rank(face.Score, fw, points, sharp);
		FaceMoments.Add(candidate.Moments, new FaceMoment(frame.CapturedMs, new Box((x + face.Box.X * w) / frame.Width, (y + face.Box.Y * h) / frame.Height, fw / frame.Width, fh / frame.Height), quality));
		if (quality <= candidate.FaceQuality) return;
		candidate.FaceQuality = quality;
		if (fw < 14) return;
		var side = Math.Max(fw, fh) * 1.7;
		var cx = x + face.Box.Cx * w;
		var cy = y + face.Box.Cy * h;
		var fx = (int)Math.Clamp(cx - side / 2, 0, frame.Width - 1);
		var fy = (int)Math.Clamp(cy - side / 2, 0, frame.Height - 1);
		var fs = (int)Math.Min(side, Math.Min(frame.Width - fx, frame.Height - fy));
		if (fs < 16) return;
		candidate.Face = frame.Crop(fx, fy, fs, fs);
		(candidate.FaceW, candidate.FaceH) = (fs, fs);
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
			var id = database.SightingId(file);
			database.SetFaces(id, faceFile ?? "", null, c.Face is null ? 0 : (int)(c.FaceW / 1.7));
			if (!recording.IsPaused(camera.Id)) _ = Task.Run(() => EnhanceAsync(camera, id, Path.Combine(dir, firstMs.ToString(CultureInfo.InvariantCulture)), c));
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			log.LogWarning(e, "Could not save a sighting");
		}
	}

	async Task EnhanceAsync(Camera camera, long sightingId, string stem, Candidate c)
	{
		if (c.Moments.Count == 0 || Faces() is not { } faces) return;
		try
		{
			EnhancedFace? face = null;
			foreach (var waitMs in new[] { 2500, 4000 })
			{
				await Task.Delay(waitMs);
				foreach (var moment in c.Moments)
				{
					var side = Math.Max(moment.Box.W * 7, 0.22);
					var region = Box.FromCenter(moment.Box.Cx, moment.Box.Cy, side, side * FrameReader.Width / FrameReader.Height);
					var found = await enhancer.RunAsync(camera, faces, moment.AtMs, region, moment.Box.W, CancellationToken.None);
					if (found is not null && found.Quality > (face?.Quality ?? 0)) face = found;
				}
				if (face is not null) break;
			}
			if (face is null) return;
			var faceFile = stem + "_face.png";
			await File.WriteAllBytesAsync(faceFile, Png.FromBgr(face.Bgr, face.Width, face.Height));
			string? fusedFile = null;
			if (face.Fused is { } fused)
			{
				fusedFile = stem + "_fused.png";
				await File.WriteAllBytesAsync(fusedFile, Png.FromBgr(fused.Bgr, fused.Width, fused.Height));
			}
			database.SetFaces(sightingId, faceFile, fusedFile, (int)(face.Width / 1.8));
			log.LogInformation("Face from the main stream for sighting {Id}: {Side} px, {Frames} frames, fused {Fused}", sightingId, face.Width, face.Frames, face.Fused?.Used ?? 0);
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException or HttpRequestException or OperationCanceledException or InvalidOperationException or System.ComponentModel.Win32Exception)
		{
			log.LogWarning(e, "Could not improve the face of sighting {Id}", sightingId);
		}
	}

	void Steer(CameraVision v, IReadOnlyList<TrackState> tracks, long now)
	{
		var calibration = Calibration(v.Camera.Id);
		if (!IsFollowing(v.Camera.Id) || v.Busy || now < v.ManualUntilMs || calibration is null)
		{
			if (!v.Busy) v.Wanted = PtzRate.Zero;
			v.Follow.Reset();
			return;
		}
		var both = calibration.BothAxes ? 1 : 0.55;
		v.Follow.Configure(new FollowGeometry(calibration.ViewPerX, calibration.ViewPerY, calibration.LagS, calibration.ActS, calibration.CurveX.Top * both, calibration.CurveY.Top * both, calibration.CurveX.Top, calibration.CurveY.Top), Zone(v.Camera.Id));
		v.Wanted = v.Follow.Step(tracks, now);
		v.TargetId = v.Follow.TargetId;
		v.Coasting = v.Follow.Coasting;
		Record(v);
		if (v.TargetId is not null)
		{
			v.LastTargetMs = now;
			v.AtHome = false;
		}
		else if (!v.AtHome && !v.Coasting && (v.Home is not null || Zone(v.Camera.Id) is not null) && now - v.LastTargetMs > ReturnAfterMs)
		{
			v.AtHome = true;
			v.ReturnHome = true;
		}
	}

	const long TraceLimit = 4_000_000;
	StreamWriter? _trace;

	void Record(CameraVision v)
	{
		if (v.Follow.Trace is not { } t || t.State == "idle" && v.Wanted.IsZero) return;
		try
		{
			var file = Path.Combine(paths.Home, "follow2.csv");
			if (_trace is null || _trace.BaseStream.Length > TraceLimit)
			{
				_trace?.Dispose();
				var fresh = !File.Exists(file) || new FileInfo(file).Length > TraceLimit;
				_trace = new StreamWriter(file, append: !fresh) { AutoFlush = true };
				if (fresh) _trace.WriteLine("ms,estado,cam_x,cam_y,objetivo,imagen_x,imagen_y,persona_x,persona_vel,meta_x,meta_y,orden_x,orden_y,retraso");
			}
			_trace.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{t.AtMs},{t.State},{t.CamX:0.0000},{t.CamY:0.0000},{t.Target},{t.ImageX:0.000},{t.ImageY:0.000},{t.PersonX:0.0000},{t.PersonRate:0.000},{t.AimX:0.0000},{t.AimY:0.0000},{t.RateX:0.000},{t.RateY:0.000},{t.LagS:0.00}"));
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
	}

	async Task SendAsync(CameraVision v, CancellationToken ct)
	{
		double sentX = 0, sentY = 0;
		long sentMs = 0;
		var continuous = true;
		var turn = new AxisTurn();
		while (!ct.IsCancellationRequested)
		{
			try
			{
				await Task.Delay(60, ct);
				if (v.Busy) continue;
				var id = v.Camera.Id;
				var zone = Zone(id);
				var calibration = Calibration(id);
				var moving = sentX != 0 || sentY != 0;
				var watching = moving || Now - v.LastTargetMs < 6000 || v.Tracks.Count > 0 && Now - v.TracksAtMs < 2000;
				if (IsFollowing(id) && Now - v.PositionMs > (watching ? 110 : 1500) && links.For(id) is { } positionLink)
				{
					var asked = Now;
					if (await positionLink.PositionAsync(ct) is { } position)
					{
						v.Position = position;
						v.PositionMs = Now;
						v.Follow.Pose((asked + v.PositionMs) / 2, position.X, position.Y);
					}
				}
				if (zone is null && v.CaptureHomeAtMs > 0 && Now >= v.CaptureHomeAtMs && links.For(id) is { } homeLink)
				{
					v.CaptureHomeAtMs = 0;
					v.Home = await homeLink.PositionAsync(ct);
				}
				var home = zone is not null ? new PanTilt(zone.HomeX, zone.HomeY) : v.Home;
				if (v.ReturnHome && home is not null && links.For(id) is { } returnLink)
				{
					v.ReturnHome = false;
					v.Wanted = PtzRate.Zero;
					v.Follow.Reset();
					await returnLink.AbsoluteMoveAsync(home, ct);
					sentX = sentY = 0;
					sentMs = Now;
					v.ReturnedAtMs = Now;
					v.AtLimit = false;
					continue;
				}
				var rate = v.Wanted;
				if (zone is not null && v.Position is { } at && !rate.IsZero)
				{
					var (allowedX, allowedY) = zone.Limit(at.X, at.Y, rate.X, rate.Y, 0.4);
					v.AtLimit = (allowedX == 0 && rate.X != 0) || (allowedY == 0 && rate.Y != 0);
					rate = new PtzRate(allowedX, allowedY);
				}
				else if (rate.IsZero) v.AtLimit = false;
				if (calibration is { BothAxes: false }) rate = turn.Step(rate, Now, calibration.CurveX.Top, calibration.CurveY.Top);
				var x = calibration is null ? 0 : Math.Sign(rate.X) * calibration.TurnX * calibration.CurveX.Command(rate.X);
				var y = calibration is null ? 0 : Math.Sign(rate.Y) * calibration.TurnY * calibration.CurveY.Command(rate.Y);
				var idle = x == 0 && y == 0;
				var changed = Math.Abs(x - sentX) > 0.04 || Math.Abs(y - sentY) > 0.04 || idle != (sentX == 0 && sentY == 0);
				var keepAlive = !idle && Now - sentMs > 600;
				if (!changed && !keepAlive) continue;
				if (links.For(id) is not { } onvif) continue;
				continuous = calibration?.Continuous ?? continuous;
				if (idle) await onvif.StopAsync(ct);
				else if (continuous) await onvif.ContinuousMoveAsync(x, y, TimeSpan.FromSeconds(1), ct);
				else await onvif.MoveAsync(x * 0.06, y * 0.06, ct);
				(sentX, sentY) = (x, y);
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
		if ((sentX != 0 || sentY != 0) && links.For(v.Camera.Id) is { } link)
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
		if (v.Busy) throw new InvalidOperationException("La cámara está ocupada midiendo; espera a que termine.");
		if (links.For(camera.Id) is not { } onvif) throw new InvalidOperationException("La cámara no tiene cuenta para moverla.");
		v.Busy = true;
		v.Wanted = PtzRate.Zero;
		CalibrationLog.Clear();
		PanTilt? home = null;
		try
		{
			await onvif.StopAsync(ct);
			home = await onvif.PositionAsync(ct) ?? throw new InvalidOperationException("La cámara no dice su posición.");
			var start = Zone(camera.Id) is { } zone ? new PanTilt(zone.HomeX, zone.HomeY) : new PanTilt(Math.Clamp(home.X, -0.6, 0.6), Math.Clamp(home.Y, -0.5, 0.5));
			Note($"inicio {home}; mide desde {start}");
			await onvif.AbsoluteMoveAsync(start, ct);
			await Task.Delay(400, ct);
			double viewX, viewY;
			try
			{
				viewX = await ViewPerAsync(v, onvif, true, 0.1, ct);
				viewY = await ViewPerAsync(v, onvif, false, 0.16, ct);
			}
			catch (InvalidOperationException e) when (Known(camera.Id) is { ViewPerX: not 0, ViewPerY: not 0 } known && Math.Abs(known.ViewPerX) is > 0.5 and < 5 && Math.Abs(known.ViewPerY) is > 0.3 and < 3)
			{
				Note($"{e.Message} Se conserva la medida anterior de la imagen.");
				(viewX, viewY) = (known.ViewPerX, known.ViewPerY);
			}
			Note($"la vista se mueve {viewX:0.00} anchos a la derecha y {viewY:0.00} altos hacia abajo por unidad de posición");
			if (Math.Abs(viewX) is < 0.5 or > 5 || Math.Abs(viewY) is < 0.3 or > 3) throw new InvalidOperationException("La medición de la imagen no es creíble; repite con la cámara viendo algo quieto.");
			var continuous = true;
			double[]? ratesX = null, ratesY = null;
			var lag = 1.6;
			var actuation = 0.3;
			var bothAxes = true;
			try
			{
				ratesX = [];
				ratesY = [];
				var starts = new List<double>();
				foreach (var level in Levels)
				{
					ratesX = [.. ratesX, await RateAsync(onvif, true, level, starts, ct)];
					ratesY = [.. ratesY, await RateAsync(onvif, false, level, starts, ct)];
					Note($"orden {level:0.00}: giro {ratesX[^1]:0.000} e inclinación {ratesY[^1]:0.000} unidades por segundo");
				}
				if (Math.Abs(ratesX[^1]) < 0.01 || Math.Abs(ratesY[^1]) < 0.01) throw new InvalidOperationException("La cámara no se movió con la orden continua.");
				await onvif.AbsoluteMoveAsync(start, ct);
				await Task.Delay(400, ct);
				await SettleAsync(onvif, ct);
				await Task.Delay(SettleMs, ct);
				actuation = starts.Count == 0 ? 0.3 : Math.Clamp(starts.Order().ElementAt(starts.Count / 2), 0.05, 0.8);
				var corner = await SettleAsync(onvif, ct);
				var towardX = corner.X > 0 ? -1 : 1;
				var towardY = corner.Y > 0 ? -1 : 1;
				await onvif.ContinuousMoveAsync(0.5 * towardX * Math.Sign(ratesX[^1]), 0.5 * towardY * Math.Sign(ratesY[^1]), TimeSpan.FromSeconds(2), ct);
				await Task.Delay(1000, ct);
				await onvif.StopAsync(ct);
				var after = await SettleAsync(onvif, ct);
				bothAxes = Math.Abs(after.X - corner.X) > Math.Abs(ratesX[1]) * 0.3 && Math.Abs(after.Y - corner.Y) > Math.Abs(ratesY[1]) * 0.3;
				Note($"orden en diagonal: giro {after.X - corner.X:0.000}, inclinación {after.Y - corner.Y:0.000} → {(bothAxes ? "mueve los dos ejes a la vez" : "mueve un eje a la vez")}");
				var lags = new List<double>();
				foreach (var direction in new[] { 1, -1, 1, -1 })
				{
					lags.Add((await MeasureAsync(v, onvif, 0.5 * direction, 0, ct)).Lag);
					await Task.Delay(SettleMs, ct);
				}
				lags.Sort();
				lag = (lags[1] + lags[2]) / 2 - actuation;
				Note($"la cámara tarda {actuation:0.00} s en arrancar y el video llega {lag:0.00} s después");
			}
			catch (HttpRequestException e)
			{
				Note($"movimiento continuo rechazado: {e.Message}");
				continuous = false;
				ratesX = ratesY = null;
			}
			var topX = ratesX is null ? 0.13 : ratesX.Max(Math.Abs);
			var topY = ratesY is null ? 0.2 : ratesY.Max(Math.Abs);
			var result = new PtzCalibration(
				Math.Abs(viewX) * topX, Math.Abs(viewY) * topY, Math.Clamp(lag, 0.2, 2.5),
				viewX < 0, viewY < 0, continuous, Now,
				PtzCalibration.Current, viewX, viewY, ratesX is null ? null : Levels, ratesX, ratesY, actuation, bothAxes);
			lock (_settingsLock)
			{
				settings.Calibrations[camera.Id] = result;
				settings.Save(paths);
			}
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

	static readonly double[] Levels = [0.25, 0.5, 1.0];
	const int RunMs = 1200;
	public const int SnapWidth = 320;
	public const int SnapHeight = 180;

	async Task<PanTilt> SettleAsync(OnvifClient onvif, CancellationToken ct, int limitMs = 15000)
	{
		var last = await onvif.PositionAsync(ct) ?? throw new InvalidOperationException("La cámara no dice su posición.");
		var still = 0;
		var until = Now + limitMs;
		while (Now < until && still < 2)
		{
			await Task.Delay(250, ct);
			var p = await onvif.PositionAsync(ct) ?? last;
			still = Math.Abs(p.X - last.X) < 1e-4 && Math.Abs(p.Y - last.Y) < 1e-4 ? still + 1 : 0;
			last = p;
		}
		return last;
	}

	async Task<float[]> StillAsync(CameraVision v, CancellationToken ct)
	{
		await Task.Delay(2600, ct);
		return (await GrayAsync(v, ct)).Gray;
	}

	const double Slack = 0.04;
	const int BandTop = GrayHeight / 10;
	const int BandRows = GrayHeight * 78 / 100;

	static float[] Band(float[] gray) => gray.AsSpan(BandTop * GrayWidth, BandRows * GrayWidth).ToArray();

	async Task<double> ViewPerAsync(CameraVision v, OnvifClient onvif, bool horizontal, double step, CancellationToken ct)
	{
		var here = await SettleAsync(onvif, ct);
		var first = (horizontal ? here.X : here.Y) > 0 ? -1 : 1;
		var ratios = new List<double>();
		foreach (var direction in new[] { first, -first, first, -first })
		{
			await onvif.MoveAsync(horizontal ? Slack * direction : 0, horizontal ? 0 : Slack * direction, ct);
			await Task.Delay(400, ct);
			var from = await SettleAsync(onvif, ct);
			var before = Band(await StillAsync(v, ct));
			await onvif.MoveAsync(horizontal ? step * direction : 0, horizontal ? 0 : step * direction, ct);
			await Task.Delay(400, ct);
			var to = await SettleAsync(onvif, ct);
			var after = Band(await StillAsync(v, ct));
			var moved = horizontal ? to.X - from.X : to.Y - from.Y;
			var (slide, score) = GlobalMotion.Slide(before, after, GrayWidth, BandRows, horizontal);
			var shift = horizontal ? slide : slide * BandRows / GrayHeight;
			Note($"paso {(horizontal ? "de giro" : "de inclinación")} {step * direction:0.00}: posición {moved:0.0000}, la imagen se corrió {shift:0.000} (parecido {score:0.00})");
			if (Math.Abs(moved) < step / 4) throw new InvalidOperationException("La cámara no se movió al pedírselo.");
			if (Math.Abs(shift) >= 0.02 && score >= 0.5) ratios.Add(-shift / moved);
		}
		ratios.Sort();
		var middle = ratios.Count == 0 ? 0 : ratios[ratios.Count / 2];
		var agreeing = ratios.Where(r => Math.Abs(r - middle) <= Math.Abs(middle) * 0.2).ToList();
		if (agreeing.Count < 2) throw new InvalidOperationException("No se pudo medir cuánto se mueve la imagen: la escena cambió entre una foto y otra.");
		return agreeing.Average();
	}

	async Task<double> RateAsync(OnvifClient onvif, bool horizontal, double level, List<double> starts, CancellationToken ct)
	{
		var start = await SettleAsync(onvif, ct);
		var first = (horizontal ? start.X : start.Y) > 0 ? -1 : 1;
		double sum = 0;
		foreach (var direction in new[] { first, -first })
		{
			var samples = new List<(double T, double P)>();
			var clock = System.Diagnostics.Stopwatch.StartNew();
			await onvif.ContinuousMoveAsync(horizontal ? level * direction : 0, horizontal ? 0 : level * direction, TimeSpan.FromSeconds(3), ct);
			while (clock.ElapsedMilliseconds < RunMs)
			{
				var asked = clock.Elapsed.TotalSeconds;
				if (await onvif.PositionAsync(ct) is { } p) samples.Add(((asked + clock.Elapsed.TotalSeconds) / 2, horizontal ? p.X : p.Y));
				await Task.Delay(40, ct);
			}
			await onvif.StopAsync(ct);
			var origin = samples.Count > 0 ? samples[0].P : 0;
			if (samples.FirstOrDefault(s => Math.Abs(s.P - origin) > 0.004) is { T: > 0 } first1) starts.Add(first1.T);
			var window = samples.Where(s => s.T >= 0.4).ToList();
			if (window.Count >= 3)
			{
				var meanT = window.Average(s => s.T);
				var meanP = window.Average(s => s.P);
				var spread = window.Sum(s => (s.T - meanT) * (s.T - meanT));
				if (spread > 0) sum += window.Sum(s => (s.T - meanT) * (s.P - meanP)) / spread * direction;
			}
			await SettleAsync(onvif, ct);
		}
		return sum / 2;
	}

	public SurveyState? Survey(string cameraId)
	{
		if (_surveys.TryGetValue(cameraId, out var known)) return known;
		var index = Path.Combine(paths.Coverage, cameraId, "index.json");
		if (!File.Exists(index)) return null;
		try
		{
			var loaded = System.Text.Json.JsonSerializer.Deserialize<SurveyState>(File.ReadAllText(index), SurveyJson);
			if (loaded is null) return null;
			loaded.Running = false;
			return _surveys.GetOrAdd(cameraId, loaded);
		}
		catch (Exception e) when (e is IOException or System.Text.Json.JsonException)
		{
			return null;
		}
	}

	static readonly System.Text.Json.JsonSerializerOptions SurveyJson = new(System.Text.Json.JsonSerializerDefaults.Web);

	public string SurveyTileFile(string cameraId, int index) => Path.Combine(paths.Coverage, cameraId, $"{index}.png");

	public void StopSurvey(string cameraId)
	{
		if (_surveyStops.TryRemove(cameraId, out var stop)) stop.Cancel();
	}

	public void StartSurvey(Camera camera)
	{
		if (!_cameras.TryGetValue(camera.Id, out var v) || v.Status != "running") throw new InvalidOperationException("La detección no está corriendo: hace falta imagen para medir la cobertura.");
		if (v.Busy) throw new InvalidOperationException("La cámara está ocupada midiendo; espera a que termine.");
		if (links.For(camera.Id) is not { } onvif) throw new InvalidOperationException("La cámara no tiene cuenta para moverla.");
		var calibration = Calibration(camera.Id);
		var perX = Math.Abs(calibration?.ViewPerX ?? 2.3);
		var perY = Math.Abs(calibration?.ViewPerY ?? 1.1);
		var columns = (int)Math.Ceiling(2 / Math.Clamp(0.9 / perX, 0.2, 0.5)) + 1;
		var rows = (int)Math.Ceiling(2 / Math.Clamp(0.75 / perY, 0.25, 0.7)) + 1;
		var state = new SurveyState { Running = true, Total = columns * rows, AtMs = Now, TileW = 1 / perX, TileH = 1 / perY };
		_surveys[camera.Id] = state;
		var stop = new CancellationTokenSource();
		_surveyStops[camera.Id] = stop;
		v.Busy = true;
		v.Wanted = PtzRate.Zero;
		_ = Task.Run(() => SurveyAsync(camera, v, onvif, state, columns, rows, (calibration?.LagS ?? 1.7) + 0.7, stop.Token), CancellationToken.None);
	}

	async Task SurveyAsync(Camera camera, CameraVision v, OnvifClient onvif, SurveyState state, int columns, int rows, double waitS, CancellationToken ct)
	{
		var dir = Path.Combine(paths.Coverage, camera.Id);
		PanTilt? home = null;
		try
		{
			if (Directory.Exists(dir)) Directory.Delete(dir, true);
			Directory.CreateDirectory(dir);
			await onvif.StopAsync(ct);
			home = await onvif.PositionAsync(ct);
			for (var row = 0; row < rows; row++)
				for (var step = 0; step < columns; step++)
				{
					var column = row % 2 == 0 ? step : columns - 1 - step;
					var target = new PanTilt(-1 + 2.0 * column / (columns - 1), -1 + 2.0 * row / (rows - 1));
					await onvif.AbsoluteMoveAsync(target, ct);
					await Task.Delay(350, ct);
					var at = await SettleAsync(onvif, ct);
					await Task.Delay(TimeSpan.FromSeconds(waitS), ct);
					var snap = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
					v.Snap = snap;
					var png = await snap.Task.WaitAsync(TimeSpan.FromSeconds(8), ct);
					var index = state.Tiles.Count;
					await File.WriteAllBytesAsync(Path.Combine(dir, $"{index}.png"), png, ct);
					state.Tiles.Add(new SurveyTile(index, at.X, at.Y));
					state.Done = state.Tiles.Count;
				}
			state.AtMs = Now;
		}
		catch (OperationCanceledException)
		{
			state.Error = "Medición cancelada.";
		}
		catch (Exception e) when (e is HttpRequestException or TimeoutException or IOException or UnauthorizedAccessException or System.Xml.XmlException or InvalidOperationException)
		{
			state.Error = "La cámara dejó de responder a media medición.";
			log.LogWarning(e, "Coverage survey failed for {Camera}", camera.Id);
		}
		finally
		{
			try
			{
				if (home is not null) await onvif.AbsoluteMoveAsync(home, CancellationToken.None);
			}
			catch (Exception e) when (e is HttpRequestException or OperationCanceledException or System.Xml.XmlException) { }
			state.Running = false;
			_surveyStops.TryRemove(camera.Id, out _);
			v.Snap = null;
			v.Busy = false;
			try
			{
				File.WriteAllText(Path.Combine(dir, "index.json"), System.Text.Json.JsonSerializer.Serialize(state, SurveyJson));
			}
			catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
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
			// Without the band the printed hour and logo dominate at night and every shift reads 0.
			var (dx, dy) = GlobalMotion.Shift(Band(previous.Gray), Band(gray), GrayWidth, BandRows, 0.15);
			var d = horizontal ? dx : dy * BandRows / GrayHeight;
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
