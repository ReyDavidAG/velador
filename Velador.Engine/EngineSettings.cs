using System.Text.Json;
using Velador.Core;

namespace Velador.Engine;

public sealed class EngineSettings
{
	public int Port { get; set; } = 8352;
	public string? RecordingsDir { get; set; }
	public int RetentionDays { get; set; } = 14;
	public int RetentionPercent { get; set; } = 80;
	public bool KeepAwake { get; set; } = true;
	public List<Camera> Cameras { get; set; } = [];
	public Dictionary<string, List<KnownNetwork>> Networks { get; set; } = [];
	public Dictionary<string, long> Paused { get; set; } = [];

	[System.Text.Json.Serialization.JsonIgnore]
	public int Revision { get; set; }

	static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

	public static EngineSettings Load(EnginePaths paths)
	{
		if (!File.Exists(paths.Settings)) return new EngineSettings();
		var settings = JsonSerializer.Deserialize<EngineSettings>(File.ReadAllText(paths.Settings), Json) ?? new EngineSettings();
		settings.Cameras = settings.Cameras.Where(c => Camera.IsValidId(c.Id)).ToList();
		return settings;
	}

	public void Save(EnginePaths paths) => File.WriteAllText(paths.Settings, JsonSerializer.Serialize(this, Json));
}

public sealed class EnginePaths
{
	public EnginePaths(string home)
	{
		Home = home;
		Directory.CreateDirectory(home);
	}

	public string Home { get; }
	public string Settings => Path.Combine(Home, "config.json");
	public string Token => Path.Combine(Home, "token");
	public string Database => Path.Combine(Home, "velador.db");
	public string MediaConfig => Path.Combine(Home, "mediamtx.yml");
	public string MediaPid => Path.Combine(Home, "mediamtx.pid");
	public string Handover => Path.Combine(Home, "handover");

	public string MediaBinary
	{
		get
		{
			var name = OperatingSystem.IsWindows() ? "mediamtx.exe" : "mediamtx";
			var beside = Path.Combine(AppContext.BaseDirectory, name);
			return Environment.GetEnvironmentVariable("VELADOR_MEDIAMTX") ?? (File.Exists(beside) ? beside : Path.Combine(Home, "bin", name));
		}
	}

	public static string? WebRoot()
	{
		var beside = Path.Combine(AppContext.BaseDirectory, "web");
		return Environment.GetEnvironmentVariable("VELADOR_WEB") ?? (Directory.Exists(beside) ? beside : null);
	}

	public static string DefaultRecordings => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Velador");

	public static EnginePaths Resolve() =>
		new(Environment.GetEnvironmentVariable("VELADOR_HOME")
			?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Velador"));
}

public sealed record MediaPorts(int Api, int WebRtc, int WebRtcUdp, int Playback)
{
	public static MediaPorts From(int enginePort) => new(enginePort + 1, enginePort + 2, enginePort + 3, enginePort + 4);
}
