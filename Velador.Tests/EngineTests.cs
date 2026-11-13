using Velador.Core;
using Velador.Engine;
using Velador.Engine.Api;
using Velador.Engine.MediaMtx;
using Velador.Engine.Sqlite;

namespace Velador.Tests;

public class MediaMtxConfigTests
{
	static readonly Camera Entrada = new("entrada", "Entrada", "10.0.0.9", "user@example.com");

	[Fact]
	public void Everything_listens_on_loopback_and_the_open_protocols_are_off()
	{
		var yaml = MediaMtxConfig.Build(MediaPorts.From(8352), "/data/rec", [Entrada]);

		Assert.Contains("apiAddress: 127.0.0.1:8353", yaml);
		Assert.Contains("webrtcAddress: 127.0.0.1:8354", yaml);
		Assert.Contains("webrtcLocalUDPAddress: 127.0.0.1:8355", yaml);
		Assert.Contains("playbackAddress: 127.0.0.1:8356", yaml);
		foreach (var off in new[] { "moq", "rtsp", "rtmp", "hls", "srt", "metrics", "pprof" }) Assert.Contains($"\n{off}: no\n", yaml);
		Assert.DoesNotContain("Address: :", yaml);
	}

	[Fact]
	public void The_file_never_holds_the_camera_address_or_password()
	{
		var yaml = MediaMtxConfig.Build(MediaPorts.From(8352), "/data/rec", [Entrada]);
		var env = MediaMtxConfig.SourceEnvironment([Entrada], _ => "p@ss/word.");

		Assert.DoesNotContain("10.0.0.9", yaml);
		Assert.DoesNotContain("example.com", yaml);
		Assert.Equal("rtsp://user%40example.com:p%40ss%2Fword.@10.0.0.9:554/stream1", env["MTX_PATHS_ENTRADAMAIN_SOURCE"]);
		Assert.Equal("rtsp://user%40example.com:p%40ss%2Fword.@10.0.0.9:554/stream2", env["MTX_PATHS_ENTRADAMINOR_SOURCE"]);
	}

	[Fact]
	public void Recordings_keep_one_second_parts_and_are_never_deleted_by_the_media_server()
	{
		var yaml = MediaMtxConfig.Build(MediaPorts.From(8352), @"C:\Videos\Velador", [Entrada]);

		Assert.Contains("recordPartDuration: 1s", yaml);
		Assert.Contains("recordDeleteAfter: 0s", yaml);
		Assert.Contains("recordPath: 'C:/Videos/Velador/%path/%Y-%m-%d_%H-%M-%S-%f%z'", yaml);
	}

	[Theory]
	[InlineData("entrada", true)]
	[InlineData("cam2", true)]
	[InlineData("", false)]
	[InlineData("mi_camara", false)]
	[InlineData("Entrada", false)]
	[InlineData("a/b", false)]
	public void Camera_ids_are_safe_for_paths_and_environment_names(string id, bool valid)
	{
		Assert.Equal(valid, Camera.IsValidId(id));
	}
}

public class SessionTests : IDisposable
{
	readonly string _home = Directory.CreateTempSubdirectory("velador-test").FullName;

	public void Dispose() => Directory.Delete(_home, true);

	[Fact]
	public void The_token_is_created_once_and_only_the_owner_reads_it()
	{
		var paths = new EnginePaths(_home);
		var first = new Session(paths, 8352);
		var second = new Session(paths, 8352);

		Assert.Equal(64, first.Token.Length);
		Assert.Equal(first.Token, second.Token);
		if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(paths.Token));
	}

	[Theory]
	[InlineData("127.0.0.1:8352", true)]
	[InlineData("localhost:8352", true)]
	[InlineData("127.0.0.1:9999", false)]
	[InlineData("evil.example", false)]
	[InlineData("127.0.0.1:8352.evil.example", false)]
	[InlineData(null, false)]
	public void Only_the_local_host_is_served(string? host, bool allowed)
	{
		Assert.Equal(allowed, new Session(new EnginePaths(_home), 8352).HostAllowed(host));
	}

	[Fact]
	public void Writes_from_another_origin_are_refused_and_wrong_tokens_do_not_match()
	{
		var session = new Session(new EnginePaths(_home), 8352);

		Assert.True(session.OriginAllowed(null));
		Assert.True(session.OriginAllowed("http://127.0.0.1:8352"));
		Assert.False(session.OriginAllowed("http://evil.example"));
		Assert.True(session.Matches(session.Token));
		Assert.False(session.Matches(session.Token[..^1] + "0x"));
		Assert.False(session.Matches(null));
	}
}

