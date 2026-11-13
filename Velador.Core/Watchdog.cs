namespace Velador.Core;

public readonly record struct WatchdogSample(long NowMs, bool ProcessAlive, bool Online, long InboundBytes, bool CameraReachable, bool GatewayReachable, bool CredentialsRejected = false);

public sealed record WatchdogVerdict(RecordedGap? Opened, long? ClosedAtMs, bool RestartMedia);

public sealed class Watchdog(long stallMs = 5000, long restartEveryMs = 30000)
{
	long? _lastBytes;
	long _lastProgressMs = -1;
	long? _gapStartMs;
	long _lastRestartMs = long.MinValue / 2;

	public bool InGap => _gapStartMs is not null;

	public WatchdogVerdict Observe(WatchdogSample s)
	{
		if (_lastProgressMs < 0) _lastProgressMs = s.NowMs;
		var flowing = s.ProcessAlive && s.Online;
		var progressed = flowing && _lastBytes is { } last && (s.InboundBytes > last || (s.InboundBytes < last && s.InboundBytes > 0));
		_lastBytes = flowing ? s.InboundBytes : null;

		if (progressed)
		{
			_lastProgressMs = s.NowMs;
			if (_gapStartMs is null) return new WatchdogVerdict(null, null, false);
			_gapStartMs = null;
			return new WatchdogVerdict(null, s.NowMs, false);
		}

		if (s.NowMs - _lastProgressMs < stallMs) return new WatchdogVerdict(null, null, false);

		RecordedGap? opened = null;
		if (_gapStartMs is null)
		{
			_gapStartMs = _lastProgressMs;
			opened = new RecordedGap(_lastProgressMs, null, Classify(s));
		}
		var hung = s.ProcessAlive && s.CameraReachable && !s.CredentialsRejected;
		var restart = hung && s.NowMs - _lastRestartMs >= restartEveryMs && s.NowMs - _lastProgressMs >= stallMs * 3;
		if (restart) _lastRestartMs = s.NowMs;
		return new WatchdogVerdict(opened, null, restart);
	}

	public void Resync(long nowMs)
	{
		_lastBytes = null;
		_lastProgressMs = Math.Max(_lastProgressMs, nowMs - stallMs);
	}

	static GapCause Classify(WatchdogSample s)
	{
		if (!s.ProcessAlive) return GapCause.Engine;
		if (s.CredentialsRejected) return GapCause.Credentials;
		if (!s.GatewayReachable) return GapCause.Network;
		return GapCause.Camera;
	}
}
