using System.IO.Compression;

namespace Velador.Core.Vision;

public sealed class BgrFrame(byte[] pixels, int width, int height, long capturedMs)
{
	public byte[] Pixels { get; } = pixels;
	public int Width { get; } = width;
	public int Height { get; } = height;
	public long CapturedMs { get; } = capturedMs;

	public (int X, int Y, int W, int H) PixelRect(Box box, double pad = 0)
	{
		var x0 = (int)Math.Floor(Math.Clamp(box.X - box.W * pad, 0, 1) * Width);
		var y0 = (int)Math.Floor(Math.Clamp(box.Y - box.H * pad, 0, 1) * Height);
		var x1 = (int)Math.Ceiling(Math.Clamp(box.X + box.W * (1 + pad), 0, 1) * Width);
		var y1 = (int)Math.Ceiling(Math.Clamp(box.Y + box.H * (1 + pad), 0, 1) * Height);
		return (x0, y0, Math.Max(0, x1 - x0), Math.Max(0, y1 - y0));
	}

	public byte[] Crop(int x, int y, int w, int h)
	{
		var crop = new byte[w * h * 3];
		for (var row = 0; row < h; row++)
			Buffer.BlockCopy(Pixels, ((y + row) * Width + x) * 3, crop, row * w * 3, w * 3);
		return crop;
	}

	public float[] Gray(int width, int height)
	{
		var gray = new float[width * height];
		for (var y = 0; y < height; y++)
		{
			var sy = Math.Min(Height - 1, y * Height / height);
			for (var x = 0; x < width; x++)
			{
				var i = (sy * Width + Math.Min(Width - 1, x * Width / width)) * 3;
				gray[y * width + x] = 0.114f * Pixels[i] + 0.587f * Pixels[i + 1] + 0.299f * Pixels[i + 2];
			}
		}
		return gray;
	}
}

public static class GlobalMotion
{
	public static (double Dx, double Dy) Shift(float[] before, float[] after, int width, int height, double maxFraction = 0.3)
	{
		var dx = Best(Profile(before, width, height, columns: true), Profile(after, width, height, columns: true), (int)(width * maxFraction));
		var dy = Best(Profile(before, width, height, columns: false), Profile(after, width, height, columns: false), (int)(height * maxFraction));
		return (dx / width, dy / height);
	}

	static double[] Profile(float[] gray, int width, int height, bool columns)
	{
		var n = columns ? width : height;
		var profile = new double[n];
		for (var y = 0; y < height; y++)
			for (var x = 0; x < width; x++)
				profile[columns ? x : y] += gray[y * width + x];
		var gradient = new double[n];
		for (var i = 1; i < n; i++) gradient[i] = profile[i] - profile[i - 1];
		return gradient;
	}

	static double Best(double[] a, double[] b, int maxShift)
	{
		var best = 0;
		var bestScore = double.NegativeInfinity;
		for (var s = -maxShift; s <= maxShift; s++)
		{
			double sum = 0, na = 0, nb = 0;
			for (var i = Math.Max(1, -s); i < a.Length && i + s < b.Length; i++)
			{
				if (i + s < 1) continue;
				sum += a[i] * b[i + s];
				na += a[i] * a[i];
				nb += b[i + s] * b[i + s];
			}
			var score = na > 0 && nb > 0 ? sum / Math.Sqrt(na * nb) : 0;
			if (score > bestScore)
			{
				bestScore = score;
				best = s;
			}
		}
		return best;
	}
}

public static class Appearance
{
	const int HueBins = 12;
	const int Parts = 2;
	const int PerPart = HueBins * 2 + 4;

