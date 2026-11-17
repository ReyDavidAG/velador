using System.Text;
using Velador.Core;

namespace Velador.Engine.MediaMtx;

public static class MediaMtxConfig
{
	public const string PlaceholderSource = "rtsp://127.0.0.1:1/none";

	public static string Build(MediaPorts ports, string recordingsDir, IEnumerable<Camera> cameras, IReadOnlySet<string>? paused = null)
	{
		var dir = recordingsDir.Replace('\\', '/').Replace("'", "''");
		var yaml = new StringBuilder();
		Line(yaml, "logLevel: info");
		Line(yaml, "logDestinations: [stdout]");
		Line(yaml, "api: yes");
		Line(yaml, $"apiAddress: 127.0.0.1:{ports.Api}");
		foreach (var off in new[] { "metrics", "pprof", "rtmp", "hls", "srt", "moq" }) Line(yaml, $"{off}: no");
		Line(yaml, "rtsp: yes");
		Line(yaml, $"rtspAddress: 127.0.0.1:{ports.Rtsp}");
		Line(yaml, "rtspTransports: [tcp]");
		Line(yaml, "rtspEncryption: \"no\"");
		Line(yaml, "webrtc: yes");
		Line(yaml, $"webrtcAddress: 127.0.0.1:{ports.WebRtc}");
		Line(yaml, $"webrtcLocalUDPAddress: 127.0.0.1:{ports.WebRtcUdp}");
		Line(yaml, $"webrtcLocalTCPAddress: 127.0.0.1:{ports.WebRtcUdp}");
		Line(yaml, "webrtcIPsFromInterfaces: no");
		Line(yaml, "webrtcAdditionalHosts: [127.0.0.1]");
		Line(yaml, "webrtcAllowOrigins: []");
		Line(yaml, "playback: yes");
		Line(yaml, $"playbackAddress: 127.0.0.1:{ports.Playback}");
		Line(yaml, "playbackAllowOrigins: []");
		Line(yaml, "pathDefaults:");
		Line(yaml, "  rtspTransport: tcp");
		Line(yaml, "  record: yes");
		Line(yaml, "  recordFormat: fmp4");
		Line(yaml, "  recordPartDuration: 1s");
		Line(yaml, "  recordSegmentDuration: 1h");
		Line(yaml, "  recordDeleteAfter: 0s");
		Line(yaml, $"  recordPath: '{dir}/%path/%Y-%m-%d_%H-%M-%S-%f%z'");
		Line(yaml, "paths:");
		foreach (var camera in cameras)
			foreach (var stream in new[] { Camera.MainStream, Camera.MinorStream })
			{
				Line(yaml, $"  {camera.MediaPath(stream)}:");
				Line(yaml, $"    source: {PlaceholderSource}");
				if (paused?.Contains(camera.Id) == true) Line(yaml, "    record: no");
			}
		return yaml.ToString();
	}

	static void Line(StringBuilder yaml, string text) => yaml.Append(text).Append('\n');

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
