namespace Velador.Core;

public static class Outage
{
	public const long HeartbeatMs = 5000;
	public const long MissedBeatsMs = HeartbeatMs * 4;
	public const long RestartMs = 2000;

	public static RecordedGap? AtStartup(long? lastAliveMs, long nowMs, long uptimeMs, bool cleanStop, bool mediaStillRecording)
	{
		if (lastAliveMs is not { } last || mediaStillRecording || nowMs - last < RestartMs) return null;
		var cause = cleanStop ? GapCause.Paused : uptimeMs < nowMs - last ? GapCause.Power : GapCause.Engine;
		return new RecordedGap(last, null, cause);
	}

	public static RecordedGap? BetweenBeats(long previousBeatMs, long nowMs) =>
		nowMs - previousBeatMs >= MissedBeatsMs ? new RecordedGap(previousBeatMs, null, GapCause.Sleep) : null;
}
