using Velador.Core.Vision;

namespace Velador.Tests;

public class TrackerTests
{
	static Detection Person(double cx, double cy, double score = 0.9) => new(Box.FromCenter(cx, cy, 0.1, 0.3), score);

	[Fact]
	public void A_walking_person_keeps_one_id_and_its_speed_is_estimated()
	{
		var tracker = new Tracker();
		IReadOnlyList<TrackState> tracks = [];
		for (var i = 0; i < 20; i++) tracks = tracker.Update([Person(0.2 + i * 0.02, 0.5)], i * 100);

		var track = Assert.Single(tracks);
		Assert.Equal(1, track.Id);
		Assert.Equal(0.2, track.Vx, 1);
		Assert.Equal(0, track.Vy, 1);
	}

	[Fact]
	public void A_fast_person_is_not_lost_between_frames()
	{
		var tracker = new Tracker();
		IReadOnlyList<TrackState> tracks = [];
		for (var i = 0; i < 12; i++) tracks = tracker.Update([Person(0.05 + i * 0.08, 0.5)], i * 100);

		Assert.Equal(1, Assert.Single(tracks).Id);
	}

	[Fact]
	public void A_missed_frame_keeps_the_track_and_a_long_absence_drops_it()
	{
		var tracker = new Tracker();
		for (var i = 0; i < 5; i++) tracker.Update([Person(0.5, 0.5)], i * 100);

		var missed = tracker.Update([], 600);
		Assert.False(Assert.Single(missed).SeenNow);
		Assert.Equal(1, Assert.Single(tracker.Update([Person(0.5, 0.5)], 700)).Id);
		Assert.Empty(tracker.Update([], 2500));
	}

	[Fact]
	public void Two_people_get_two_ids_and_a_single_blip_is_not_a_person()
	{
		var tracker = new Tracker();
		IReadOnlyList<TrackState> tracks = [];
		for (var i = 0; i < 5; i++) tracks = tracker.Update([Person(0.2, 0.5), Person(0.8, 0.5), .. i == 2 ? [Person(0.5, 0.2)] : Array.Empty<Detection>()], i * 100);

		Assert.Equal([1, 2], tracks.Select(t => t.Id).Order());
	}

	[Fact]
	public void Weak_detections_only_extend_existing_tracks()
	{
		var tracker = new Tracker();
		for (var i = 0; i < 4; i++) tracker.Update([Person(0.3, 0.5)], i * 100);

		var tracks = tracker.Update([Person(0.31, 0.5, 0.3), Person(0.8, 0.5, 0.3)], 400);

		Assert.True(Assert.Single(tracks).SeenNow);
	}
}

public class FollowTests
{
	[Fact]
	public void A_centered_still_person_does_not_move_the_camera()
	{
		var follow = new FollowController();
		var still = new TrackState(1, Box.FromCenter(0.5, 0.6, 0.1, 0.4), 0, 0, 0.9, true, 0, 0);

		for (var i = 0; i < 10; i++) follow.Step(still, i * 100);

		Assert.True(follow.Command.IsZero);
	}

	[Fact]
	public void It_turns_toward_a_person_off_center_keeps_going_a_moment_and_stops_when_lost()
	{
		var follow = new FollowController();
		for (var i = 0; i < 5; i++) follow.Step(new TrackState(1, Box.FromCenter(0.85, 0.6, 0.1, 0.4), 0, 0, 0.9, true, 0, i * 100), i * 100);
		Assert.True(follow.Command.X > 0.3);

		follow.Step(null, 600);
		Assert.True(follow.Coasting);

		for (var i = 7; i < 55; i++) follow.Step(null, i * 100);
		Assert.True(follow.Command.IsZero);
		Assert.False(follow.Coasting);
	}

