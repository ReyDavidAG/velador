namespace Velador.Core.Vision;

public readonly record struct PtzRate(double X, double Y)
{
	public static readonly PtzRate Zero = new(0, 0);
	public bool IsZero => X == 0 && Y == 0;

}

public sealed class AxisTurn(double boost = AxisTurn.Boost)
{
	public const double Boost = 1.5;
	public const int HoldMs = 800;
	public const int LongMs = 1600;
	const double PanFirst = 1.3;

	bool _pan = true;
	long _sinceMs;

	public PtzRate Step(PtzRate rate, long nowMs, double maxX, double maxY)
	{
		if (rate.X == 0 || rate.Y == 0)
		{
			if (!rate.IsZero && _pan != (rate.X != 0))
			{
				_pan = rate.X != 0;
				_sinceMs = nowMs;
			}
			return rate;
		}
		var pan = Math.Abs(rate.X) / maxX * PanFirst;
		var tilt = Math.Abs(rate.Y) / maxY;
		var (mine, other) = _pan ? (pan, tilt) : (tilt, pan);
		var held = nowMs - _sinceMs;
		if (held >= HoldMs && other > mine || held >= LongMs && other > 0.3)
		{
			_pan = !_pan;
			_sinceMs = nowMs;
		}
		return _pan ? new PtzRate(Math.Clamp(rate.X * boost, -maxX, maxX), 0) : new PtzRate(0, Math.Clamp(rate.Y * boost, -maxY, maxY));
	}
}

public sealed record FollowGeometry(double ViewPerX, double ViewPerY, double LagS, double ActuationS, double MaxRateX, double MaxRateY, double TopRateX = 0, double TopRateY = 0);

public sealed record FollowTrace(long AtMs, string State, double CamX, double CamY, int? Target, double ImageX, double ImageY, double PersonX, double PersonRate, double AimX, double AimY, double RateX, double RateY, double LagS);

public sealed class PathFollower
{
	const double AimInBox = 0.3;
	const double AimInImage = 0.45;
	const double GainX = 1.1;
	const double GainY = 1.0;
	const double DeadX = 0.07;
	const double DeadY = 0.12;
	const double AccelX = 1.2;
	const double AccelY = 1.0;
	const double CoastS = 3.0;
	const double MinTrackS = 0.8;
	const double MinRate = 0.012;
	const double MaxPersonRate = 0.3;
	const double MaxOffTheRoute = 0.15;
	const double ReacquireWidths = 0.35;
	const long HistoryMs = 8000;
	const long MaxBackdateMs = 1000;
	const double PoseDoubtS = 0.25;
	static readonly double[] LagOffsets = [-0.45, -0.3, -0.15, 0, 0.15, 0.3, 0.45];

	readonly Lock _lock = new();
	readonly List<(long AtMs, double X, double Y)> _poses = [];
	readonly KalmanAxis?[] _guesses = new KalmanAxis?[LagOffsets.Length];
	readonly double[] _misses = new double[LagOffsets.Length];
	int _best = LagOffsets.Length / 2;
	FollowGeometry? _geometry;
	PtzZone? _zone;
	KalmanAxis? _person;
	int? _targetId;
	long _seenStamp = -1;
	long _measuredMs = -1;
	long _firstMs = -1;
	long _lastStepMs = -1;
	double _offRoute;
	double _freeY;
	bool _movingX, _movingY;

	public PtzRate Rate { get; private set; } = PtzRate.Zero;
	public bool Coasting { get; private set; }
	public int? TargetId => _targetId;
	public FollowTrace? Trace { get; private set; }

	public void Configure(FollowGeometry geometry, PtzZone? zone)
	{
		lock (_lock)
		{
			_geometry = geometry;
			_zone = zone;
		}
	}

	public void Pose(long atMs, double x, double y)
	{
		lock (_lock)
		{
			if (_poses.Count > 0 && _geometry is { TopRateX: > 0, TopRateY: > 0 } g)
			{
				var last = _poses[^1];
				var needMs = (long)Math.Min(MaxBackdateMs, 1000 * Math.Max(Math.Abs(x - last.X) / (g.TopRateX * 1.15), Math.Abs(y - last.Y) / (g.TopRateY * 1.15)));
				var startMs = atMs - needMs;
				if (startMs < last.AtMs && PoseAt(startMs) is { } start)
				{
					_poses.RemoveAll(p => p.AtMs >= startMs);
					_poses.Add((startMs, start.X, start.Y));
				}
			}
			_poses.Add((atMs, x, y));
			while (_poses.Count > 2 && _poses[1].AtMs < atMs - HistoryMs) _poses.RemoveAt(0);
		}
	}

