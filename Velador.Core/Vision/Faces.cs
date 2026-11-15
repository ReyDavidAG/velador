namespace Velador.Core.Vision;

public readonly record struct FacePoint(double X, double Y);

public sealed record FaceShot(byte[] Bgr, int Width, int Height, FacePoint[] Points, double Score = 1);

public sealed record FusedFace(byte[] Bgr, int Width, int Height, int Used);

public static class FaceQuality
{
	public static float[] Gray(byte[] bgr, int width, int height)
	{
		var gray = new float[width * height];
		for (var i = 0; i < gray.Length; i++) gray[i] = 0.114f * bgr[i * 3] + 0.587f * bgr[i * 3 + 1] + 0.299f * bgr[i * 3 + 2];
		return gray;
	}

	public static double Sharpness(byte[] bgr, int width, int height) => Sharpness(Gray(bgr, width, height), width, height);

	public static double Sharpness(float[] gray, int width, int height)
	{
		if (width < 3 || height < 3) return 0;
		double sum = 0, squares = 0;
		var n = 0;
		for (var y = 1; y < height - 1; y++)
			for (var x = 1; x < width - 1; x++)
			{
				var i = y * width + x;
				var l = 4 * gray[i] - gray[i - 1] - gray[i + 1] - gray[i - width] - gray[i + width];
				sum += l;
				squares += l * l;
				n++;
			}
		var mean = sum / n;
		return squares / n - mean * mean;
	}

	public static double Frontal(IReadOnlyList<FacePoint> points)
	{
		if (points.Count < 3) return 0.5;
		var eyes = Math.Abs(points[1].X - points[0].X);
		if (eyes <= 0) return 0;
		var off = Math.Abs(points[2].X - (points[0].X + points[1].X) / 2) / eyes;
		return Math.Clamp(1 - off * 2, 0, 1);
	}

	public static bool Plausible(IReadOnlyList<FacePoint> points, double faceWidth)
	{
		if (points.Count < 5 || faceWidth <= 0) return false;
		var dx = points[1].X - points[0].X;
		var dy = points[1].Y - points[0].Y;
		var eyes = Math.Sqrt(dx * dx + dy * dy);
		if (dx <= 0 || eyes < faceWidth * 0.22 || eyes > faceWidth * 0.8) return false;
		if (Math.Abs(Math.Atan2(dy, dx)) > 0.6) return false;
		var eyeY = (points[0].Y + points[1].Y) / 2;
		var mouthY = (points[3].Y + points[4].Y) / 2;
		if (points[2].Y <= eyeY || mouthY <= points[2].Y) return false;
		if (mouthY - eyeY < eyes * 0.5 || mouthY - eyeY > eyes * 2.2) return false;
		return points[2].X > Math.Min(points[0].X, points[1].X) - eyes * 0.35 && points[2].X < Math.Max(points[0].X, points[1].X) + eyes * 0.35;
	}

	public static double EyeDistance(IReadOnlyList<FacePoint> points) =>
		points.Count < 2 ? 0 : Math.Sqrt(Math.Pow(points[1].X - points[0].X, 2) + Math.Pow(points[1].Y - points[0].Y, 2));

	public static double Upright(IReadOnlyList<FacePoint> points)
	{
		if (points.Count < 5) return 0.5;
		var eyes = EyeDistance(points);
		if (eyes <= 0) return 0;
		var drop = (points[3].Y + points[4].Y) / 2 - (points[0].Y + points[1].Y) / 2;
		return Math.Clamp((drop / eyes - 0.5) / 0.5, 0, 1);
	}

	public static double Rank(double score, double faceWidth, IReadOnlyList<FacePoint> points, double sharpness) =>
		score * Math.Sqrt(Math.Max(0, faceWidth)) * (0.15 + 0.85 * Frontal(points) * Upright(points)) * Math.Clamp(Math.Sqrt(sharpness) / 12, 0.5, 1.5);
}

public readonly record struct FaceMoment(long AtMs, Box Box, double Quality);

public static class FaceMoments
{
	public const int Keep = 3;
	public const long ApartMs = 1200;

	public static void Add(List<FaceMoment> moments, FaceMoment seen)
	{
		var near = moments.FindIndex(m => Math.Abs(m.AtMs - seen.AtMs) < ApartMs);
		if (near >= 0)
		{
			if (moments[near].Quality >= seen.Quality) return;
			moments[near] = seen;
		}
		else moments.Add(seen);
		moments.Sort((a, b) => b.Quality.CompareTo(a.Quality));
		if (moments.Count > Keep) moments.RemoveRange(Keep, moments.Count - Keep);
	}
}

