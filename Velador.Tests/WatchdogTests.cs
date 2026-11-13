using Velador.Core;

namespace Velador.Tests;

public class WatchdogTests
{
	static WatchdogSample Flowing(long now, long bytes) => new(now, true, true, bytes, true, true);

	[Fact]
	public void Steady_bytes_never_open_a_gap()
	{
		var dog = new Watchdog();
		for (var i = 0; i < 20; i++)
		{
			var verdict = dog.Observe(Flowing(i * 1000, i * 150_000));
			Assert.Null(verdict.Opened);
			Assert.False(verdict.RestartMedia);
		}
		Assert.False(dog.InGap);
	}

	[Fact]
	public void Five_seconds_without_bytes_open_a_gap_at_the_last_good_moment()
	{
		var dog = new Watchdog();
		dog.Observe(Flowing(0, 100));
		dog.Observe(Flowing(1000, 200));
		WatchdogVerdict? verdict = null;
		for (var t = 2000; t <= 6000; t += 1000) verdict = dog.Observe(new WatchdogSample(t, true, false, 200, false, true));

		Assert.NotNull(verdict!.Opened);
		Assert.Equal(1000, verdict.Opened!.StartMs);
		Assert.Equal(GapCause.Camera, verdict.Opened.Cause);
	}

	[Fact]
	public void The_gap_opens_once_and_closes_when_bytes_return()
	{
		var dog = new Watchdog();
		dog.Observe(Flowing(0, 100));
		dog.Observe(Flowing(1000, 200));
		var opened = 0;
		for (var t = 2000; t <= 20_000; t += 1000)
			if (dog.Observe(new WatchdogSample(t, true, false, 200, false, true)).Opened is not null) opened++;
		dog.Observe(Flowing(21_000, 10));
		var closed = dog.Observe(Flowing(22_000, 500));

		Assert.Equal(1, opened);
		Assert.Equal(22_000, closed.ClosedAtMs);
		Assert.False(dog.InGap);
	}

	[Theory]
	[InlineData(false, true, true, false, GapCause.Engine)]
	[InlineData(true, false, false, false, GapCause.Network)]
	[InlineData(true, false, true, false, GapCause.Camera)]
	[InlineData(true, true, true, true, GapCause.Credentials)]
	public void The_cause_says_who_failed(bool alive, bool camera, bool gateway, bool rejected, GapCause expected)
	{
		var dog = new Watchdog();
		dog.Observe(Flowing(0, 100));
		dog.Observe(Flowing(1000, 200));
		RecordedGap? gap = null;
		for (var t = 2000; t <= 7000 && gap is null; t += 1000)
			gap = dog.Observe(new WatchdogSample(t, alive, false, 200, camera, gateway, rejected)).Opened;

		Assert.Equal(expected, gap!.Cause);
	}

	[Fact]
	public void A_hung_media_server_is_restarted_but_not_in_a_burst()
	{
		var dog = new Watchdog();
		dog.Observe(Flowing(0, 100));
		dog.Observe(Flowing(1000, 200));
		var restarts = new List<long>();
		for (var t = 2000; t <= 60_000; t += 1000)
			if (dog.Observe(new WatchdogSample(t, true, true, 200, true, true)).RestartMedia) restarts.Add(t);

		Assert.Equal([16_000, 46_000], restarts);
	}

	[Fact]
	public void An_unreachable_camera_or_a_rejected_password_never_restarts_the_media_server()
	{
		var dog = new Watchdog();
		dog.Observe(Flowing(0, 100));
		for (var t = 1000; t <= 120_000; t += 1000)
		{
			Assert.False(dog.Observe(new WatchdogSample(t, true, false, 100, false, true)).RestartMedia);
			Assert.False(dog.Observe(new WatchdogSample(t, true, false, 100, true, true, true)).RestartMedia);
		}
	}

	[Fact]
	public void A_restarted_counter_counts_as_progress()
	{
		var dog = new Watchdog();
		dog.Observe(Flowing(0, 900_000));
		var verdict = dog.Observe(Flowing(1000, 4000));

		Assert.Null(verdict.Opened);
		Assert.False(dog.InGap);
	}
}
