namespace Velador.Core.Vision;

public readonly record struct Box(double X, double Y, double W, double H)
{
	public double Cx => X + W / 2;
	public double Cy => Y + H / 2;
	public double Area => Math.Max(0, W) * Math.Max(0, H);

	public static Box FromCenter(double cx, double cy, double w, double h) => new(cx - w / 2, cy - h / 2, w, h);

	public double IoU(Box other)
	{
		var ix = Math.Max(0, Math.Min(X + W, other.X + other.W) - Math.Max(X, other.X));
		var iy = Math.Max(0, Math.Min(Y + H, other.Y + other.H) - Math.Max(Y, other.Y));
		var inter = ix * iy;
		var union = Area + other.Area - inter;
		return union <= 0 ? 0 : inter / union;
	}

	public bool TouchesEdge(double margin = 0.01) => X <= margin || Y <= margin || X + W >= 1 - margin || Y + H >= 1 - margin;
}

public readonly record struct Detection(Box Box, double Score);

public sealed record TrackState(int Id, Box Box, double Vx, double Vy, double Score, bool SeenNow, long FirstSeenMs, long LastSeenMs);

sealed class KalmanAxis(double position, double processNoise, double measurementNoise)
{
	double _p00 = 0.01, _p01, _p11 = 1;

	public double Position { get; private set; } = position;
	public double Velocity { get; private set; }

	public void Predict(double dt)
	{
		Position += Velocity * dt;
		var q = processNoise;
		var p00 = _p00 + dt * (2 * _p01 + dt * _p11) + q * dt * dt * dt / 3;
		var p01 = _p01 + dt * _p11 + q * dt * dt / 2;
		var p11 = _p11 + q * dt;
		(_p00, _p01, _p11) = (p00, p01, p11);
	}

	public void Update(double z)
	{
		var s = _p00 + measurementNoise;
		var k0 = _p00 / s;
		var k1 = _p01 / s;
		var y = z - Position;
		Position += k0 * y;
		Velocity += k1 * y;
		(_p00, _p01, _p11) = ((1 - k0) * _p00, (1 - k0) * _p01, _p11 - k1 * _p01);
	}
}

public sealed class Tracker(TrackerTuning? tuning = null)
{
	readonly TrackerTuning _t = tuning ?? new TrackerTuning();
	readonly List<Track> _tracks = [];
	int _nextId = 1;
	long _lastMs = -1;

	sealed class Track(int id, Detection d, long nowMs, TrackerTuning t)
	{
		public int Id { get; } = id;
		public KalmanAxis X { get; } = new(d.Box.Cx, t.ProcessNoise, t.MeasurementNoise);
		public KalmanAxis Y { get; } = new(d.Box.Cy, t.ProcessNoise, t.MeasurementNoise);
		public double W { get; set; } = d.Box.W;
		public double H { get; set; } = d.Box.H;
		public double Score { get; set; } = d.Score;
		public int Hits { get; set; } = 1;
		public long FirstSeenMs { get; } = nowMs;
		public long LastSeenMs { get; set; } = nowMs;
		public bool SeenNow { get; set; } = true;
		public Box Box => Box.FromCenter(X.Position, Y.Position, W, H);
		public bool Confirmed => Hits >= t.ConfirmHits;
	}

	public IReadOnlyList<TrackState> Update(IReadOnlyList<Detection> detections, long nowMs)
	{
		var dt = _lastMs < 0 ? 0 : Math.Clamp((nowMs - _lastMs) / 1000.0, 0, 2);
		_lastMs = nowMs;
		foreach (var track in _tracks)
		{
			track.X.Predict(dt);
			track.Y.Predict(dt);
			track.SeenNow = false;
		}
		var high = detections.Where(d => d.Score >= _t.HighScore).ToList();
		var low = detections.Where(d => d.Score >= _t.LowScore && d.Score < _t.HighScore).ToList();
		var unmatched = Associate(_tracks.OrderByDescending(t => t.Confirmed).ThenByDescending(t => t.Hits).ToList(), high, nowMs, _t.MatchIoU);
		Associate(unmatched, low, nowMs, _t.MatchIoU + 0.1);
		foreach (var d in high)
			if (!_tracks.Any(t => t.SeenNow && t.Box.IoU(d.Box) > 0.7))
				_tracks.Add(new Track(_nextId++, d, nowMs, _t));
		_tracks.RemoveAll(t => nowMs - t.LastSeenMs > (t.Confirmed ? _t.KeepLostMs : 0) && !t.SeenNow);
		return _tracks.Where(t => t.Confirmed).Select(t => new TrackState(t.Id, Clamp(t.Box), t.X.Velocity, t.Y.Velocity, t.Score, t.SeenNow, t.FirstSeenMs, t.LastSeenMs)).ToList();
	}

	List<Track> Associate(List<Track> tracks, List<Detection> detections, long nowMs, double minIoU)
	{
		var pairs = new List<(double Cost, Track Track, Detection Det)>();
		foreach (var track in tracks)
			foreach (var d in detections)
			{
				var box = track.Box;
				var iou = box.IoU(d.Box);
				var reach = Math.Max(box.W, box.H) * 0.75;
				var dist = Math.Sqrt(Math.Pow(box.Cx - d.Box.Cx, 2) + Math.Pow(box.Cy - d.Box.Cy, 2));
				if (iou >= minIoU) pairs.Add((1 - iou, track, d));
				else if (dist <= reach) pairs.Add((1 + dist / reach, track, d));
			}
		var usedTracks = new HashSet<Track>();
		var usedDets = new HashSet<int>();
		foreach (var (_, track, d) in pairs.OrderBy(p => p.Cost))
		{
			var index = detections.IndexOf(d);
			if (usedTracks.Contains(track) || usedDets.Contains(index)) continue;
			usedTracks.Add(track);
			usedDets.Add(index);
			track.X.Update(d.Box.Cx);
			track.Y.Update(d.Box.Cy);
			track.W += (d.Box.W - track.W) * 0.5;
			track.H += (d.Box.H - track.H) * 0.5;
			track.Score = d.Score;
			track.Hits++;
			track.LastSeenMs = nowMs;
			track.SeenNow = true;
		}
		for (var i = detections.Count - 1; i >= 0; i--)
			if (usedDets.Contains(i)) detections.RemoveAt(i);
		return tracks.Where(t => !usedTracks.Contains(t)).ToList();
	}

	static Box Clamp(Box b)
	{
		var x = Math.Clamp(b.X, -0.5, 1);
		var y = Math.Clamp(b.Y, -0.5, 1);
		return new Box(x, y, b.W, b.H);
	}
}

public sealed record TrackerTuning(
	double HighScore = 0.5,
	double LowScore = 0.15,
	double MatchIoU = 0.2,
	int ConfirmHits = 3,
	long KeepLostMs = 1500,
	double ProcessNoise = 0.5,
	double MeasurementNoise = 0.0004);
