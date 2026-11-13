using Velador.Core;

namespace Velador.Tests;

public class OutageTests
{
	[Fact]
	public void The_first_start_ever_has_no_gap()
	{
		Assert.Null(Outage.AtStartup(null, 1_000_000, 500_000, false, false));
	}

	[Fact]
	public void A_quick_restart_is_not_a_gap()
	{
		Assert.Null(Outage.AtStartup(1_000_000, 1_008_000, 9_000_000, false, false));
	}

	[Fact]
	public void If_the_media_server_kept_recording_there_is_no_gap()
	{
		Assert.Null(Outage.AtStartup(1_000_000, 1_600_000, 9_000_000, false, true));
	}

	[Fact]
	public void A_reboot_shorter_than_the_silence_is_a_power_cut()
	{
		var gap = Outage.AtStartup(1_000_000, 1_600_000, 120_000, false, false);

		Assert.Equal(new RecordedGap(1_000_000, null, GapCause.Power), gap);
	}

	[Fact]
	public void A_crash_with_the_machine_still_up_is_the_engine()
	{
		Assert.Equal(GapCause.Engine, Outage.AtStartup(1_000_000, 1_600_000, 9_000_000, false, false)!.Cause);
	}

	[Fact]
	public void A_clean_stop_is_a_pause()
	{
		Assert.Equal(GapCause.Paused, Outage.AtStartup(1_000_000, 1_600_000, 9_000_000, true, false)!.Cause);
	}

	[Fact]
	public void A_jump_between_beats_is_sleep()
	{
		Assert.Null(Outage.BetweenBeats(1_000_000, 1_005_200));
		Assert.Equal(new RecordedGap(1_000_000, null, GapCause.Sleep), Outage.BetweenBeats(1_000_000, 1_900_000));
	}
}
