using Velador.Core.Vision;

namespace Velador.Tests;

public class FollowTests
{
	static readonly FollowGeometry Measured = new(2.24, 1.01, 1.4, 0.15, 0.5 * 0.55, 0.67 * 0.55);
	static readonly PtzZone Zone = new(-0.35, 0.35, -0.7, 0.2, 0.35, -0.5, -0.33, 0, 0.05, -0.33);

	sealed class Street(double speed, double from, double to, double lagS = 1.5, int seed = 1, bool slices = false)
	{
		const double UnitDegrees = 170, Fov = 73.5, FovTall = 41.3, CameraHeight = 3.2, Away = 2.3, Tall = 1.7, StepS = 0.05, ActuationS = 0.15, PanStartS = 0.35, TiltStartS = 0.15;

		static PtzRate Sliced(PtzRate rate, long ms) =>
			rate.X == 0 || rate.Y == 0 ? rate : ms / 300 % 2 == 0 ? new PtzRate(Math.Clamp(rate.X * 1.8, -0.5, 0.5), 0) : new PtzRate(0, Math.Clamp(rate.Y * 1.8, -0.67, 0.67));

		public double CamX = Zone.HomeX, CamY = Zone.HomeY;
		public readonly PathFollower Follower = new();
		public readonly List<(double T, double CamX, double CamY, bool InView, bool Tracking)> Log = [];

		static double Degrees(double radians) => radians * 180 / Math.PI;

		Box? See(double along)
		{
			var ground = Math.Sqrt(along * along + Away * Away);
			var pan = Degrees(Math.Atan2(along, Away));
			var top = Degrees(Math.Atan((CameraHeight - Tall) / ground));
			var feet = Degrees(Math.Atan(CameraHeight / ground));
			var tilt = 41 + 45 * CamY;
			var cx = 0.5 + (pan - CamX * UnitDegrees) / Fov;
			var y0 = 0.5 + (top - tilt) / FovTall;
			var y1 = 0.5 + (feet - tilt) / FovTall;
			var width = Degrees(Math.Atan(0.5 / ground)) / Fov;
			var aim = y0 + (y1 - y0) * 0.3;
			return cx is > 0.02 and < 0.98 && aim is > 0.02 and < 0.98 ? Box.FromCenter(cx, (y0 + y1) / 2, width, y1 - y0) : null;
		}

		public double Run()
		{
			var random = new Random(seed);
			Follower.Configure(Measured, Zone);
			var frames = new Queue<(double At, Box? Box)>();
			var orders = new Queue<(double At, PtzRate Rate)>();
			var applied = PtzRate.Zero;
			var turns = new AxisTurn();
			int panSign = 0, tiltSign = 0;
			double panSince = 0, tiltSince = 0;
			var along = from;
			var direction = Math.Sign(to - from);
			int inView = 0, total = 0;
			long seen = 0;
			for (var step = 0; Math.Abs(along - from) < Math.Abs(to - from); step++)
			{
				var t = step * StepS;
				var ms = (long)(t * 1000);
				along += direction * speed * StepS;
				while (orders.Count > 0 && orders.Peek().At <= t) applied = orders.Dequeue().Rate;
				var turn = slices ? Sliced(applied, ms) : turns.Step(applied, ms, 0.5, 0.67);
				if (Math.Sign(turn.X) != panSign) (panSign, panSince) = (Math.Sign(turn.X), t);
				if (Math.Sign(turn.Y) != tiltSign) (tiltSign, tiltSince) = (Math.Sign(turn.Y), t);
				if (t - panSince >= PanStartS) CamX = Math.Clamp(CamX + Math.Clamp(turn.X, -0.5, 0.5) * StepS, -1, 1);
				if (t - tiltSince >= TiltStartS) CamY = Math.Clamp(CamY + Math.Clamp(turn.Y, -0.67, 0.67) * StepS, -1, 1);
				if (step % 3 == 0) Follower.Pose(ms, CamX, CamY);
				var now = See(along);
				if (step % 2 == 0)
				{
					frames.Enqueue((t + lagS, now is { } b ? b with { X = b.X + (random.NextDouble() - 0.5) * 0.02, Y = b.Y + (random.NextDouble() - 0.5) * 0.02 } : null));
					Box? arrived = null;
					var any = false;
					while (frames.Count > 0 && frames.Peek().At <= t)
					{
						arrived = frames.Dequeue().Box;
						any = true;
					}
					IReadOnlyList<TrackState> tracks = any && arrived is { } box ? [new TrackState(1, box, 0, 0, 0.9, true, 0, seen = ms)] : [];
					orders.Enqueue((t + ActuationS, Follower.Step(tracks, ms)));
				}
				if (Math.Abs(along) <= 12)
				{
					total++;
					if (now is not null) inView++;
				}
				Log.Add((t, CamX, CamY, now is not null, Follower.TargetId is not null));
			}
			return (double)inView / total;
		}
	}

	[Theory]
	[InlineData(1.4, 0.9)]
	[InlineData(2.8, 0.6)]
	[InlineData(5.0, 0.35)]
	public void Someone_coming_down_the_street_stays_in_view_past_the_camera(double speed, double atLeast)
	{
		var results = Enumerable.Range(1, 4).Select(seed => new Street(speed, 14, -14, seed: seed).Run()).ToList();

		Assert.True(results.Min() >= atLeast, $"a la vista {string.Join(", ", results.Select(r => r.ToString("P0")))}");
	}

