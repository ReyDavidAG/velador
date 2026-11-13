using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Velador.Core.Vision;

namespace Velador.Engine.Vision;

public sealed class FaceDetector : IDisposable
{
	const int Size = 640;
	static readonly int[] Strides = [8, 16, 32];

	readonly InferenceSession _session;
	readonly Lock _lock = new();
	readonly DenseTensor<float> _tensor = new([1, 3, Size, Size]);

	public FaceDetector(string modelFile)
	{
		if (OperatingSystem.IsWindows())
		{
			try
			{
				var gpu = new Microsoft.ML.OnnxRuntime.SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL, EnableMemoryPattern = false, ExecutionMode = ExecutionMode.ORT_SEQUENTIAL };
				gpu.AppendExecutionProvider_DML(0);
				_session = new InferenceSession(modelFile, gpu);
				return;
			}
			catch (Exception e) when (e is OnnxRuntimeException or EntryPointNotFoundException or DllNotFoundException) { }
		}
		var cpu = new Microsoft.ML.OnnxRuntime.SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL, IntraOpNumThreads = 1, InterOpNumThreads = 1 };
		cpu.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
		_session = new InferenceSession(modelFile, cpu);
	}

	public Detection? Best(byte[] bgr, int width, int height, double minScore = 0.6)
	{
		lock (_lock)
		{
			var scale = Math.Min((double)Size / width, (double)Size / height);
			var span = _tensor.Buffer.Span;
			span.Clear();
			var plane = Size * Size;
			var w = (int)(width * scale);
			var h = (int)(height * scale);
			for (var y = 0; y < h; y++)
			{
				var row = Math.Min(height - 1, (int)(y / scale)) * width;
				for (var x = 0; x < w; x++)
				{
					var i = (row + Math.Min(width - 1, (int)(x / scale))) * 3;
					var t = y * Size + x;
					span[t] = bgr[i];
					span[plane + t] = bgr[i + 1];
					span[2 * plane + t] = bgr[i + 2];
				}
			}
			using var results = _session.Run([NamedOnnxValue.CreateFromTensor("input", _tensor)]);
			var outputs = results.ToDictionary(r => r.Name, r => r.AsTensor<float>());
			Detection? best = null;
			foreach (var stride in Strides)
			{
				var cls = outputs[$"cls_{stride}"];
				var obj = outputs[$"obj_{stride}"];
				var box = outputs[$"bbox_{stride}"];
				var cols = Size / stride;
				for (var i = 0; i < cols * cols; i++)
				{
					var score = Math.Sqrt(Math.Clamp(cls[0, i, 0], 0, 1) * Math.Clamp(obj[0, i, 0], 0, 1));
					if (score < minScore || score <= (best?.Score ?? 0)) continue;
					var cx = (i % cols + box[0, i, 0]) * stride / scale;
					var cy = (i / cols + box[0, i, 1]) * stride / scale;
					var bw = Math.Exp(box[0, i, 2]) * stride / scale;
					var bh = Math.Exp(box[0, i, 3]) * stride / scale;
					if (cx >= width || cy >= height) continue;
					best = new Detection(Box.FromCenter(cx / width, cy / height, bw / width, bh / height), score);
				}
			}
			return best;
		}
	}

	public void Dispose() => _session.Dispose();
}
