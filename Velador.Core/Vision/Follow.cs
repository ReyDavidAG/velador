namespace Velador.Core.Vision;

public sealed record FollowTuning(
	double GainX = 0.30,
	double GainY = 0.25,
	double LagS = 0.8,
	double HorizonS = 0.0,
	double SettleS = 1.0,
	double DeadZoneX = 0.08,
	double DeadZoneY = 0.15,
	double AimY = 0.42,
	double MaxSpeed = 0.8,
	double MaxAccel = 2.5,
	double MinSpeed = 0.12,
	double TiltBelowHeight = 0.6,
	double WorldNoise = 0.3,
	double CoastS = 4.0,
	double ReacquireDistance = 0.3,
	double SearchSpeed = 0.7,
	double EdgeOffset = 0.2);

public readonly record struct PtzSpeed(double X, double Y)
{
	public static readonly PtzSpeed Zero = new(0, 0);
	public bool IsZero => X == 0 && Y == 0;
}

public sealed class FollowController(FollowTuning? tuning = null)
{
	readonly FollowTuning _t = tuning ?? new FollowTuning();
	readonly List<(long AtMs, PtzSpeed Speed)> _sent = [];
	(long AtMs, double X, double Y) _base = (-1, 0, 0);
	long _lastMs = -1;
	long _seenMs = -1;
	int? _targetId;
	KalmanAxis? _wx, _wy;
	bool _engagedX, _engagedY;
	double _lastImageX;

	public long LastTargetMs { get; private set; } = -1;

	public PtzSpeed Command { get; private set; } = PtzSpeed.Zero;
	public FollowTuning Tuning => _t;

	public static TrackState? PickTarget(IReadOnlyList<TrackState> tracks, int? currentId)
	{
		if (currentId is { } id && tracks.FirstOrDefault(t => t.Id == id) is { } same) return same;
		return tracks.Where(t => t.SeenNow).OrderByDescending(t => t.Box.Area * t.Score).FirstOrDefault();
	}

	public bool Coasting { get; private set; }

	public TrackState? Pick(IReadOnlyList<TrackState> tracks, long nowMs)
	{
		if (_targetId is { } id && tracks.FirstOrDefault(t => t.Id == id) is { } same) return same;
		var seen = tracks.Where(t => t.SeenNow).ToList();
		if (_wx is not null && _lastSeenAtMs >= 0 && nowMs - _lastSeenAtMs <= _t.CoastS * 1000)
		{
			var (seenX, _) = Travel(nowMs - (long)(_t.LagS * 1000));
			var near = seen
				.Select(t => (Track: t, Distance: Math.Abs(t.Box.Cx - 0.5 + _t.GainX * seenX - _wx.Position)))
				.Where(c => c.Distance <= _t.ReacquireDistance)
				.OrderBy(c => c.Distance)
				.FirstOrDefault();
			if (near.Track is not null)
			{
				_targetId = near.Track.Id;
				return near.Track;
			}
			return null;
		}
		return seen.OrderByDescending(t => t.Box.Area * t.Score).FirstOrDefault();
	}

	public PtzSpeed Step(TrackState? target, long nowMs)
	{
		var dt = _lastMs < 0 ? 0.1 : Math.Clamp((nowMs - _lastMs) / 1000.0, 0.02, 0.5);
		_lastMs = nowMs;
		if (_base.AtMs < 0) _base = (nowMs, 0, 0);
		Coasting = false;
		var desired = target is not null ? Desired(target, nowMs, dt) : Coast(nowMs, dt);
		var step = _t.MaxAccel * dt;
		if (Math.Sign(desired.X) == -Math.Sign(Command.X) && Math.Abs(desired.X) < 0.3) desired = desired with { X = 0 };
		if (Math.Sign(desired.Y) == -Math.Sign(Command.Y) && Math.Abs(desired.Y) < 0.3) desired = desired with { Y = 0 };
		var x = Command.X + Math.Clamp(desired.X - Command.X, -step, step);
		var y = Command.Y + Math.Clamp(desired.Y - Command.Y, -step, step);
		if (desired.X == 0 && Math.Abs(x) < _t.MinSpeed) x = 0;
		if (desired.Y == 0 && Math.Abs(y) < _t.MinSpeed) y = 0;
		Command = new PtzSpeed(x, y);
		_sent.Add((nowMs, Command));
		Prune(nowMs - (long)(_t.LagS * 1000) - 3000);
		return Command;
	}

	public void Reset()
	{
		Command = PtzSpeed.Zero;
		_sent.Clear();
		_base = (-1, 0, 0);
		_lastMs = -1;
		_targetId = null;
		_wx = _wy = null;
		_engagedX = _engagedY = false;
		_lastSeenAtMs = -1;
		_lastImageX = 0;
	}