	[Fact]
	public void The_camera_never_leaves_the_zone()
	{
		var street = new Street(2.8, 16, -16);
		street.Run();

		Assert.All(street.Log, l => Assert.InRange(l.CamX, Zone.MinX - 0.03, Zone.MaxX + 0.03));
		Assert.All(street.Log, l => Assert.InRange(l.CamY, Zone.MinY - 0.05, Zone.MaxY + 0.05));
	}

	[Fact]
	public void A_wrong_guess_of_the_video_delay_still_follows()
	{
		foreach (var lag in new[] { 1.1, 1.8 })
		{
			var kept = new Street(1.4, 14, -14, lag).Run();
			Assert.True(kept >= 0.8, $"con {lag} s de retraso real: {kept:P0}");
		}
	}

	[Fact]
	public void A_still_centered_person_does_not_move_the_camera()
	{
		var follower = new PathFollower();
		follower.Configure(Measured, null);
		for (var i = 0; i < 60; i++)
		{
			follower.Pose(i * 100, 0, -0.3);
			follower.Step([new TrackState(1, Box.FromCenter(0.5, 0.55, 0.1, 0.5), 0, 0, 0.9, true, 0, i * 100)], i * 100);
		}

		Assert.True(follower.Rate.IsZero, $"{follower.Rate}");
	}

	[Fact]
	public void When_the_person_is_lost_it_keeps_going_along_the_street_a_moment_and_then_stops()
	{
		var follower = new PathFollower();
		follower.Configure(Measured, Zone);
		double x = 0.2;
		for (var i = 0; i < 40; i++)
		{
			var ms = i * 100;
			follower.Pose(ms, x, -0.4);
			follower.Step([new TrackState(1, Box.FromCenter(0.5 - i * 0.012, 0.5, 0.1, 0.4), 0, 0, 0.9, true, 0, ms)], ms);
		}
		for (var i = 40; i < 55; i++)
		{
			follower.Pose(i * 100, x, -0.4);
			follower.Step([], i * 100);
		}
		Assert.True(follower.Coasting);
		Assert.True(follower.Rate.X < -0.02, $"{follower.Rate}");

		for (var i = 55; i < 120; i++)
		{
			follower.Pose(i * 100, x, -0.4);
			follower.Step([], i * 100);
		}
		Assert.False(follower.Coasting);
		Assert.True(follower.Rate.IsZero);
		Assert.Null(follower.TargetId);
	}

	[Fact]
	public void The_route_passes_through_its_three_points_and_bends_between_them()
	{
		Assert.Equal(-0.33, Zone.Route(-0.35), 3);
		Assert.Equal(0.05, Zone.Route(0), 3);
		Assert.Equal(-0.33, Zone.Route(0.35), 3);
		Assert.InRange(Zone.Route(0.2), -0.2, 0);
		Assert.True(Zone.Route(0.5) < Zone.Route(0.35));
		Assert.True(new PtzZone(-0.35, 0.35, -0.7, 0.2, 0, 0).Route(0) > new PtzZone(-0.35, 0.35, -0.7, 0.2, 0, 0).Route(0.35));
	}

	[Fact]
	public void A_camera_that_turns_one_axis_at_a_time_gets_them_in_turns()
	{
		var both = new PtzRate(0.2, -0.3);
		var turn = new AxisTurn();
		void Expect(double x, double y, long ms)
		{
			var sent = turn.Step(both, ms, 0.5, 0.6);
			Assert.Equal(x, sent.X, 6);
			Assert.Equal(y, sent.Y, 6);
		}

		Expect(0.36, 0, 0);
		Expect(0.36, 0, AxisTurn.LongMs - 100);
		Expect(0, -0.54, AxisTurn.LongMs);
		Expect(0, -0.54, AxisTurn.LongMs + AxisTurn.HoldMs - 100);
		Expect(0.36, 0, AxisTurn.LongMs + AxisTurn.HoldMs);
		Assert.Equal(new PtzRate(0.2, 0), turn.Step(new PtzRate(0.2, 0), 9000, 0.5, 0.6));
	}

	[Fact]
	public void Short_turns_stall_a_pan_that_is_slow_to_start_and_long_turns_do_not()
	{
		var shortTurns = Enumerable.Range(1, 4).Select(seed => new Street(1.4, 14, -14, seed: seed, slices: true).Run()).Min();
		var longTurns = Enumerable.Range(1, 4).Select(seed => new Street(1.4, 14, -14, seed: seed).Run()).Min();

		Assert.True(longTurns > shortTurns + 0.1, $"largos {longTurns:P0}, cortos {shortTurns:P0}");
	}

	[Fact]
	public void A_face_needs_eyes_level_and_above_the_nose_and_mouth()
	{
		FacePoint[] face = [new(18, 18), new(30, 18), new(24, 24), new(19, 30), new(29, 30)];

		Assert.True(FaceQuality.Plausible(face, 26));
		Assert.False(FaceQuality.Plausible([new(18, 30), new(30, 30), new(24, 24), new(19, 18), new(29, 18)], 26));
		Assert.False(FaceQuality.Plausible([new(18, 10), new(26, 26), new(24, 24), new(19, 30), new(29, 30)], 26));
		Assert.False(FaceQuality.Plausible(face, 90));
	}
}
