namespace Velador.Core;

public readonly record struct TimeRange(long StartMs, long EndMs)
{
	public long DurationMs => Math.Max(0, EndMs - StartMs);

	public bool Overlaps(TimeRange other) => StartMs < other.EndMs && other.StartMs < EndMs;

	public TimeRange? ClipTo(TimeRange bounds)
	{
		var start = Math.Max(StartMs, bounds.StartMs);
		var end = Math.Min(EndMs, bounds.EndMs);
		return end > start ? new TimeRange(start, end) : null;
	}
}

public sealed record Gap(TimeRange Range, GapCause Cause);

public sealed record RecordedGap(long StartMs, long? EndMs, GapCause Cause);
