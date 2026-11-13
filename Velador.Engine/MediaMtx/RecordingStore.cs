using System.Globalization;
using Velador.Core;

namespace Velador.Engine.MediaMtx;

public interface IRecordingStore
{
	IReadOnlyList<StoredSegment> Segments();
	(long TotalBytes, long FreeBytes) Disk();
	void Delete(StoredSegment segment);
}

public sealed class RecordingStore(string root) : IRecordingStore
{
	public const string NameFormat = "yyyy-MM-dd_HH-mm-ss-ffffffzzz";

	public IReadOnlyList<StoredSegment> Segments()
	{
		if (!Directory.Exists(root)) return [];
		var segments = new List<StoredSegment>();
		foreach (var file in new DirectoryInfo(root).EnumerateFiles("*.mp4", SearchOption.AllDirectories))
			if (StartOf(file.Name) is { } start)
				segments.Add(new StoredSegment(file.FullName, file.Directory!.Name, start, file.Length));
		return segments;
	}

	public (long TotalBytes, long FreeBytes) Disk()
	{
		var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!);
		return (drive.TotalSize, drive.AvailableFreeSpace);
	}

	public void Delete(StoredSegment segment)
	{
		if (Path.GetFullPath(segment.File).StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal)) File.Delete(segment.File);
	}

	public static long? StartOf(string fileName)
	{
		var stem = Path.GetFileNameWithoutExtension(fileName);
		if (stem.Length < 31) return null;
		var zone = stem[^5..];
		var text = $"{stem[..^5]}{zone[..3]}:{zone[3..]}";
		return DateTimeOffset.TryParseExact(text, NameFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ? start.ToUnixTimeMilliseconds() : null;
	}
}

public sealed class RetentionService(IRecordingStore store, EngineSettings settings, TimeProvider time, ILogger<RetentionService> log) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken ct)
	{
		using var timer = new PeriodicTimer(TimeSpan.FromMinutes(10), time);
		try
		{
			do
			{
				try
				{
					Sweep();
				}
				catch (Exception e) when (e is IOException or UnauthorizedAccessException)
				{
					log.LogError(e, "Retention sweep failed");
				}
			} while (await timer.WaitForNextTickAsync(ct));
		}
		catch (OperationCanceledException) { }
	}

	public int Sweep()
	{
		var segments = store.Segments();
		if (segments.Count == 0) return 0;
		var (total, free) = store.Disk();
		var doomed = Retention.Plan(segments, time.GetUtcNow().ToUnixTimeMilliseconds(), total, free, new RetentionLimits(settings.RetentionDays, settings.RetentionPercent));
		foreach (var segment in doomed) store.Delete(segment);
		if (doomed.Count > 0) log.LogInformation("Retention deleted {Count} segments, {Bytes} bytes", doomed.Count, doomed.Sum(d => d.Bytes));
		return doomed.Count;
	}
}