public class DaysTests
{
	static readonly TimeZoneInfo Madrid = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");

	[Theory]
	[InlineData(2026, 6, 15, 24)]
	[InlineData(2026, 3, 29, 23)]
	[InlineData(2026, 10, 25, 25)]
	public void A_local_day_is_23_24_or_25_hours(int year, int month, int day, int hours)
	{
		var bounds = Days.Bounds(new DateOnly(year, month, day), Madrid);

		Assert.Equal(hours * 3_600_000L, bounds.DurationMs);
	}
}

public class EngineDatabaseTests
{
	[Fact]
	public void One_open_gap_per_camera_and_closing_never_goes_backwards()
	{
		using var db = new EngineDatabase(":memory:");
		db.OpenGap("entrada", new RecordedGap(1000, null, GapCause.Sleep));
		db.OpenGap("entrada", new RecordedGap(2000, null, GapCause.Camera));

		Assert.Equal(new RecordedGap(1000, null, GapCause.Sleep), db.OpenGapOf("entrada"));
		db.CloseGap("entrada", 500);
		Assert.Null(db.OpenGapOf("entrada"));
		Assert.Equal([new RecordedGap(1000, 1000, GapCause.Sleep)], db.GapsIn("entrada", new TimeRange(0, 5000)));
		Assert.Equal(1000, db.LastGapEnd("entrada"));
	}

	[Fact]
	public void Gaps_are_found_by_overlap_and_kept_per_camera()
	{
		using var db = new EngineDatabase(":memory:");
		db.OpenGap("a", new RecordedGap(1000, null, GapCause.Network));
		db.CloseGap("a", 3000);
		db.OpenGap("b", new RecordedGap(1500, null, GapCause.Power));

		Assert.Single(db.GapsIn("a", new TimeRange(2500, 9000)));
		Assert.Empty(db.GapsIn("a", new TimeRange(3000, 9000)));
		Assert.Equal(GapCause.Power, Assert.Single(db.GapsIn("b", new TimeRange(0, 9000))).Cause);
		Assert.Null(db.LastGapEnd("b"));
	}

	[Fact]
	public void State_is_overwritten_by_key()
	{
		using var db = new EngineDatabase(":memory:");
		Assert.Null(db.State("last_alive_ms"));
		db.SetState("last_alive_ms", 1);
		db.SetState("last_alive_ms", 2);

		Assert.Equal(2, db.State("last_alive_ms"));
	}
}

public class RecordingStoreTests : IDisposable
{
	readonly string _root = Directory.CreateTempSubdirectory("velador-rec").FullName;

	public void Dispose() => Directory.Delete(_root, true);

	[Theory]
	[InlineData("2026-11-12_21-15-09-228278-0600.mp4", 1794539709228)]
	[InlineData("2026-06-01_00-00-00-000000+0200.mp4", 1780264800000)]
	public void The_file_name_gives_the_start_in_utc(string name, long expected)
	{
		Assert.Equal(expected, RecordingStore.StartOf(name));
	}

	[Theory]
	[InlineData("notes.mp4")]
	[InlineData("2026-13-40_99-00-00-000000-0600.mp4")]
	public void Foreign_files_are_ignored(string name)
	{
		Assert.Null(RecordingStore.StartOf(name));
	}

	[Fact]
	public void Segments_are_listed_per_stream_and_only_files_inside_the_root_are_deleted()
	{
		Directory.CreateDirectory(Path.Combine(_root, "entradamain"));
		var file = Path.Combine(_root, "entradamain", "2026-11-12_21-15-09-228278-0600.mp4");
		File.WriteAllBytes(file, new byte[10]);
		File.WriteAllText(Path.Combine(_root, "entradamain", "readme.txt"), "x");
		var outside = Path.Combine(Path.GetTempPath(), $"velador-outside-{Guid.NewGuid():N}.mp4");
		File.WriteAllText(outside, "x");
		var store = new RecordingStore(_root);

		var segment = Assert.Single(store.Segments());
		Assert.Equal(("entradamain", 10L), (segment.Stream, segment.Bytes));
		store.Delete(segment with { File = outside });
		Assert.True(File.Exists(outside));
		store.Delete(segment);
		Assert.False(File.Exists(file));
		File.Delete(outside);
	}
}
