using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Velador.Core.Vision;

namespace Velador.Engine.Vision;

public sealed class PersonDetector : IDisposable
{
	const int Size = 416;
	static readonly int[] Strides = [8, 16, 32];

	readonly InferenceSession _session;
	readonly string _input;
	readonly Lock _lock = new();
	readonly DenseTensor<float> _tensor = new([1, 3, Size, Size]);

	public string Device { get; }

	public PersonDetector(string modelFile)
	{
		if (OperatingSystem.IsWindows())
		{
			try
			{
				var gpu = new Microsoft.ML.OnnxRuntime.SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL, EnableMemoryPattern = false, ExecutionMode = ExecutionMode.ORT_SEQUENTIAL };
				gpu.AppendExecutionProvider_DML(0);
				_session = new InferenceSession(modelFile, gpu);
				_input = _session.InputMetadata.Keys.First();
				Device = "gpu";
				return;
			}
			catch (Exception e) when (e is OnnxRuntimeException or EntryPointNotFoundException or DllNotFoundException) { }
		}
		Device = "cpu";
		var options = new Microsoft.ML.OnnxRuntime.SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL, IntraOpNumThreads = 2, InterOpNumThreads = 1 };
		options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
		options.AddSessionConfigEntry("session.inter_op.allow_spinning", "0");
		_session = new InferenceSession(modelFile, options);
		_input = _session.InputMetadata.Keys.First();
	}

	public IReadOnlyList<Detection> Detect(BgrFrame frame, double minScore = 0.15)
	{
		lock (_lock)
		{
			var scale = Math.Min((double)Size / frame.Width, (double)Size / frame.Height);
			Letterbox(frame, scale);
			using var results = _session.Run([NamedOnnxValue.CreateFromTensor(_input, _tensor)]);
			var output = results[0].AsTensor<float>();
			return Suppress(Decode(output, scale, frame.Width, frame.Height, minScore));
		}
	}

	void Letterbox(BgrFrame frame, double scale)
	{
		var buffer = _tensor.Buffer.Span;
		buffer.Fill(114f);
		var width = (int)(frame.Width * scale);
		var height = (int)(frame.Height * scale);
		var plane = Size * Size;
		var src = frame.Pixels;
		for (var y = 0; y < height; y++)
		{
			var sy = Math.Min(frame.Height - 1, (int)(y / scale));
			var row = sy * frame.Width;
			for (var x = 0; x < width; x++)
			{
				var i = (row + Math.Min(frame.Width - 1, (int)(x / scale))) * 3;
				var t = y * Size + x;
				buffer[t] = src[i];
				buffer[plane + t] = src[i + 1];
				buffer[2 * plane + t] = src[i + 2];
			}
		}
	}

	static List<Detection> Decode(Tensor<float> output, double scale, int width, int height, double minScore)
	{
		var found = new List<Detection>();
		var index = 0;
		foreach (var stride in Strides)
		{
			var cells = Size / stride;
			for (var gy = 0; gy < cells; gy++)
				for (var gx = 0; gx < cells; gx++, index++)
				{
					var score = output[0, index, 4] * output[0, index, 5];
					if (score < minScore) continue;
					var cx = (output[0, index, 0] + gx) * stride / scale;
					var cy = (output[0, index, 1] + gy) * stride / scale;
					var w = Math.Exp(output[0, index, 2]) * stride / scale;
					var h = Math.Exp(output[0, index, 3]) * stride / scale;
					found.Add(new Detection(Box.FromCenter(cx / width, cy / height, w / width, h / height), score));
				}
		}
		return found;
	}

	static List<Detection> Suppress(List<Detection> found, double iou = 0.45)
	{
		var kept = new List<Detection>();
		foreach (var d in found.OrderByDescending(d => d.Score))
			if (kept.All(k => k.Box.IoU(d.Box) < iou)) kept.Add(d);
		return kept;
	}

	public void Dispose()
	{
		_session.Dispose();
	}
}
