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