	public void Reset()
	{
		lock (_lock)
		{
			Rate = PtzRate.Zero;
			Coasting = false;
			Drop();
			_seenStamp = _lastStepMs = -1;
		}
	}

	void Drop()
	{
		_person = null;
		Array.Clear(_guesses);
		_targetId = null;
		_measuredMs = _firstMs = -1;
		_offRoute = _freeY = 0;
		_movingX = _movingY = false;
	}

	(double X, double Y)? PoseAt(long atMs)
	{
		if (_poses.Count == 0) return null;
		if (atMs <= _poses[0].AtMs) return (_poses[0].X, _poses[0].Y);
		for (var i = 1; i < _poses.Count; i++)
		{
			if (atMs > _poses[i].AtMs) continue;
			var (a, b) = (_poses[i - 1], _poses[i]);
			var f = (atMs - a.AtMs) / (double)Math.Max(1, b.AtMs - a.AtMs);
			return (a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f);
		}
		return (_poses[^1].X, _poses[^1].Y);
	}

	public PtzRate Step(IReadOnlyList<TrackState> tracks, long nowMs)
	{
		lock (_lock)
		{
			if (_geometry is not { } g || PoseAt(nowMs) is not { } cam) return Rate = PtzRate.Zero;
			var dt = _lastStepMs < 0 ? 0.1 : Math.Clamp((nowMs - _lastStepMs) / 1000.0, 0.02, 0.5);
			_lastStepMs = nowMs;
			var lagS = g.LagS + LagOffsets[_best];
			var lagMs = (long)(lagS * 1000);
			var target = Pick(tracks, g, nowMs - lagMs);
			if (target is not null && target.SeenNow && target.LastSeenMs != _seenStamp && PoseAt(nowMs - lagMs) is { } then)
			{
				_seenStamp = target.LastSeenMs;
				var x = then.X + (target.Box.Cx - 0.5) / g.ViewPerX;
				var y = then.Y + (target.Box.Y + target.Box.H * AimInBox - AimInImage) / g.ViewPerY;
				var capturedMs = nowMs - lagMs;
				var fresh = _person is null || _targetId != target.Id && Math.Abs(_person.Position - x) > ReacquireWidths / Math.Abs(g.ViewPerX);
				var elapsed = Math.Clamp((capturedMs - _measuredMs) / 1000.0, 0.01, 1);
				var turning = g.TopRateX > 0 && PoseAt(capturedMs + 100) is { } after && PoseAt(capturedMs - 100) is { } before ? Math.Abs(after.X - before.X) / 0.2 : 0;
				var doubt = turning * PoseDoubtS * (turning * PoseDoubtS);
				for (var k = 0; k < LagOffsets.Length; k++)
				{
					var at = PoseAt(nowMs - lagMs - (long)((LagOffsets[k] - LagOffsets[_best]) * 1000)) ?? then;
					var seen = at.X + (target.Box.Cx - 0.5) / g.ViewPerX;
					if (fresh || _guesses[k] is not { } guess)
					{
						_guesses[k] = new KalmanAxis(seen, 0.08, 0.0004);
						continue;
					}
					guess.Predict(elapsed);
					var miss = seen - guess.Position;
					_misses[k] = _misses[k] * 0.985 + miss * miss;
					guess.Update(seen, doubt);
				}
				var smoothest = Array.IndexOf(_misses, _misses.Min());
				if (!fresh && _misses[smoothest] < _misses[_best] * 0.7 && _misses[_best] > 1e-6) _best = smoothest;
				_person = _guesses[_best];
				var off = _zone is null ? 0 : Math.Clamp(y - _zone.Route(x), -MaxOffTheRoute, MaxOffTheRoute);
				if (fresh)
				{
					_firstMs = capturedMs;
					_offRoute = off;
					_freeY = y;
				}
				else
				{
					_offRoute += 0.15 * (off - _offRoute);
					_freeY += 0.3 * (y - _freeY);
				}
				_targetId = target.Id;
				_measuredMs = capturedMs;
			}
			if (_person is null || _measuredMs < 0)
			{
				Coasting = false;
				Trace = new FollowTrace(nowMs, "idle", cam.X, cam.Y, null, 0, 0, 0, 0, cam.X, cam.Y, 0, 0, lagS);
				return Rate = Slow(PtzRate.Zero, dt);
			}
			var sinceS = (nowMs - lagMs - _measuredMs) / 1000.0;
			var lost = sinceS > 0.6;
			if (lost && sinceS > CoastS)
			{
				Drop();
				Coasting = false;
				return Rate = Slow(PtzRate.Zero, dt);
			}
			var walk = Math.Clamp(_person.Velocity, -MaxPersonRate, MaxPersonRate);
			var steady = (_measuredMs - _firstMs) / 1000.0 >= MinTrackS && Math.Abs(walk) >= MinRate;
			Coasting = lost && steady;
			if (!steady) walk = 0;
			var ahead = _person.Position + walk * (sinceS + lagS + g.ActuationS);
			var aimX = _zone is null ? ahead : Math.Clamp(ahead, _zone.MinX, _zone.MaxX);
			var aimY = _zone is null ? _freeY : Math.Clamp(_zone.Route(ahead) + _offRoute, _zone.MinY, _zone.MaxY);
			var errorX = aimX - cam.X;
			var errorY = aimY - cam.Y;
			var deadX = DeadX / Math.Abs(g.ViewPerX);
			var deadY = DeadY / Math.Abs(g.ViewPerY);
			if (!_movingX && (Math.Abs(errorX) > deadX || walk != 0)) _movingX = true;
			else if (_movingX && Math.Abs(errorX) < deadX / 2 && walk == 0) _movingX = false;
			if (!_movingY && Math.Abs(errorY) > deadY) _movingY = true;
			else if (_movingY && Math.Abs(errorY) < deadY / 2) _movingY = false;
			var wanted = new PtzRate(
				_movingX ? Math.Clamp(walk + GainX * errorX, -g.MaxRateX * 0.9, g.MaxRateX * 0.9) : 0,
				_movingY ? Math.Clamp(GainY * errorY, -g.MaxRateY * 0.7, g.MaxRateY * 0.7) : 0);
			if (_zone is not null)
			{
				var (lx, ly) = _zone.Limit(cam.X, cam.Y, wanted.X, wanted.Y, 0.35);
				wanted = new PtzRate(lx, ly);
			}
			Rate = Slow(wanted, dt);
			Trace = new FollowTrace(nowMs, Coasting ? "coast" : lost ? "hold" : "track", cam.X, cam.Y, _targetId, target?.Box.Cx ?? 0, target?.Box.Cy ?? 0, _person.Position, _person.Velocity, aimX, aimY, Rate.X, Rate.Y, lagS);
			return Rate;
		}
	}

