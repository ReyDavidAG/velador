using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Velador.Core;
using Velador.Core.Vision;
using Velador.Engine.MediaMtx;

namespace Velador.Engine.Vision;

public sealed record EnhancedFace(byte[] Bgr, int Width, int Height, FusedFace? Fused, double EyePixels, int Frames, double Quality);

public sealed partial class FaceEnhancer(MediaMtxClient media)
{
	const double BeforeS = 0.9;
	const double LengthS = 1.7;
	const int MaxFused = 8;

	readonly ConcurrentDictionary<string, (int Width, int Height)> _sizes = new();
	readonly SemaphoreSlim _one = new(1, 1);

	public async Task<EnhancedFace?> RunAsync(Camera camera, FaceDetector faces, long atMs, Box region, double expectedWidth, CancellationToken ct)
	{
		if (EnginePaths.Ffmpeg() is not { } ffmpeg) return null;
		var file = Path.Combine(Path.GetTempPath(), $"velador-face-{Guid.NewGuid():N}.mp4");
		await _one.WaitAsync(ct);
		try
		{
			using (var response = await media.MediaAsync(camera.MediaPath(Camera.MainStream), atMs - (long)(BeforeS * 1000), LengthS, ct))
			{
				if (!response.IsSuccessStatusCode) return null;
				await using var target = File.Create(file);
				await response.Content.CopyToAsync(target, ct);
			}
			if (!_sizes.TryGetValue(camera.Id, out var size))
			{
				var probe = await RunAsync(ffmpeg, ["-hide_banner", "-i", file], false, ct);
				var match = SizePattern().Match(probe.Errors);
				if (!match.Success) return null;
				size = (int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture));
				_sizes[camera.Id] = size;
			}
			var w = Even(Math.Clamp(region.W, 0.05, 1) * size.Width);
			var h = Even(Math.Clamp(region.H, 0.05, 1) * size.Height);
			var x = Even(Math.Clamp(region.X * size.Width, 0, size.Width - w));
			var y = Even(Math.Clamp(region.Y * size.Height, 0, size.Height - h));
			var filter = $"{(camera.Rotation == 180 ? "hflip,vflip," : "")}crop={w}:{h}:{x}:{y}";
			var decoded = await RunAsync(ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin", "-i", file, "-an", "-vf", filter, "-pix_fmt", "bgr24", "-f", "rawvideo", "pipe:1"], true, ct);
			var frameBytes = w * h * 3;
			var found = new List<(FaceShot Shot, double Quality, double Eyes)>();
			for (var at = 0; at + frameBytes <= decoded.Output.Length; at += frameBytes)
			{
				var frame = decoded.Output.AsSpan(at, frameBytes).ToArray();
				if (faces.Find(frame, w, h, 0.8) is not { } face) continue;
				var faceW = face.Box.W * w;
				if (!FaceQuality.Plausible(face.Points.Select(p => new FacePoint(p.X * w, p.Y * h)).ToList(), faceW)) continue;
				var faceH = face.Box.H * h;
				var expected = expectedWidth * size.Width;
				if (faceW < 16 || faceW < expected * 0.6 || faceW > expected * 2.2) continue;
				var side = (int)Math.Min(Math.Max(faceW, faceH) * 1.8, Math.Min(w, h));
				var cx = (int)Math.Clamp(face.Box.Cx * w - side / 2.0, 0, w - side);
				var cy = (int)Math.Clamp(face.Box.Cy * h - side / 2.0, 0, h - side);
				var crop = new BgrFrame(frame, w, h, 0).Crop(cx, cy, side, side);
				var points = face.Points.Select(p => new FacePoint(p.X * w - cx, p.Y * h - cy)).ToArray();
				var sharp = FaceQuality.Sharpness(crop, side, side);
				var quality = FaceQuality.Rank(face.Score, faceW, points, sharp);
				found.Add((new FaceShot(crop, side, side, points, face.Score), quality, FaceQuality.EyeDistance(points)));
			}
			if (found.Count == 0) return null;
			var ordered = found.OrderByDescending(f => f.Quality).ToList();
			var best = ordered[0];
			var alike = ordered.Where(f => f.Shot.Width >= best.Shot.Width * 0.75 && f.Shot.Width <= best.Shot.Width * 1.33).Take(MaxFused).Select(f => f.Shot).ToList();
			return new EnhancedFace(best.Shot.Bgr, best.Shot.Width, best.Shot.Height, FaceFusion.Fuse(alike), best.Eyes, found.Count, best.Quality);
		}
		finally
		{
			_one.Release();
			try
			{
				File.Delete(file);
			}
			catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
		}
	}

	static int Even(double value) => Math.Max(2, (int)value / 2 * 2);

	static async Task<(byte[] Output, string Errors)> RunAsync(string ffmpeg, string[] arguments, bool readOutput, CancellationToken ct)
	{
		var info = new ProcessStartInfo(ffmpeg, arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
		using var process = Process.Start(info) ?? throw new InvalidOperationException("ffmpeg did not start");
		using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
		limit.CancelAfter(TimeSpan.FromSeconds(20));
		try
		{
			var errors = process.StandardError.ReadToEndAsync(limit.Token);
			using var output = new MemoryStream();
			if (readOutput) await process.StandardOutput.BaseStream.CopyToAsync(output, limit.Token);
			await process.WaitForExitAsync(limit.Token);
			return (output.ToArray(), await errors);
		}
		catch (OperationCanceledException)
		{
			try
			{
				if (!process.HasExited) process.Kill();
			}
			catch (InvalidOperationException) { }
			throw;
		}
	}

	[GeneratedRegex(@"Video:[^\n]*?\b(\d{3,5})x(\d{3,5})\b")]
	private static partial Regex SizePattern();
}
