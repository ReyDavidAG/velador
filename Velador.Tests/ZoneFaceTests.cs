using Velador.Core.Vision;

namespace Velador.Tests;

public class ZoneTests
{
	static readonly PtzZone Zone = new(-0.35, 0.35, -0.7, 0.2, 0.3, -0.5);

	[Fact]
	public void A_zone_needs_its_home_inside_and_some_width()
	{
		Assert.True(Zone.IsValid);
		Assert.False((Zone with { HomeX = 0.5 }).IsValid);
		Assert.False((Zone with { MaxX = -0.34 }).IsValid);
		Assert.False((Zone with { MinY = double.NaN }).IsValid);
		Assert.False((Zone with { MaxY = 1.2 }).IsValid);
	}

	[Fact]
	public void Moving_outward_at_a_limit_is_stopped_and_inward_is_allowed()
	{
		Assert.Equal((0, -0.1), Zone.Limit(0.34, 0, 0.2, -0.1));
		Assert.Equal((-0.2, 0), Zone.Limit(0.34, 0.19, -0.2, 0.3));
		Assert.Equal((0.2, 0.1), Zone.Limit(0, 0, 0.2, 0.1));
	}

	[Fact]
	public void From_outside_the_zone_only_the_way_back_is_allowed()
	{
		Assert.Equal((0, 0), Zone.Limit(0.6, 0, 0.3, 0));
		Assert.Equal((-0.3, 0), Zone.Limit(0.6, 0, -0.3, 0));
	}

	[Fact]
	public void The_stop_comes_early_enough_for_the_speed()
	{
		Assert.Equal((0.1, 0), Zone.Limit(0.3, 0, 0.1, 0));
		Assert.Equal((0, 0), Zone.Limit(0.3, 0, 0.5, 0));
	}
}

public class SpeedCurveTests
{
	static readonly SpeedCurve Curve = new([0.25, 0.5, 1.0], [0.06, 0.17, 0.5]);

	[Fact]
	public void The_command_for_a_rate_follows_the_measured_points()
	{
		Assert.Equal(0.25, Curve.Command(0.06), 3);
		Assert.Equal(0.5, Curve.Command(0.17), 3);
		Assert.Equal(1.0, Curve.Command(0.5), 3);
		Assert.Equal(0.75, Curve.Command(0.335), 3);
		Assert.Equal(0.125, Curve.Command(0.03), 3);
		Assert.Equal(1.0, Curve.Command(3), 3);
		Assert.Equal(0, Curve.Command(0));
	}

	[Fact]
	public void Rate_and_command_undo_each_other()
	{
		foreach (var command in new[] { 0.1, 0.3, 0.6, 0.9 }) Assert.Equal(command, Curve.Command(Curve.Rate(command)), 6);
		Assert.Equal(Curve.Rate(0.4), Curve.Rate(-0.4));
	}

	[Fact]
	public void A_camera_with_one_speed_is_told_apart()
	{
		var flat = new SpeedCurve([0.25, 0.5, 1.0], [0.2, 0.2, 0.2]);

		Assert.True(Curve.IsProportional);
		Assert.False(flat.IsProportional);
		Assert.Equal(0.25, flat.Command(0.2), 3);
	}
}

public class FaceTests
{
	const int Size = 48;

	static double Truth(double x, double y)
	{
		var disk = Math.Sqrt(Math.Pow(x - 24, 2) + Math.Pow(y - 22, 2)) < 13 ? 70 : 0;
		var eyes = Math.Abs(y - 18) < 1.6 && (Math.Abs(x - 18) < 3 || Math.Abs(x - 30) < 3) ? -90 : 0;
		var mouth = Math.Abs(y - 30) < 1.2 && Math.Abs(x - 24) < 6 ? -70 : 0;
		return 110 + disk + eyes + mouth + 12 * Math.Sin(x * 0.9) * Math.Cos(y * 0.7);
	}

	static FacePoint[] Points(double dx, double dy) => [new(18 + dx, 18 + dy), new(30 + dx, 18 + dy), new(24 + dx, 24 + dy), new(19 + dx, 30 + dy), new(29 + dx, 30 + dy)];

	static FaceShot Shot(double dx, double dy, double noise, Random random)
	{
		var bgr = new byte[Size * Size * 3];
		for (var y = 0; y < Size; y++)
			for (var x = 0; x < Size; x++)
			{
				double sum = 0;
				for (var sy = 0; sy < 4; sy++)
					for (var sx = 0; sx < 4; sx++) sum += Truth(x - dx + (sx + 0.5) / 4 - 0.5, y - dy + (sy + 0.5) / 4 - 0.5);
				var u1 = 1 - random.NextDouble();
				var u2 = random.NextDouble();
				var value = sum / 16 + noise * Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
				var b = (byte)Math.Clamp(Math.Round(value), 0, 255);
				bgr[(y * Size + x) * 3] = bgr[(y * Size + x) * 3 + 1] = bgr[(y * Size + x) * 3 + 2] = b;
			}
		return new FaceShot(bgr, Size, Size, Points(dx + (random.NextDouble() - 0.5) * 0.8, dy + (random.NextDouble() - 0.5) * 0.8));
	}

