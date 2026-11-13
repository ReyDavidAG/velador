using System.Text;
using Velador.Core;

namespace Velador.Engine.MediaMtx;

public static class MediaMtxConfig
{
	public const string PlaceholderSource = "rtsp://127.0.0.1:1/none";

	public static string Build(MediaPorts ports, string recordingsDir, IEnumerable<Camera> cameras)
	{
		var dir = recordingsDir.Replace('\\', '/').Replace("'", "''");
		var yaml = new StringBuilder();
		yaml.AppendLine("logLevel: info");
		yaml.AppendLine("logDestinations: [stdout]");
		yaml.AppendLine("api: yes");
		yaml.AppendLine($"apiAddress: 127.0.0.1:{ports.Api}");
		foreach (var off in new[] { "metrics", "pprof", "rtsp", "rtmp", "hls", "srt", "moq" }) yaml.AppendLine($"{off}: no");
		yaml.AppendLine("webrtc: yes");
		yaml.AppendLine($"webrtcAddress: 127.0.0.1:{ports.WebRtc}");
		yaml.AppendLine($"webrtcLocalUDPAddress: 127.0.0.1:{ports.WebRtcUdp}");
		yaml.AppendLine("webrtcIPsFromInterfaces: no");
		yaml.AppendLine("webrtcAdditionalHosts: [127.0.0.1]");
		yaml.AppendLine("webrtcAllowOrigins: []");
		yaml.AppendLine("playback: yes");
		yaml.AppendLine($"playbackAddress: 127.0.0.1:{ports.Playback}");
		yaml.AppendLine("playbackAllowOrigins: []");
		yaml.AppendLine("pathDefaults:");
		yaml.AppendLine("  rtspTransport: tcp");
		yaml.AppendLine("  record: yes");
		yaml.AppendLine("  recordFormat: fmp4");
		yaml.AppendLine("  recordPartDuration: 1s");
		yaml.AppendLine("  recordSegmentDuration: 1h");
		yaml.AppendLine("  recordDeleteAfter: 0s");
		yaml.AppendLine($"  recordPath: '{dir}/%path/%Y-%m-%d_%H-%M-%S-%f%z'");
		yaml.AppendLine("paths:");
		foreach (var camera in cameras)
			foreach (var stream in new[] { Camera.MainStream, Camera.MinorStream })
			{
				yaml.AppendLine($"  {camera.MediaPath(stream)}:");
				yaml.AppendLine($"    source: {PlaceholderSource}");
			}
		return yaml.ToString();
	}

	public static Dictionary<string, string> SourceEnvironment(IEnumerable<Camera> cameras, Func<Camera, string?> password)
	{
		var env = new Dictionary<string, string>();
		foreach (var camera in cameras)
		{
			if (password(camera) is not { Length: > 0 } secret) continue;
			var auth = $"{Uri.EscapeDataString(camera.User)}:{Uri.EscapeDataString(secret)}";
			env[$"MTX_PATHS_{camera.MediaPath(Camera.MainStream).ToUpperInvariant()}_SOURCE"] = $"rtsp://{auth}@{camera.Host}:{camera.RtspPort}/{camera.MainPath}";
			env[$"MTX_PATHS_{camera.MediaPath(Camera.MinorStream).ToUpperInvariant()}_SOURCE"] = $"rtsp://{auth}@{camera.Host}:{camera.RtspPort}/{camera.MinorPath}";
		}
		return env;
	}
}
