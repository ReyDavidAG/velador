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

public class DetectorShutdownTests
{
	static string? Model(string name)
	{
		for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
		{
			var file = Path.Combine(dir.FullName, "tools", "models", name);
			if (File.Exists(file)) return file;
		}
		return null;
	}

	[Fact]
	public async Task Disposing_while_detecting_waits_and_later_calls_return_nothing()
	{
		if (Model("yolox_tiny.onnx") is not { } model) return;
		var detector = new Velador.Engine.Vision.PersonDetector(model);
		var frame = new BgrFrame(new byte[1280 * 720 * 3], 1280, 720, 0);
		using var stop = new CancellationTokenSource();
		var loop = Task.Run(() => { while (!stop.IsCancellationRequested) detector.Detect(frame); });
		await Task.Delay(200);
		detector.Dispose();
		Assert.Empty(detector.Detect(frame));
		stop.Cancel();
		await loop;
	}
}

public class SceneChangeTests
{
	[Fact]
	public void Only_changes_inside_the_band_and_past_the_level_count()
	{
		var still = new byte[8 * 8];
		Array.Fill(still, (byte)100);
		var small = SceneChange.Shrink(still, 8, 8, 2);
		Assert.Equal(16, small.Length);
		Assert.All(small, b => Assert.Equal(100, b));

		var moved = (byte[])small.Clone();
		moved[0] = 255;
		moved[5] = 120;
		moved[9] = 200;
		Assert.Equal(1 / 8.0, SceneChange.Changed(small, moved, 4, 1, 2, 30));
	}
}