	PtzRate Slow(PtzRate wanted, double dt)
	{
		var x = Rate.X + Math.Clamp(wanted.X - Rate.X, -AccelX * dt, AccelX * dt);
		var y = Rate.Y + Math.Clamp(wanted.Y - Rate.Y, -AccelY * dt, AccelY * dt);
		if (wanted.X == 0 && Math.Abs(x) < 0.02) x = 0;
		if (wanted.Y == 0 && Math.Abs(y) < 0.02) y = 0;
		return new PtzRate(x, y);
	}

	TrackState? Pick(IReadOnlyList<TrackState> tracks, FollowGeometry g, long capturedMs)
	{
		if (_targetId is { } id && tracks.FirstOrDefault(t => t.Id == id) is { } same) return same;
		var seen = tracks.Where(t => t.SeenNow).ToList();
		if (_person is not null && _measuredMs >= 0 && PoseAt(capturedMs) is { } then)
		{
			var expected = _person.Position + Math.Clamp(_person.Velocity, -MaxPersonRate, MaxPersonRate) * (capturedMs - _measuredMs) / 1000.0;
			var near = seen
				.Select(t => (Track: t, Distance: Math.Abs(then.X + (t.Box.Cx - 0.5) / g.ViewPerX - expected) * Math.Abs(g.ViewPerX)))
				.Where(c => c.Distance <= ReacquireWidths)
				.OrderBy(c => c.Distance)
				.FirstOrDefault();
			return near.Track;
		}
		return seen.OrderByDescending(t => t.Box.Area * t.Score).FirstOrDefault();
	}
}
