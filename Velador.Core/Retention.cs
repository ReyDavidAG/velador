namespace Velador.Core;

public sealed record StoredSegment(string File, string Stream, long StartMs, long Bytes);

public sealed record RetentionLimits(int Days, int DiskPercent, long FloorBytes = 2L * 1024 * 1024 * 1024);

public static class Retention
{
	public static IReadOnlyList<StoredSegment> Plan(IEnumerable<StoredSegment> segments, long nowMs, long diskTotalBytes, long diskFreeBytes, RetentionLimits limits, IEnumerable<TimeRange>? kept = null, long segmentMs = 3_600_000)
	{
		var all = segments.ToList();
		var newest = all.GroupBy(s => s.Stream).Select(g => g.MaxBy(s => s.StartMs)!).ToHashSet();
		var protectedRanges = kept?.ToList() ?? [];
		var cutoff = nowMs - limits.Days * 86_400_000L;
		var allowedUsed = diskTotalBytes * limits.DiskPercent / 100;
		var free = diskFreeBytes;
		var doomed = new List<StoredSegment>();
		foreach (var segment in all.OrderBy(s => s.StartMs))
		{
			if (newest.Contains(segment)) continue;
			if (protectedRanges.Any(r => r.Overlaps(new TimeRange(segment.StartMs, segment.StartMs + segmentMs)))) continue;
			var tooOld = segment.StartMs + segmentMs <= cutoff;
			var diskFull = diskTotalBytes - free > allowedUsed || free < limits.FloorBytes;
			if (!tooOld && !diskFull) break;
			doomed.Add(segment);
			free += segment.Bytes;
		}
		return doomed;
	}
}

public sealed record RetentionOutlook(long RecordedBytes, long? OldestMs, long? BytesPerDay, double? DaysThatFit, string LimitedBy, long? NextDeletionMs);

public static class RetentionForecast
{
	const long DayMs = 86_400_000;
	const long MinSampleMs = 10 * 60_000;

	public static RetentionOutlook Outlook(IReadOnlyList<StoredSegment> segments, long nowMs, long diskTotalBytes, long diskFreeBytes, RetentionLimits limits, long segmentMs = 3_600_000)
	{
		var recorded = segments.Sum(s => s.Bytes);
		long? oldest = segments.Count == 0 ? null : segments.Min(s => s.StartMs);
		var recent = segments.Where(s => s.StartMs >= nowMs - DayMs).ToList();
		var sampleMs = recent.Count == 0 ? 0 : nowMs - recent.Min(s => s.StartMs);
		long? perDay = sampleMs >= MinSampleMs ? (long)(recent.Sum(s => s.Bytes) * (double)DayMs / sampleMs) : null;
		var others = diskTotalBytes - diskFreeBytes - recorded;
		var allowed = Math.Min(diskTotalBytes * limits.DiskPercent / 100 - others, recorded + diskFreeBytes - limits.FloorBytes);
		double? byDisk = perDay is > 0 ? Math.Max(0, allowed) / (double)perDay : null;
		var limitedBy = byDisk is { } d && d < limits.Days ? "disk" : "days";
		double? fit = byDisk is { } days ? Math.Min(limits.Days, days) : null;
		long? next = null;
		if (limitedBy == "days" && oldest is { } first) next = Math.Max(nowMs, first + segmentMs + limits.Days * DayMs);
		else if (limitedBy == "disk" && perDay is > 0) next = nowMs + (long)(Math.Max(0, allowed - recorded) / (double)perDay * DayMs);
		return new RetentionOutlook(recorded, oldest, perDay, fit, limitedBy, next);
	}
}