	public static float[] Describe(byte[] bgr, int width, int height)
	{
		var d = new float[Parts * PerPart];
		var bands = new[] { (0.15, 0.5), (0.5, 0.95) };
		for (var part = 0; part < Parts; part++)
		{
			var (from, to) = bands[part];
			var offset = part * PerPart;
			float total = 0;
			for (var y = (int)(height * from); y < (int)(height * to); y++)
				for (var x = (int)(width * 0.2); x < (int)(width * 0.8); x++)
				{
					var i = (y * width + x) * 3;
					var (h, s, v) = Hsv(bgr[i + 2], bgr[i + 1], bgr[i]);
					total++;
					if (s < 0.25 || v < 0.15)
					{
						d[offset + HueBins * 2 + Math.Min(3, (int)(v * 4))]++;
						continue;
					}
					var at = h / 360 * HueBins;
					var low = (int)Math.Floor(at);
					var frac = (float)(at - low);
					var shade = v < 0.5 ? HueBins : 0;
					d[offset + shade + low % HueBins] += 1 - frac;
					d[offset + shade + (low + 1) % HueBins] += frac;
				}
			if (total > 0)
				for (var i = 0; i < PerPart; i++) d[offset + i] /= total;
		}
		return d;
	}

	public static double Similarity(float[] a, float[] b)
	{
		if (a.Length != b.Length || a.Length == 0) return 0;
		double sum = 0;
		for (var part = 0; part < Parts; part++)
		{
			double bc = 0;
			for (var i = 0; i < PerPart; i++) bc += Math.Sqrt(a[part * PerPart + i] * b[part * PerPart + i]);
			sum += bc;
		}
		return sum / Parts;
	}

	public static byte[] ToBytes(float[] d)
	{
		var bytes = new byte[d.Length * 4];
		Buffer.BlockCopy(d, 0, bytes, 0, bytes.Length);
		return bytes;
	}

	public static float[] FromBytes(byte[] bytes)
	{
		var d = new float[bytes.Length / 4];
		Buffer.BlockCopy(bytes, 0, d, 0, d.Length * 4);
		return d;
	}

	static (double H, double S, double V) Hsv(byte r, byte g, byte b)
	{
		double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
		var max = Math.Max(rf, Math.Max(gf, bf));
		var min = Math.Min(rf, Math.Min(gf, bf));
		var delta = max - min;
		var h = delta == 0 ? 0 : max == rf ? 60 * (((gf - bf) / delta + 6) % 6) : max == gf ? 60 * ((bf - rf) / delta + 2) : 60 * ((rf - gf) / delta + 4);
		return (h, max == 0 ? 0 : delta / max, max);
	}
}

public static class Png
{
	public static (int Width, int Height, byte[] Bgr) ToBgr(byte[] png)
	{
		static int Int(byte[] b, int at) => (b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3];
		var width = Int(png, 16);
		var height = Int(png, 20);
		using var idat = new MemoryStream();
		for (var at = 8; at + 8 <= png.Length;)
		{
			var length = Int(png, at);
			if (png[at + 4] == 'I' && png[at + 5] == 'D' && png[at + 6] == 'A' && png[at + 7] == 'T') idat.Write(png, at + 8, length);
			at += 12 + length;
		}
		idat.Position = 0;
		using var z = new ZLibStream(idat, CompressionMode.Decompress);
		using var raw = new MemoryStream();
		z.CopyTo(raw);
		var data = raw.GetBuffer();
		var bgr = new byte[width * height * 3];
		for (var y = 0; y < height; y++)
			for (var x = 0; x < width; x++)
			{
				var s = y * (width * 3 + 1) + 1 + x * 3;
				var t = (y * width + x) * 3;
				bgr[t] = data[s + 2];
				bgr[t + 1] = data[s + 1];
				bgr[t + 2] = data[s];
			}
		return (width, height, bgr);
	}

	static readonly uint[] Table = Enumerable.Range(0, 256).Select(n =>
	{
		var c = (uint)n;
		for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
		return c;
	}).ToArray();

