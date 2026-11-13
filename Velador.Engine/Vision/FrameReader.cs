using System.Diagnostics;
using Velador.Core.Vision;

namespace Velador.Engine.Vision;

public sealed class FrameReader : IDisposable
{
	public const int Width = 960;
	public const int Height = 540;
	public const int Fps = 10;

	readonly Process _process;
	readonly byte[][] _ring = [new byte[Width * Height * 3], new byte[Width * Height * 3], new byte[Width * Height * 3]];
	readonly Lock _lock = new();
	readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
	int _latest = -1;
	int _busy = -1;
	long _latestMs;
	long _sequence;
	SemaphoreSlim _signal = new(0, 1);

	public long Frames => Interlocked.Read(ref _sequence);
	public Task Ended => _ended.Task;
	public string LastError { get; private set; } = "";

	FrameReader(Process process) => _process = process;

	public static FrameReader Start(string ffmpeg, string url, int rotation, TimeProvider time)
	{
		var filter = $"fps={Fps},scale={Width}:{Height}{(rotation == 180 ? ",hflip,vflip" : "")}";
		var info = new ProcessStartInfo(ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin", "-rtsp_transport", "tcp", "-fflags", "nobuffer", "-flags", "low_delay", "-probesize", "200000", "-analyzeduration", "500000", "-i", url, "-an", "-vf", filter, "-pix_fmt", "bgr24", "-f", "rawvideo", "pipe:1"])
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			RedirectStandardInput = false,
		};
		var process = Process.Start(info) ?? throw new InvalidOperationException("ffmpeg did not start");
		var reader = new FrameReader(process);
		process.ErrorDataReceived += (_, e) =>
		{
			if (!string.IsNullOrWhiteSpace(e.Data)) reader.LastError = e.Data.Length > 300 ? e.Data[..300] : e.Data;
		};
		process.BeginErrorReadLine();
		new Thread(() => reader.Pump(time)) { IsBackground = true, Name = "velador-frames" }.Start();
		return reader;
	}

	void Pump(TimeProvider time)
	{
		var stream = _process.StandardOutput.BaseStream;
		var size = Width * Height * 3;
		try
		{
			while (true)
			{
				int slot;
				lock (_lock) slot = Enumerable.Range(0, 3).First(i => i != _busy && i != _latest);
				var buffer = _ring[slot];
				var read = 0;
				while (read < size)
				{
					var n = stream.Read(buffer, read, size - read);
					if (n == 0) return;
					read += n;
				}
				lock (_lock)
				{
					_latest = slot;
					_latestMs = time.GetUtcNow().ToUnixTimeMilliseconds();
				}
				Interlocked.Increment(ref _sequence);
				if (_signal.CurrentCount == 0) _signal.Release();
			}
		}
		catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException) { }
		finally
		{
			_ended.TrySetResult();
			try
			{
				if (_signal.CurrentCount == 0) _signal.Release();
			}
			catch (ObjectDisposedException) { }
		}
	}

	public async Task<FrameLease?> NextAsync(CancellationToken ct)
	{
		while (!ct.IsCancellationRequested)
		{
			lock (_lock)
			{
				if (_latest >= 0)
				{
					_busy = _latest;
					_latest = -1;
					return new FrameLease(this, new BgrFrame(_ring[_busy], Width, Height, _latestMs));
				}
			}
			if (_ended.Task.IsCompleted) return null;
			await _signal.WaitAsync(TimeSpan.FromSeconds(5), ct);
		}
		return null;
	}

	internal void Release()
	{
		lock (_lock) _busy = -1;
	}

	public void Dispose()
	{
		try
		{
			if (!_process.HasExited) _process.Kill();
		}
		catch (InvalidOperationException) { }
		_process.Dispose();
	}

	public sealed class FrameLease(FrameReader owner, BgrFrame frame) : IDisposable
	{
		public BgrFrame Frame { get; } = frame;
		public void Dispose() => owner.Release();
	}
}
