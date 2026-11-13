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