	public static byte[] FromBgr(byte[] bgr, int width, int height)
	{
		var raw = new byte[height * (width * 3 + 1)];
		for (var y = 0; y < height; y++)
		{
			var row = y * (width * 3 + 1);
			for (var x = 0; x < width; x++)
			{
				var s = (y * width + x) * 3;
				var t = row + 1 + x * 3;
				raw[t] = bgr[s + 2];
				raw[t + 1] = bgr[s + 1];
				raw[t + 2] = bgr[s];
			}
		}
		using var zipped = new MemoryStream();
		using (var z = new ZLibStream(zipped, CompressionLevel.Fastest, leaveOpen: true)) z.Write(raw);
		using var png = new MemoryStream();
		png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
		var header = new byte[13];
		BigEndian(header, 0, (uint)width);
		BigEndian(header, 4, (uint)height);
		header[8] = 8;
		header[9] = 2;
		Chunk(png, "IHDR", header);
		Chunk(png, "IDAT", zipped.ToArray());
		Chunk(png, "IEND", []);
		return png.ToArray();
	}

	static void Chunk(Stream s, string type, byte[] data)
	{
		var head = new byte[8];
		BigEndian(head, 0, (uint)data.Length);
		for (var i = 0; i < 4; i++) head[4 + i] = (byte)type[i];
		s.Write(head);
		s.Write(data);
		var crc = 0xFFFFFFFFu;
		foreach (var b in head.AsSpan(4)) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
		foreach (var b in data) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
		var tail = new byte[4];
		BigEndian(tail, 0, crc ^ 0xFFFFFFFFu);
		s.Write(tail);
	}

	static void BigEndian(byte[] b, int at, uint v)
	{
		b[at] = (byte)(v >> 24);
		b[at + 1] = (byte)(v >> 16);
		b[at + 2] = (byte)(v >> 8);
		b[at + 3] = (byte)v;
	}
}

public static class PeopleGrouping
{
	public const double SameLook = 0.70;

	public static float[] Blend(float[] mean, int count, float[] next)
	{
		var blended = new float[mean.Length];
		for (var i = 0; i < mean.Length; i++) blended[i] = (mean[i] * count + next[i]) / (count + 1);
		return blended;
	}

	public static List<List<int>> Cluster(IReadOnlyList<float[]> descriptors, double threshold = SameLook)
	{
		var groups = new List<(float[] Mean, List<int> Members)>();
		for (var i = 0; i < descriptors.Count; i++)
		{
			var best = -1;
			var bestScore = threshold;
			for (var g = 0; g < groups.Count; g++)
			{
				var score = Appearance.Similarity(descriptors[i], groups[g].Mean);
				if (score < bestScore) continue;
				bestScore = score;
				best = g;
			}
			if (best < 0) groups.Add((descriptors[i], [i]));
			else groups[best] = (Blend(groups[best].Mean, groups[best].Members.Count, descriptors[i]), [.. groups[best].Members, i]);
		}
		return groups.Select(g => g.Members).ToList();
	}

	public static long? Match(float[] descriptor, IEnumerable<(long GroupId, float[] Descriptor)> known, double threshold = SameLook)
	{
		long? best = null;
		var bestScore = threshold;
		foreach (var (id, other) in known)
		{
			var score = Appearance.Similarity(descriptor, other);
			if (score < bestScore) continue;
			bestScore = score;
			best = id;
		}
		return best;
	}
}

public static class Yuv
{
	public static void ToBgr(byte[] i420, int width, int height, byte[] bgr)
	{
		var uPlane = width * height;
		var vPlane = uPlane + uPlane / 4;
		var half = width / 2;
		Parallel.For(0, height / 2, pair =>
		{
			for (var dy = 0; dy < 2; dy++)
			{
				var y = pair * 2 + dy;
				var row = y * width;
				var chroma = pair * half;
				for (var x = 0; x < width; x++)
				{
					var c = 298 * (i420[row + x] - 16);
					var d = i420[uPlane + chroma + x / 2] - 128;
					var e = i420[vPlane + chroma + x / 2] - 128;
					var t = (row + x) * 3;
					bgr[t] = Clamp((c + 516 * d + 128) >> 8);
					bgr[t + 1] = Clamp((c - 100 * d - 208 * e + 128) >> 8);
					bgr[t + 2] = Clamp((c + 409 * e + 128) >> 8);
				}
			}
		});
	}

	static byte Clamp(int v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
}
