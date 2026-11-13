namespace Velador.Core.Vision;

public sealed record FollowTuning(
	double GainX = 0.30,
	double GainY = 0.25,
	double LagS = 0.8,
	double HorizonS = 0.2,
	double SettleS = 0.7,
	double DeadZoneX = 0.08,
	double DeadZoneY = 0.12,
	double AimY = 0.42,
	double MaxSpeed = 1.0,
	double MaxAccel = 4.0,
	double MinSpeed = 0.12);

public readonly record struct PtzSpeed(double X, double Y)
{
	public static readonly PtzSpeed Zero = new(0, 0);
	public bool IsZero => X == 0 && Y == 0;
}

public sealed class FollowController(FollowTuning? tuning = null)
{
	readonly FollowTuning _t = tuning ?? new FollowTuning();
	readonly List<(long AtMs, PtzSpeed Speed)> _sent = [];
	long _lastMs = -1;

	public PtzSpeed Command { get; private set; } = PtzSpeed.Zero;
	public FollowTuning Tuning => _t;

	public static TrackState? PickTarget(IReadOnlyList<TrackState> tracks, int? currentId)
	{
		if (currentId is { } id && tracks.FirstOrDefault(t => t.Id == id) is { } same) return same;
		return tracks.Where(t => t.SeenNow).OrderByDescending(t => t.Box.Area * t.Score).FirstOrDefault();
	}

	public PtzSpeed Step(TrackState? target, long nowMs)
	{
		var dt = _lastMs < 0 ? 0.1 : Math.Clamp((nowMs - _lastMs) / 1000.0, 0.02, 0.5);
		_lastMs = nowMs;
		var desired = target is null ? PtzSpeed.Zero : Desired(target, nowMs);
		var step = _t.MaxAccel * dt;
		var x = Command.X + Math.Clamp(desired.X - Command.X, -step, step);
		var y = Command.Y + Math.Clamp(desired.Y - Command.Y, -step, step);
		if (desired.X == 0 && Math.Abs(x) < _t.MinSpeed) x = 0;
		if (desired.Y == 0 && Math.Abs(y) < _t.MinSpeed) y = 0;
		Command = new PtzSpeed(x, y);
		_sent.Add((nowMs, Command));
		_sent.RemoveAll(s => s.AtMs < nowMs - (long)(_t.LagS * 1000) - 2000);
		return Command;
	}

	public void Reset()
	{
		Command = PtzSpeed.Zero;
		_sent.Clear();
		_lastMs = -1;
	}

	PtzSpeed Desired(TrackState target, long nowMs)
	{
		var lagMs = (long)(_t.LagS * 1000);
		var seen = At(nowMs - lagMs);
		var (flightX, flightY) = Integral(nowMs - lagMs, nowMs);
		var aimY = target.Box.Y + target.Box.H * 0.3;
		return new PtzSpeed(
			Axis(target.Box.Cx - 0.5, target.Vx, seen.X, flightX, Command.X, _t.GainX, _t.DeadZoneX),
			Axis(aimY - _t.AimY, target.Vy, seen.Y, flightY, Command.Y, _t.GainY, _t.DeadZoneY));
	}

	double Axis(double offset, double imageVelocity, double seenCommand, double inFlight, double current, double gain, double deadZone)
	{
		var subject = imageVelocity + gain * seenCommand;
		var now = offset + subject * _t.LagS - gain * inFlight;
		var predicted = now + (subject - gain * current) * _t.HorizonS;
		if (Math.Abs(predicted) < deadZone && Math.Abs(subject) < gain * _t.MinSpeed) return 0;
		var wanted = (subject + predicted / _t.SettleS) / gain;
		var speed = Math.Clamp(wanted, -_t.MaxSpeed, _t.MaxSpeed);
		return Math.Abs(speed) < _t.MinSpeed ? Math.Sign(speed) * _t.MinSpeed : speed;
	}

	PtzSpeed At(long ms)
	{
		var last = PtzSpeed.Zero;
		foreach (var (at, speed) in _sent)
		{
			if (at > ms) break;
			last = speed;
		}
		return last;
	}

	(double X, double Y) Integral(long fromMs, long toMs)
	{
		double x = 0, y = 0;
		var current = At(fromMs);
		var cursor = fromMs;
		foreach (var (at, speed) in _sent)
		{
			if (at <= fromMs) continue;
			if (at > toMs) break;
			x += current.X * (at - cursor) / 1000.0;
			y += current.Y * (at - cursor) / 1000.0;
			cursor = at;
			current = speed;
		}
		x += current.X * (toMs - cursor) / 1000.0;
		y += current.Y * (toMs - cursor) / 1000.0;
		return (x, y);
	}
}

public static class PtzOrientation
{
	public static (double X, double Y) ToCamera(double x, double y, int rotation) => rotation == 180 ? (-x, -y) : (x, y);
}