	[Theory]
	[InlineData(0.4, 1)]
	[InlineData(-0.4, -1)]
	public void When_the_person_leaves_by_one_side_it_turns_toward_that_side(double offset, int side)
	{
		var follow = new FollowController();
		for (var i = 0; i < 5; i++) follow.Step(new TrackState(1, Box.FromCenter(0.5 + offset, 0.6, 0.1, 0.4), 0, 0, 0.9, true, 0, i * 100), i * 100);
		for (var i = 5; i < 12; i++) follow.Step(null, i * 100);

		Assert.True(follow.Coasting);
		Assert.Equal(side, Math.Sign(follow.Command.X));
	}

	[Theory]
	[InlineData(0.15, 0.95)]
	[InlineData(0.30, 0.75)]
	public void With_the_measured_camera_it_keeps_a_walking_person_in_view(double speed, double atLeast)
	{
		var predicting = InView(new FollowTuning(GainX: 0.86, GainY: 0.63, LagS: 1.56), speed);
		var chasing = InView(new FollowTuning(GainX: 0.86, GainY: 0.63, LagS: 0, HorizonS: 0), speed);

		Assert.True(predicting >= atLeast, $"con predicción {predicting:P0}");
		Assert.True(predicting > chasing + 0.2, $"con predicción {predicting:P0}, sin ella {chasing:P0}");
	}

	static double InView(FollowTuning tuning, double speed)
	{
		const double dt = 0.1, gain = 0.86;
		var inView = 0;
		var total = 0;
		for (var seed = 0; seed < 6; seed++)
		{
			var rng = new Random(seed);
			var follow = new FollowController(tuning);
			var tracker = new Tracker();
			double person = 0, view = 0;
			var seen = new Queue<(double Person, double View)>();
			var commands = new Queue<double>();
			for (var step = 0; step < 150; step++)
			{
				var t = step * dt;
				person += (t is >= 2 and < 9 ? speed : 0) * dt;
				seen.Enqueue((person, view));
				var (p, v) = seen.Count > 13 ? seen.Dequeue() : seen.Peek();
				var x = 0.5 + p - v + (rng.NextDouble() - 0.5) * 0.04;
				var ms = (long)(t * 1000);
				var tracks = tracker.Update(Math.Abs(x - 0.5) < 0.5 ? [new Detection(Box.FromCenter(x, 0.5, 0.15, 0.45), 0.9)] : [], ms);
				commands.Enqueue(follow.Step(follow.Pick(tracks, ms), ms).X);
				view += gain * (commands.Count > 3 ? commands.Dequeue() : 0) * dt;
				inView += Math.Abs(person - view) < 0.4 ? 1 : 0;
				total++;
			}
		}
		return (double)inView / total;
	}
}

public class VisionPrimitiveTests
{
	static float[] Pattern(int w, int h, int shiftX, int shiftY)
	{
		var rng = new Random(7);
		var noise = Enumerable.Range(0, 400 * 400).Select(_ => (float)rng.NextDouble() * 255).ToArray();
		var g = new float[w * h];
		for (var y = 0; y < h; y++)
			for (var x = 0; x < w; x++)
				g[y * w + x] = noise[(y + 100 + shiftY) * 400 + x + 100 + shiftX];
		return g;
	}

	[Fact]
	public void Global_motion_reads_the_direction_and_size_of_a_pan()
	{
		var (dx, dy) = GlobalMotion.Shift(Pattern(160, 90, 0, 0), Pattern(160, 90, 12, -5), 160, 90);

		Assert.Equal(-12.0 / 160, dx, 3);
		Assert.Equal(5.0 / 90, dy, 3);
	}

	static byte[] Shirt(byte b, byte g, byte r, byte pb, byte pg, byte pr, int w = 20, int h = 40)
	{
		var px = new byte[w * h * 3];
		for (var y = 0; y < h; y++)
			for (var x = 0; x < w; x++)
			{
				var top = y < h / 2;
				px[(y * w + x) * 3] = top ? b : pb;
				px[(y * w + x) * 3 + 1] = top ? g : pg;
				px[(y * w + x) * 3 + 2] = top ? r : pr;
			}
		return px;
	}

