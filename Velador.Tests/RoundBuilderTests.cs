using Velador.Core;

namespace Velador.Tests;

public class RoundBuilderTests
{
	static readonly TimeRange Day = new(0, 86_400_000);

	[Fact]
	public void A_full_day_has_no_gaps_and_full_coverage()
	{
		var round = RoundBuilder.Build(Day, 90_000_000, [new TimeRange(-5000, 86_400_000)], []);

		Assert.Empty(round.Gaps);
		Assert.Equal(new TimeRange(0, 86_400_000), Assert.Single(round.Spans));
		Assert.Equal(100, round.Coverage);
	}

	[Fact]
	public void Today_ends_at_now_and_the_future_is_not_a_gap()
	{
		var round = RoundBuilder.Build(Day, 3_600_000, [new TimeRange(0, 3_600_000)], []);

		Assert.Equal(3_600_000, round.EndMs);
		Assert.Empty(round.Gaps);
		Assert.Equal(100, round.Coverage);
	}

	[Fact]
	public void Segments_closer_than_the_tolerance_are_one_span()
	{
		var round = RoundBuilder.Build(Day, 86_400_000, [new TimeRange(0, 60_000), new TimeRange(60_400, 86_400_000)], []);

		Assert.Single(round.Spans);
		Assert.Empty(round.Gaps);
	}

	[Fact]
	public void A_hole_between_spans_is_a_gap_with_the_recorded_cause()
	{
		var round = RoundBuilder.Build(
			Day,
			86_400_000,
			[new TimeRange(0, 10_000_000), new TimeRange(10_096_000, 86_400_000)],
			[new RecordedGap(9_999_000, 10_097_000, GapCause.Camera)]);

		var gap = Assert.Single(round.Gaps);
		Assert.Equal(new TimeRange(10_000_000, 10_096_000), gap.Range);
		Assert.Equal(GapCause.Camera, gap.Cause);
		Assert.InRange(round.Coverage, 99.8, 99.99);
	}

	[Fact]
	public void A_hole_nobody_explained_is_unknown()
	{
		var round = RoundBuilder.Build(Day, 86_400_000, [new TimeRange(0, 1_000_000), new TimeRange(2_000_000, 86_400_000)], []);

		Assert.Equal(GapCause.Unknown, Assert.Single(round.Gaps).Cause);
	}

	[Fact]
	public void An_open_gap_reaches_now()
	{
		var round = RoundBuilder.Build(Day, 5_000_000, [new TimeRange(0, 4_000_000)], [new RecordedGap(4_000_000, null, GapCause.Network)]);

		var gap = Assert.Single(round.Gaps);
		Assert.Equal(new TimeRange(4_000_000, 5_000_000), gap.Range);
		Assert.Equal(GapCause.Network, gap.Cause);
	}

	[Fact]
	public void The_cause_that_covers_most_of_the_hole_wins()
	{
		var round = RoundBuilder.Build(
			Day,
			86_400_000,
			[new TimeRange(0, 1_000_000), new TimeRange(2_000_000, 86_400_000)],
			[new RecordedGap(1_000_000, 1_010_000, GapCause.Camera), new RecordedGap(1_010_000, 2_000_000, GapCause.Sleep)]);

		Assert.Equal(GapCause.Sleep, Assert.Single(round.Gaps).Cause);
	}

	[Fact]
	public void A_day_with_nothing_recorded_is_one_gap()
	{
		var round = RoundBuilder.Build(Day, 86_400_000, [], []);

		Assert.Equal(Day, Assert.Single(round.Gaps).Range);
		Assert.Equal(0, round.Coverage);
	}

	[Fact]
	public void Time_before_the_camera_was_ever_watched_is_not_a_gap()
	{
		var round = RoundBuilder.Build(Day, 50_000_000, [new TimeRange(40_000_000, 50_000_000)], [], watchedSinceMs: 40_000_000);

		Assert.Empty(round.Gaps);
		Assert.Equal(100, round.Coverage);
		Assert.Equal(40_000_000, round.WatchedFromMs);
	}

	[Fact]
	public void A_day_before_the_camera_existed_expects_nothing()
	{
		var round = RoundBuilder.Build(Day, 200_000_000, [], [], watchedSinceMs: 150_000_000);

		Assert.Empty(round.Gaps);
		Assert.Empty(round.Spans);
		Assert.Equal(100, round.Coverage);
	}

	[Fact]
	public void A_short_day_from_a_clock_change_keeps_its_own_length()
	{
		var shortDay = new TimeRange(0, 82_800_000);
		var round = RoundBuilder.Build(shortDay, 90_000_000, [new TimeRange(0, 82_800_000)], []);

		Assert.Equal(100, round.Coverage);
		Assert.Equal(82_800_000, round.EndMs);
	}
}