public static class FaceFusion
{
	public const int Scale = 2;
	const double MinMatch = 0.8;
	public const int MinFrames = 3;

	readonly record struct Similarity(double A, double B, double Tx, double Ty)
	{
		public (double X, double Y) Map(double x, double y) => (A * x - B * y + Tx, B * x + A * y + Ty);
		public Similarity Shifted(double dx, double dy) => this with { Tx = Tx + dx, Ty = Ty + dy };
	}

	public static FusedFace? Fuse(IReadOnlyList<FaceShot> shots)
	{
		if (shots.Count < MinFrames) return null;
		var grays = shots.Select(s => FaceQuality.Gray(s.Bgr, s.Width, s.Height)).ToList();
		var sharp = grays.Select((g, i) => FaceQuality.Sharpness(g, shots[i].Width, shots[i].Height)).ToList();
		var main = shots[0];
		var width = main.Width * Scale;
		var height = main.Height * Scale;
		var layers = new List<(float[] Pixels, double Weight)> { (Warp(main, new Similarity(1, 0, 0, 0), width, height), 1) };
		for (var i = 1; i < shots.Count; i++)
		{
			if (Align(main.Points, shots[i].Points) is not { } rough) continue;
			var (refined, match) = Refine(grays[0], main.Width, main.Height, grays[i], shots[i].Width, shots[i].Height, rough);
			if (match < MinMatch) continue;
			layers.Add((Warp(shots[i], refined, width, height), (match - 0.5) * 2 * Math.Clamp(sharp[i] / Math.Max(1e-6, sharp[0]), 0.2, 1)));
		}
		if (layers.Count < MinFrames) return null;
		var fused = new byte[width * height * 3];
		var trim = layers.Count >= 4;
		for (var i = 0; i < fused.Length; i++)
		{
			double sum = 0, weights = 0;
			int low = -1, high = -1;
			if (trim)
			{
				low = high = 0;
				for (var k = 1; k < layers.Count; k++)
				{
					if (layers[k].Pixels[i] < layers[low].Pixels[i]) low = k;
					if (layers[k].Pixels[i] >= layers[high].Pixels[i]) high = k;
				}
			}
			for (var k = 0; k < layers.Count; k++)
			{
				if (k == low || k == high) continue;
				sum += layers[k].Pixels[i] * layers[k].Weight;
				weights += layers[k].Weight;
			}
			fused[i] = (byte)Math.Clamp(Math.Round(sum / weights), 0, 255);
		}
		return new FusedFace(Sharpen(fused, width, height, 0.6), width, height, layers.Count);
	}

	static Similarity? Align(FacePoint[] from, FacePoint[] to)
	{
		var n = Math.Min(from.Length, to.Length);
		if (n < 2) return null;
		double mx = 0, my = 0, ux = 0, uy = 0;
		for (var i = 0; i < n; i++)
		{
			mx += from[i].X;
			my += from[i].Y;
			ux += to[i].X;
			uy += to[i].Y;
		}
		mx /= n;
		my /= n;
		ux /= n;
		uy /= n;
		double a = 0, b = 0, spread = 0;
		for (var i = 0; i < n; i++)
		{
			var x = from[i].X - mx;
			var y = from[i].Y - my;
			var u = to[i].X - ux;
			var v = to[i].Y - uy;
			a += x * u + y * v;
			b += x * v - y * u;
			spread += x * x + y * y;
		}
		if (spread <= 0) return null;
		a /= spread;
		b /= spread;
		var size = Math.Sqrt(a * a + b * b);
		if (size is < 0.85 or > 1.18) return null;
		return new Similarity(a, b, ux - (a * mx - b * my), uy - (b * mx + a * my));
	}

