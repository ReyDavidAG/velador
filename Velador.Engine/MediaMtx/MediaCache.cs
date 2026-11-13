using System.Collections.Concurrent;

namespace Velador.Engine.MediaMtx;

public sealed class MediaCache
{
	const int MaxFiles = 64;

	readonly string _dir;
	readonly MediaMtxClient _media;
	readonly ConcurrentDictionary<string, Lazy<Task<string?>>> _pending = new();

	public MediaCache(EnginePaths paths, MediaMtxClient media)
	{
		_media = media;
		_dir = Path.Combine(paths.Home, "cache", "media");
		if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
		Directory.CreateDirectory(_dir);
	}

	public Task<string?> FileAsync(string path, long startMs, long durationMs, CancellationToken ct)
	{
		var key = $"{path}_{startMs}_{durationMs}.mp4";
		var file = Path.Combine(_dir, key);
		if (File.Exists(file))
		{
			File.SetLastAccessTimeUtc(file, DateTime.UtcNow);
			return Task.FromResult<string?>(file);
		}
		var fetch = _pending.GetOrAdd(key, _ => new Lazy<Task<string?>>(() => FetchAsync(path, startMs, durationMs, file, ct)));
		return Finish(key, fetch.Value);
	}

	async Task<string?> Finish(string key, Task<string?> task)
	{
		try
		{
			return await task;
		}
		finally
		{
			_pending.TryRemove(key, out _);
		}
	}

	async Task<string?> FetchAsync(string path, long startMs, long durationMs, string file, CancellationToken ct)
	{
		var partial = $"{file}.{Guid.NewGuid():N}.part";
		try
		{
			using var upstream = await _media.MediaAsync(path, startMs, durationMs / 1000.0, ct);
			if (!upstream.IsSuccessStatusCode) return null;
			await using (var target = File.Create(partial)) await upstream.Content.CopyToAsync(target, ct);
			File.Move(partial, file, true);
			Trim();
			return file;
		}
		catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException)
		{
			File.Delete(partial);
			return null;
		}
	}

	void Trim()
	{
		var files = new DirectoryInfo(_dir).GetFiles("*.mp4").OrderByDescending(f => f.LastAccessTimeUtc).Skip(MaxFiles);
		foreach (var old in files)
		{
			try
			{
				old.Delete();
			}
			catch (IOException) { }
		}
	}
}
