using System.Diagnostics;
using Velador.Core.Vision;

namespace Velador.Engine.Vision;

public sealed class FrameReader : IDisposable
{
	public const int Width = 1280;
	public const int Height = 720;
	public const int Fps = 10;
	const int FrameBytes = Width * Height * 3 / 2;

	readonly Process _process;
	readonly byte[][] _ring = [new byte[FrameBytes], new byte[FrameBytes], new byte[FrameBytes]];
	readonly byte[] _bgr = new byte[Width * Height * 3];
	readonly Lock _lock = new();
	readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
	readonly SemaphoreSlim _signal = new(0, 1);
	int _latest = -1;
	int _busy = -1;
	long _latestMs;
	long _sequence;

	public long Frames => Interlocked.Read(ref _sequence);
	public Task Ended => _ended.Task;
	public string LastError { get; private set; } = "";

	FrameReader(Process process) => _process = process;

	public static FrameReader Start(string ffmpeg, string url, int rotation, TimeProvider time)
	{
		var filter = $"fps={Fps},scale={Width}:{Height}{(rotation == 180 ? ",hflip,vflip" : "")}";
		var info = new ProcessStartInfo(ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin", "-rtsp_transport", "tcp", "-fflags", "nobuffer", "-flags", "low_delay", "-probesize", "200000", "-analyzeduration", "500000", "-threads", "1", "-max_delay", "0", "-i", url, "-an", "-vf", filter, "-pix_fmt", "yuv420p", "-f", "rawvideo", "pipe:1"])
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

	void Wake()
	{
		try
		{
			if (_signal.CurrentCount == 0) _signal.Release();
		}
		catch (Exception e) when (e is SemaphoreFullException or ObjectDisposedException) { }
	}

	void Pump(TimeProvider time)
	{
		var stream = _process.StandardOutput.BaseStream;
		try
		{
			while (true)
			{
				int slot;
				lock (_lock) slot = Enumerable.Range(0, 3).First(i => i != _busy && i != _latest);
				var buffer = _ring[slot];
				var read = 0;
				while (read < FrameBytes)
				{
					var n = stream.Read(buffer, read, FrameBytes - read);
					if (n == 0) return;
					read += n;
				}
				lock (_lock)
				{
					_latest = slot;
					_latestMs = time.GetUtcNow().ToUnixTimeMilliseconds();
				}
				Interlocked.Increment(ref _sequence);
				Wake();
			}
		}
		catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException) { }
		finally
		{
			_ended.TrySetResult();
			Wake();
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
					return new FrameLease(this, _ring[_busy], _latestMs);
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

	public sealed class FrameLease(FrameReader owner, byte[] yuv, long capturedMs) : IDisposable
	{
		BgrFrame? _frame;

		public long CapturedMs { get; } = capturedMs;

		public BgrFrame Frame => _frame ??= Convert();

		BgrFrame Convert()
		{
			Yuv.ToBgr(yuv, Width, Height, owner._bgr);
			return new BgrFrame(owner._bgr, Width, Height, CapturedMs);
		}

		public void Dispose() => owner.Release();
	}
}
