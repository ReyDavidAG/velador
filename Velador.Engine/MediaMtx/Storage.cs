namespace Velador.Engine.MediaMtx;

public sealed record VolumeInfo(string Name, string Path, long TotalBytes, long FreeBytes);

public sealed class Storage(EngineSettings settings, string fallback, TimeProvider time)
{
	long _releasedUntilMs;
	string? _active;

	public string Fallback => fallback;
	public string Primary => string.IsNullOrWhiteSpace(settings.RecordingsDir) ? fallback : settings.RecordingsDir;
	public bool UsesExternal => !SamePath(Primary, fallback);
	public bool Released => time.GetUtcNow().ToUnixTimeMilliseconds() < _releasedUntilMs;
	public bool PrimaryAvailable => Reachable(Primary);
	public string Desired => !Released && PrimaryAvailable ? Primary : fallback;
	public string Active => _active ?? fallback;
	public bool OnFallback => UsesExternal && SamePath(Active, fallback);

	public string Activate()
	{
		_active = !Released && Available(Primary) ? Primary : fallback;
		Directory.CreateDirectory(_active);
		return _active;
	}

	public static bool Reachable(string dir)
	{
		try
		{
			var full = Path.GetFullPath(dir);
			return MountPoint(full) is not { } mount || Directory.Exists(mount);
		}
		catch (Exception e) when (e is IOException or ArgumentException or NotSupportedException)
		{
			return false;
		}
	}

	public void Release(TimeSpan span) => _releasedUntilMs = time.GetUtcNow().Add(span).ToUnixTimeMilliseconds();

	public void Reclaim() => _releasedUntilMs = 0;

	public int Relocate()
	{
		if (!UsesExternal || !SamePath(Active, Primary) || !Directory.Exists(fallback)) return 0;
		var moved = 0;
		foreach (var file in Directory.EnumerateFiles(fallback, "*.mp4", SearchOption.AllDirectories))
		{
			var target = Path.Combine(Primary, Path.GetRelativePath(fallback, file));
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(target)!);
				if (File.Exists(target)) continue;
				File.Copy(file, target + ".part", true);
				File.Move(target + ".part", target);
				File.Delete(file);
				moved++;
			}
			catch (Exception e) when (e is IOException or UnauthorizedAccessException)
			{
				break;
			}
		}
		return moved;
	}

	public static bool Available(string dir)
	{
		try
		{
			var full = Path.GetFullPath(dir);
			if (MountPoint(full) is { } mount && !Directory.Exists(mount)) return false;
			Directory.CreateDirectory(full);
			var probe = Path.Combine(full, $".velador-{Guid.NewGuid():N}");
			File.WriteAllBytes(probe, []);
			File.Delete(probe);
			return true;
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
		{
			return false;
		}
	}

	public static string? MountPoint(string fullPath)
	{
		if (OperatingSystem.IsWindows())
		{
			var root = Path.GetPathRoot(fullPath);
			return string.IsNullOrEmpty(root) ? null : root;
		}
		var parts = fullPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
		return parts is ["Volumes", var name, ..] ? $"/Volumes/{name}" : null;
	}

	public static IReadOnlyList<VolumeInfo> Volumes()
	{
		var list = new List<VolumeInfo>();
		foreach (var drive in DriveInfo.GetDrives())
		{
			try
			{
				if (!drive.IsReady || drive.TotalSize < 1L << 30) continue;
				var path = drive.RootDirectory.FullName;
				var external = OperatingSystem.IsWindows() ? drive.DriveType is DriveType.Removable or DriveType.Fixed && !path.StartsWith(Path.GetPathRoot(Environment.SystemDirectory)!, StringComparison.OrdinalIgnoreCase) : path.StartsWith("/Volumes/", StringComparison.Ordinal);
				if (external) list.Add(new VolumeInfo(OperatingSystem.IsWindows() ? path : Path.GetFileName(path.TrimEnd('/')), path, drive.TotalSize, drive.AvailableFreeSpace));
			}
			catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
		}
		return list;
	}

	static bool SamePath(string a, string b) => string.Equals(Path.GetFullPath(a).TrimEnd('/', '\\'), Path.GetFullPath(b).TrimEnd('/', '\\'), StringComparison.Ordinal);
}
