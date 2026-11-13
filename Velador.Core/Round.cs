namespace Velador.Core;

public sealed record Round(TimeRange Day, long WatchedFromMs, long EndMs, IReadOnlyList<TimeRange> Spans, IReadOnlyList<Gap> Gaps)
{
	public double Coverage
	{
		get
		{
			var elapsed = EndMs - WatchedFromMs;
			if (elapsed <= 0) return 100;
			var recorded = Spans.Sum(s => s.DurationMs);
			return Math.Min(100, recorded * 100.0 / elapsed);
		}
	}
}

public static class RoundBuilder
{
	public const long JoinToleranceMs = 1000;

	public static Round Build(TimeRange day, long nowMs, IEnumerable<TimeRange> recorded, IEnumerable<RecordedGap> knownGaps, long? watchedSinceMs = null)
	{
		var end = Math.Clamp(nowMs, day.StartMs, day.EndMs);
		var from = Math.Clamp(watchedSinceMs ?? day.StartMs, day.StartMs, end);
		var window = new TimeRange(from, end);
		var spans = Merge(recorded.Select(r => r.ClipTo(window)).OfType<TimeRange>());
		var known = knownGaps.ToList();
		var gaps = new List<Gap>();
		var cursor = from;
		foreach (var span in spans)
		{
			AddGap(gaps, known, new TimeRange(cursor, span.StartMs), nowMs);
			cursor = span.EndMs;
		}
		AddGap(gaps, known, new TimeRange(cursor, end), nowMs);
		return new Round(day, from, end, spans, gaps);
	}

	static void AddGap(List<Gap> gaps, List<RecordedGap> known, TimeRange range, long nowMs)
	{
		if (range.DurationMs < JoinToleranceMs) return;
		var best = known
			.Select(k => (k.Cause, Overlap: new TimeRange(k.StartMs, k.EndMs ?? nowMs).ClipTo(range)?.DurationMs ?? 0))
			.Where(k => k.Overlap > 0)
			.OrderByDescending(k => k.Overlap)
			.FirstOrDefault();
		gaps.Add(new Gap(range, best.Overlap > 0 ? best.Cause : GapCause.Unknown));
	}

	static List<TimeRange> Merge(IEnumerable<TimeRange> ranges)
	{
		var merged = new List<TimeRange>();
		foreach (var range in ranges.OrderBy(r => r.StartMs))
		{
			if (merged.Count > 0 && range.StartMs - merged[^1].EndMs < JoinToleranceMs)
				merged[^1] = merged[^1] with { EndMs = Math.Max(merged[^1].EndMs, range.EndMs) };
			else
				merged.Add(range);
		}
		return merged;
	}
}