	[Fact]
	public void The_same_clothes_look_alike_and_different_clothes_do_not()
	{
		var redJeans = Appearance.Describe(Shirt(30, 30, 200, 120, 60, 20), 20, 40);
		var redJeansAgain = Appearance.Describe(Shirt(40, 35, 190, 110, 55, 25), 20, 40);
		var greenBlack = Appearance.Describe(Shirt(40, 180, 40, 15, 15, 15), 20, 40);

		Assert.True(Appearance.Similarity(redJeans, redJeansAgain) > PeopleGrouping.SameLook);
		Assert.True(Appearance.Similarity(redJeans, greenBlack) < 0.5);
		Assert.Equal(7, PeopleGrouping.Match(redJeansAgain, [(3, greenBlack), (7, redJeans)]));
		Assert.Null(PeopleGrouping.Match(greenBlack, [(7, redJeans)]));
		Assert.Equal(redJeans, Appearance.FromBytes(Appearance.ToBytes(redJeans)));
	}

	[Fact]
	public void Png_has_the_signature_and_the_size()
	{
		var png = Png.FromBgr(Shirt(1, 2, 3, 4, 5, 6), 20, 40);

		Assert.Equal([0x89, 0x50, 0x4E, 0x47], png[..4]);
		Assert.Equal(20, png[19]);
		Assert.Equal(40, png[23]);
	}

	[Fact]
	public void An_upside_down_camera_turns_the_other_way()
	{
		Assert.Equal((-0.4, 0.2), PtzOrientation.ToCamera(0.4, -0.2, 180));
		Assert.Equal((0.4, -0.2), PtzOrientation.ToCamera(0.4, -0.2, 0));
	}
}

public class PeopleGroupingTests
{
	[Fact]
	public void Grouping_by_the_group_average_keeps_one_person_together()
	{
		static float[] Look(double red, double blue) => Appearance.Describe(Enumerable.Range(0, 20 * 40).SelectMany(i => i < 400 ? new[] { (byte)(blue * 255), (byte)40, (byte)(red * 255) } : new[] { (byte)120, (byte)60, (byte)20 }).ToArray(), 20, 40);
		var looks = new[] { Look(0.8, 0.1), Look(0.1, 0.8), Look(0.75, 0.15), Look(0.15, 0.75), Look(0.82, 0.08) };

		var groups = PeopleGrouping.Cluster(looks);

		Assert.Equal([[0, 2, 4], [1, 3]], groups);
	}

	[Fact]
	public void A_png_comes_back_with_the_same_pixels()
	{
		var bgr = Enumerable.Range(0, 6 * 4 * 3).Select(i => (byte)(i * 7)).ToArray();

		var (w, h, back) = Png.ToBgr(Png.FromBgr(bgr, 6, 4));

		Assert.Equal((6, 4), (w, h));
		Assert.Equal(bgr, back);
	}
}

public class YuvTests
{
	[Fact]
	public void Gray_red_and_blue_come_out_in_bgr()
	{
		static byte[] Plain(byte y, byte u, byte v) => [.. Enumerable.Repeat(y, 4 * 2), .. Enumerable.Repeat(u, 2), .. Enumerable.Repeat(v, 2)];
		var bgr = new byte[4 * 2 * 3];

		Yuv.ToBgr(Plain(126, 128, 128), 4, 2, bgr);
		Assert.All(bgr, b => Assert.InRange(b, 125, 130));

		Yuv.ToBgr(Plain(81, 90, 240), 4, 2, bgr);
		Assert.InRange(bgr[2], 240, 255);
		Assert.InRange(bgr[0], 0, 10);

		Yuv.ToBgr(Plain(41, 240, 110), 4, 2, bgr);
		Assert.InRange(bgr[0], 240, 255);
		Assert.InRange(bgr[2], 0, 10);
	}
}