	static (Similarity Map, double Match) Refine(float[] reference, int rw, int rh, float[] other, int ow, int oh, Similarity rough)
	{
		var x0 = (int)(rw * 0.2);
		var x1 = (int)(rw * 0.8);
		var y0 = (int)(rh * 0.2);
		var y1 = (int)(rh * 0.8);
		var stride = Math.Max(1, (int)Math.Sqrt((x1 - x0) * (double)(y1 - y0) / 2500));
		var best = rough;
		var bestScore = Match(reference, rw, other, ow, oh, rough, x0, x1, y0, y1, stride);
		foreach (var (reach, step) in new[] { (2.0, 0.5), (0.5, 0.125) })
		{
			var center = best;
			for (var dy = -reach; dy <= reach + 1e-9; dy += step)
				for (var dx = -reach; dx <= reach + 1e-9; dx += step)
				{
					var candidate = center.Shifted(dx, dy);
					var score = Match(reference, rw, other, ow, oh, candidate, x0, x1, y0, y1, stride);
					if (score <= bestScore) continue;
					bestScore = score;
					best = candidate;
				}
		}
		return (best, bestScore);
	}

	static double Match(float[] reference, int rw, float[] other, int ow, int oh, Similarity map, int x0, int x1, int y0, int y1, int stride)
	{
		double sa = 0, sb = 0, saa = 0, sbb = 0, sab = 0;
		var n = 0;
		for (var y = y0; y < y1; y += stride)
			for (var x = x0; x < x1; x += stride)
			{
				var (u, v) = map.Map(x, y);
				if (u < 0 || v < 0 || u > ow - 1 || v > oh - 1) continue;
				var a = reference[y * rw + x];
				var b = Sample(other, ow, oh, u, v);
				sa += a;
				sb += b;
				saa += a * a;
				sbb += b * b;
				sab += a * b;
				n++;
			}
		if (n < 16) return -1;
		var va = saa - sa * sa / n;
		var vb = sbb - sb * sb / n;
		return va <= 0 || vb <= 0 ? -1 : (sab - sa * sb / n) / Math.Sqrt(va * vb);
	}

	static double Sample(float[] gray, int width, int height, double x, double y)
	{
		var xi = Math.Min(width - 2, (int)x);
		var yi = Math.Min(height - 2, (int)y);
		var fx = x - xi;
		var fy = y - yi;
		var i = yi * width + xi;
		return gray[i] * (1 - fx) * (1 - fy) + gray[i + 1] * fx * (1 - fy) + gray[i + width] * (1 - fx) * fy + gray[i + width + 1] * fx * fy;
	}

	static float[] Warp(FaceShot shot, Similarity map, int width, int height)
	{
		var sum = new float[width * height * 3];
		for (var y = 0; y < height; y++)
			for (var x = 0; x < width; x++)
			{
				var (u, v) = map.Map((x + 0.5) / Scale - 0.5, (y + 0.5) / Scale - 0.5);
				u = Math.Clamp(u, 0, shot.Width - 1.001);
				v = Math.Clamp(v, 0, shot.Height - 1.001);
				var xi = (int)u;
				var yi = (int)v;
				var fx = u - xi;
				var fy = v - yi;
				var i = (yi * shot.Width + xi) * 3;
				var down = i + shot.Width * 3;
				var t = (y * width + x) * 3;
				for (var c = 0; c < 3; c++)
					sum[t + c] = (float)(shot.Bgr[i + c] * (1 - fx) * (1 - fy) + shot.Bgr[i + 3 + c] * fx * (1 - fy) + shot.Bgr[down + c] * (1 - fx) * fy + shot.Bgr[down + 3 + c] * fx * fy);
			}
		return sum;
	}

	public static byte[] Upscale(byte[] bgr, int width, int height)
	{
		return Warp(new FaceShot(bgr, width, height, []), new Similarity(1, 0, 0, 0), width * Scale, height * Scale).Select(v => (byte)Math.Clamp(Math.Round(v), 0, 255)).ToArray();
	}

	static byte[] Sharpen(byte[] bgr, int width, int height, double amount)
	{
		var result = new byte[bgr.Length];
		for (var y = 0; y < height; y++)
			for (var x = 0; x < width; x++)
				for (var c = 0; c < 3; c++)
				{
					double blur = 0;
					var n = 0;
					for (var dy = -1; dy <= 1; dy++)
						for (var dx = -1; dx <= 1; dx++)
						{
							var yy = y + dy;
							var xx = x + dx;
							if (yy < 0 || xx < 0 || yy >= height || xx >= width) continue;
							blur += bgr[(yy * width + xx) * 3 + c];
							n++;
						}
					var i = (y * width + x) * 3 + c;
					result[i] = (byte)Math.Clamp(Math.Round(bgr[i] + amount * (bgr[i] - blur / n)), 0, 255);
				}
		return result;
	}
}
