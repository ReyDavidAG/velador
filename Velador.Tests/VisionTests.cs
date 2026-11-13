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
	public void It_turns_toward_a_person_off_center_and_stops_when_lost()
	{
		var follow = new FollowController();
		var right = new TrackState(1, Box.FromCenter(0.85, 0.6, 0.1, 0.4), 0, 0, 0.9, true, 0, 0);

		for (var i = 0; i < 5; i++) follow.Step(right, i * 100);
		Assert.True(follow.Command.X > 0.3);

		for (var i = 5; i < 15; i++) follow.Step(null, i * 100);
		Assert.True(follow.Command.IsZero);
	}

	[Fact]
	public void A_person_crossing_fast_stays_in_view_with_prediction()
	{
		var worst = Simulate(new FollowTuning(), personSpeed: 0.25);
		Assert.True(worst < 0.5, $"lo perdió: llegó a {worst:0.00} del centro");
	}

	[Fact]
	public void Prediction_keeps_the_person_closer_than_chasing_where_it_was()
	{
		var predicting = Simulate(new FollowTuning(), personSpeed: 0.2, settleAfterS: 3);
		var chasing = Simulate(new FollowTuning(LagS: 0, HorizonS: 0), personSpeed: 0.2, settleAfterS: 3);
		Assert.True(predicting < chasing, $"con predicción {predicting:0.000}, sin ella {chasing:0.000}");
	}

	static double Simulate(FollowTuning tuning, double personSpeed, double settleAfterS = 0)
	{
		const double latencyS = 0.6;
		const double dt = 0.1;
		var follow = new FollowController(tuning);
		var tracker = new Tracker();
		double person = 0, view = 0, worst = 0;
		var history = new Queue<(double Person, double View)>();
		var commands = new Queue<double>();
		for (var step = 0; step < 80; step++)
		{
			var t = step * dt;
			person += personSpeed * dt;
			history.Enqueue((person, view));
			var (seenPerson, seenView) = history.Count > latencyS / dt ? history.Dequeue() : history.Peek();
			var imageX = 0.5 + (seenPerson - seenView);
			var tracks = tracker.Update(Math.Abs(imageX - 0.5) < 0.5 ? [new Detection(Box.FromCenter(imageX, 0.6, 0.1, 0.4), 0.9)] : [], (long)(t * 1000));
			var cmd = follow.Step(FollowController.PickTarget(tracks, null), (long)(t * 1000)).X;
			commands.Enqueue(cmd);
			var applied = commands.Count > 2 ? commands.Dequeue() : 0;
			view += tuning.GainX * applied * dt;
			if (t >= settleAfterS) worst = Math.Max(worst, Math.Abs(person - view));
		}
		return worst;
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