	long _lastSeenAtMs = -1;

	PtzSpeed Coast(long nowMs, double dt)
	{
		if (_wx is null || _wy is null || _lastSeenAtMs < 0 || nowMs - _lastSeenAtMs > _t.CoastS * 1000)
		{
			_wx = _wy = null;
			_targetId = null;
			_engagedX = _engagedY = false;
			return PtzSpeed.Zero;
		}
		Coasting = true;
		_wx.Predict(dt);
		_wy.Predict(dt);
		if (Math.Abs(_lastImageX) > _t.EdgeOffset) return new PtzSpeed(Math.Sign(_lastImageX) * _t.SearchSpeed, 0);
		var (nowX, _) = Travel(nowMs);
		return new PtzSpeed(Axis(_wx, _t.GainX * nowX, _t.GainX, _t.DeadZoneX, ref _engagedX), 0);
	}

	PtzSpeed Desired(TrackState target, long nowMs, double dt)
	{
		var lagMs = (long)(_t.LagS * 1000);
		var (seenX, seenY) = Travel(nowMs - lagMs);
		var (nowX, nowY) = Travel(nowMs);
		var aimY = target.Box.Y + target.Box.H * 0.3;
		var worldX = target.Box.Cx - 0.5 + _t.GainX * seenX;
		var worldY = aimY - _t.AimY + _t.GainY * seenY;
		if (_wx is null || _wy is null || (_targetId != target.Id && Math.Abs(worldX - _wx.Position) > _t.ReacquireDistance))
		{
			_targetId = target.Id;
			_wx = new KalmanAxis(worldX, _t.WorldNoise, 0.0004);
			_wy = new KalmanAxis(worldY, _t.WorldNoise, 0.0004);
			_seenMs = -1;
		}
		_wx.Predict(dt);
		_wy.Predict(dt);
		_targetId = target.Id;
		if (target.SeenNow && target.LastSeenMs != _seenMs)
		{
			_seenMs = target.LastSeenMs;
			_lastSeenAtMs = nowMs;
			LastTargetMs = nowMs;
			_lastImageX = target.Box.Cx - 0.5;
			_wx.Update(worldX);
			_wy.Update(worldY);
		}
		var x = Axis(_wx, _t.GainX * nowX, _t.GainX, _t.DeadZoneX, ref _engagedX);
		var y = target.Box.H > _t.TiltBelowHeight ? 0 : Axis(_wy, _t.GainY * nowY, _t.GainY, _t.DeadZoneY, ref _engagedY);
		return new PtzSpeed(x, y);
	}

	double Axis(KalmanAxis world, double viewNow, double gain, double deadZone, ref bool engaged)
	{
		var walking = Math.Abs(world.Velocity) >= gain * _t.MinSpeed;
		var speed = walking ? Math.Clamp(world.Velocity, -_t.MaxSpeed * gain * 1.5, _t.MaxSpeed * gain * 1.5) : 0;
		var ahead = world.Position + speed * (_t.LagS + _t.HorizonS) - viewNow;
		if (!engaged && (Math.Abs(ahead) > deadZone || walking)) engaged = true;
		else if (engaged && Math.Abs(ahead) < deadZone / 2 && !walking) engaged = false;
		if (!engaged) return 0;
		var wanted = (speed + ahead / _t.SettleS) / gain;
		var command = Math.Clamp(wanted, -_t.MaxSpeed, _t.MaxSpeed);
		if (Math.Abs(command) < _t.MinSpeed / 2) return 0;
		return Math.Abs(command) < _t.MinSpeed ? Math.Sign(command) * _t.MinSpeed : command;
	}

	(double X, double Y) Travel(long untilMs)
	{
		double x = _base.X, y = _base.Y;
		var cursor = _base.AtMs;
		var current = PtzSpeed.Zero;
		foreach (var (at, speed) in _sent)
		{
			if (at > untilMs) break;
			if (at > cursor)
			{
				x += current.X * (at - cursor) / 1000.0;
				y += current.Y * (at - cursor) / 1000.0;
				cursor = at;
			}
			current = speed;
		}
		if (untilMs > cursor)
		{
			x += current.X * (untilMs - cursor) / 1000.0;
			y += current.Y * (untilMs - cursor) / 1000.0;
		}
		return (x, y);
	}

	void Prune(long beforeMs)
	{
		while (_sent.Count > 1 && _sent[1].AtMs < beforeMs)
		{
			var (x, y) = Travel(_sent[1].AtMs);
			_base = (_sent[1].AtMs, x, y);
			_sent.RemoveAt(0);
		}
	}
}

public static class PtzOrientation
{
	public static (double X, double Y) ToCamera(double x, double y, int rotation) => rotation == 180 ? (-x, -y) : (x, y);
}