	static double Error(byte[] bgr, int width, int height)
	{
		double sum = 0;
		var n = 0;
		for (var y = 12; y < height - 12; y++)
			for (var x = 12; x < width - 12; x++)
			{
				var truth = Truth((x + 0.5) / FaceFusion.Scale - 0.5, (y + 0.5) / FaceFusion.Scale - 0.5);
				sum += Math.Pow(bgr[(y * width + x) * 3] - truth, 2);
				n++;
			}
		return Math.Sqrt(sum / n);
	}

	[Fact]
	public void Fusing_shifted_noisy_frames_gets_closer_to_the_real_face_than_one_frame()
	{
		var random = new Random(7);
		var shots = new List<FaceShot> { Shot(0, 0, 14, random) };
		foreach (var (dx, dy) in new[] { (0.5, 0.25), (-0.75, 0.5), (1.25, -0.5), (-0.25, -1.25), (0.75, 0.75), (-1.5, 0.25), (0.25, -0.75) }) shots.Add(Shot(dx, dy, 14, random));

		var fused = FaceFusion.Fuse(shots);

		Assert.NotNull(fused);
		Assert.Equal((Size * 2, Size * 2), (fused.Width, fused.Height));
		Assert.True(fused.Used >= 6, $"used {fused.Used}");
		var single = Error(FaceFusion.Upscale(shots[0].Bgr, Size, Size), Size * 2, Size * 2);
		var combined = Error(fused.Bgr, fused.Width, fused.Height);
		Assert.True(combined < single * 0.75, $"fused {combined:0.0} vs single {single:0.0}");
	}

	[Fact]
	public void A_frame_of_something_else_is_left_out()
	{
		var random = new Random(3);
		var shots = new List<FaceShot> { Shot(0, 0, 6, random), Shot(0.5, 0.5, 6, random), Shot(-0.5, 0.25, 6, random) };
		var other = new byte[Size * Size * 3];
		random.NextBytes(other);
		shots.Add(new FaceShot(other, Size, Size, Points(0, 0)));

		var fused = FaceFusion.Fuse(shots);

		Assert.NotNull(fused);
		Assert.Equal(3, fused.Used);
	}

	[Fact]
	public void One_frame_alone_is_not_fused()
	{
		Assert.Null(FaceFusion.Fuse([Shot(0, 0, 5, new Random(1))]));
	}

	[Fact]
	public void A_sharp_face_scores_above_a_blurred_one_and_a_frontal_above_a_profile()
	{
		var random = new Random(5);
		var sharp = Shot(0, 0, 0, random);
		var blurred = sharp.Bgr.ToArray();
		for (var y = 1; y < Size - 1; y++)
			for (var x = 1; x < Size - 1; x++)
			{
				var sum = 0;
				for (var dy = -1; dy <= 1; dy++)
					for (var dx = -1; dx <= 1; dx++) sum += sharp.Bgr[((y + dy) * Size + x + dx) * 3];
				blurred[(y * Size + x) * 3] = blurred[(y * Size + x) * 3 + 1] = blurred[(y * Size + x) * 3 + 2] = (byte)(sum / 9);
			}

		Assert.True(FaceQuality.Sharpness(sharp.Bgr, Size, Size) > FaceQuality.Sharpness(blurred, Size, Size) * 2);
		Assert.True(FaceQuality.Frontal(Points(0, 0)) > 0.9);
		Assert.True(FaceQuality.Frontal([new(18, 18), new(30, 18), new(29, 24)]) < 0.3);
	}

	[Fact]
	public void A_smaller_face_looking_at_the_camera_beats_a_bigger_one_looking_down_or_sideways()
	{
		FacePoint[] front = [new(18, 18), new(30, 18), new(24, 24), new(19, 30), new(29, 30)];
		FacePoint[] down = [new(18, 18), new(30, 18), new(24, 22), new(19, 25), new(29, 25)];
		FacePoint[] side = [new(18, 18), new(30, 18), new(30, 24), new(22, 30), new(30, 30)];

		Assert.True(FaceQuality.Upright(front) > 0.9);
		Assert.True(FaceQuality.Upright(down) < 0.2);
		Assert.True(FaceQuality.Rank(0.9, 55, front, 144) > FaceQuality.Rank(0.9, 75, down, 144));
		Assert.True(FaceQuality.Rank(0.9, 55, front, 144) > FaceQuality.Rank(0.9, 75, side, 144));
	}

	[Fact]
	public void The_best_three_moments_of_a_sighting_are_kept_apart_in_time()
	{
		var moments = new List<FaceMoment>();
		var box = new Box(0.4, 0.4, 0.05, 0.05);
		FaceMoments.Add(moments, new FaceMoment(1000, box, 2));
		FaceMoments.Add(moments, new FaceMoment(1400, box, 5));
		FaceMoments.Add(moments, new FaceMoment(1500, box, 3));
		FaceMoments.Add(moments, new FaceMoment(4000, box, 1));
		FaceMoments.Add(moments, new FaceMoment(8000, box, 9));
		FaceMoments.Add(moments, new FaceMoment(12000, box, 4));

		Assert.Equal([8000L, 1400L, 12000L], moments.Select(m => m.AtMs));
	}
}
