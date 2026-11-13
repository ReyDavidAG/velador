using Velador.Core;

namespace Velador.Tests;

public class RetentionTests
{
	const long Hour = 3_600_000;
	const long Gb = 1024L * 1024 * 1024;
	static readonly RetentionLimits Limits = new(Days: 2, DiskPercent: 80);

	static List<StoredSegment> Hours(int count, long bytes = Gb, string stream = "main") =>
		Enumerable.Range(0, count).Select(i => new StoredSegment($"{stream}-{i}", stream, i * Hour, bytes)).ToList();

	[Fact]
	public void Nothing_is_deleted_inside_the_limits()
	{
		Assert.Empty(Retention.Plan(Hours(24), 24 * Hour, 500 * Gb, 400 * Gb, Limits));
	}

	[Fact]
	public void Segments_older_than_the_days_go_first_and_only_them()
	{
		var doomed = Retention.Plan(Hours(72), 72 * Hour, 500 * Gb, 400 * Gb, Limits);

		Assert.Equal(24, doomed.Count);
		Assert.Equal(Enumerable.Range(0, 24).Select(i => $"main-{i}"), doomed.Select(d => d.File));
	}

	[Fact]
	public void A_full_disk_deletes_the_oldest_until_it_fits()
	{
		var doomed = Retention.Plan(Hours(24), 24 * Hour, 500 * Gb, 95 * Gb, Limits);

		Assert.Equal(5, doomed.Count);
		Assert.Equal("main-0", doomed[0].File);
	}

	[Fact]
	public void The_floor_of_free_space_wins_even_under_the_percentage()
	{
		var doomed = Retention.Plan(Hours(10, Gb / 2), 10 * Hour, 4 * Gb, Gb, new RetentionLimits(30, 100));

		Assert.Equal(2, doomed.Count);
	}

	[Fact]
	public void The_file_being_written_is_never_deleted()
	{
		var doomed = Retention.Plan(Hours(3), 500 * Hour, 500 * Gb, 400 * Gb, Limits);

		Assert.Equal(["main-0", "main-1"], doomed.Select(d => d.File));
	}

	[Fact]
	public void Each_stream_keeps_its_own_newest_file()
	{
		var segments = Hours(3).Concat(Hours(3, Gb, "minor")).ToList();
		var doomed = Retention.Plan(segments, 500 * Hour, 500 * Gb, 400 * Gb, Limits);

		Assert.DoesNotContain(doomed, d => d.File is "main-2" or "minor-2");
		Assert.Equal(4, doomed.Count);
	}

	[Fact]
	public void What_a_clip_covers_survives()
	{
		var doomed = Retention.Plan(Hours(72), 72 * Hour, 500 * Gb, 400 * Gb, Limits, [new TimeRange(5 * Hour + 1000, 5 * Hour + 60_000)]);

		Assert.DoesNotContain(doomed, d => d.File == "main-5");
		Assert.Equal(23, doomed.Count);
	}
}

public class RetentionForecastTests
{
	const long Hour = 3_600_000;
	const long Day = 86_400_000;
	const long GB = 1024L * 1024 * 1024;

	static List<StoredSegment> Hourly(long fromMs, int hours, long bytesEach) =>
		Enumerable.Range(0, hours).Select(i => new StoredSegment($"f{i}.mp4", "camamain", fromMs + i * Hour, bytesEach)).ToList();

	[Fact]
	public void It_measures_a_day_from_the_last_hours_and_says_the_days_limit_rules()
	{
		var now = 10 * Day;
		var segments = Hourly(now - 6 * Hour, 6, GB / 2);

		var outlook = RetentionForecast.Outlook(segments, now, 1000 * GB, 800 * GB, new RetentionLimits(2, 80));

		Assert.Equal(3 * GB, outlook.RecordedBytes);
		Assert.Equal(12 * GB, outlook.BytesPerDay);
		Assert.Equal(2, outlook.DaysThatFit);
		Assert.Equal("days", outlook.LimitedBy);
		Assert.Equal(now - 6 * Hour + Hour + 2 * Day, outlook.NextDeletionMs);
	}

	[Fact]
	public void A_small_disk_limits_the_days_and_says_when_it_starts_deleting()
	{
		var now = 10 * Day;
		var segments = Hourly(now - 12 * Hour, 12, GB);

		var outlook = RetentionForecast.Outlook(segments, now, 100 * GB, 60 * GB, new RetentionLimits(14, 80));

		Assert.Equal(24 * GB, outlook.BytesPerDay);
		Assert.Equal("disk", outlook.LimitedBy);
		Assert.Equal(52.0 / 24, outlook.DaysThatFit!.Value, 3);
		Assert.Equal(now + (long)(40.0 / 24 * Day), outlook.NextDeletionMs);
	}

	[Fact]
	public void Without_enough_recording_it_does_not_guess()
	{
		var outlook = RetentionForecast.Outlook(Hourly(10 * Day - 60_000, 1, GB / 100), 10 * Day, 100 * GB, 60 * GB, new RetentionLimits(14, 80));

		Assert.Null(outlook.BytesPerDay);
		Assert.Null(outlook.DaysThatFit);
		Assert.Equal("days", outlook.LimitedBy);
	}
}
