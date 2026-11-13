using Velador.Core;
using Velador.Engine;
using Velador.Engine.Api;
using Velador.Engine.Cli;
using Velador.Engine.Power;
using Velador.Engine.Secrets;
using Velador.Engine.MediaMtx;
using Velador.Engine.Onvif;
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

public class OnvifTests
{
	[Fact]
	public void The_password_digest_follows_ws_security()
	{
		var nonce = Convert.FromBase64String("LKqI6G/AikKCQrN0zqZFlg==");
		var envelope = OnvifClient.Envelope("admin", "userpassword", "<x/>", nonce, DateTimeOffset.Parse("2010-09-16T07:50:45Z"));

		Assert.Contains(">tuOSpGlFlIXsozq4HFNeeGeFLEI=</Password>", envelope);
		Assert.Contains("<Created xmlns=\"http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd\">2010-09-16T07:50:45Z</Created>", envelope);
		Assert.DoesNotContain("userpassword", envelope);
	}

	const string Pull = """
		<e:Envelope xmlns:e="http://www.w3.org/2003/05/soap-envelope" xmlns:tev="http://www.onvif.org/ver10/events/wsdl" xmlns:wsnt="http://docs.oasis-open.org/wsn/b-2" xmlns:tns1="http://www.onvif.org/ver10/topics" xmlns:tt="http://www.onvif.org/ver10/schema"><e:Body><tev:PullMessagesResponse>
		<wsnt:NotificationMessage><wsnt:Topic Dialect="x">tns1:RuleEngine/CellMotionDetector/Motion</wsnt:Topic><wsnt:Message><tt:Message UtcTime="2026-11-13T03:00:00Z" PropertyOperation="Changed"><tt:Data><tt:SimpleItem Name="IsMotion" Value="true"/></tt:Data></tt:Message></wsnt:Message></wsnt:NotificationMessage>
		<wsnt:NotificationMessage><wsnt:Topic Dialect="x">tns1:RuleEngine/PeopleDetector/People</wsnt:Topic><wsnt:Message><tt:Message><tt:Data><tt:SimpleItem Name="IsPeople" Value="false"/></tt:Data></tt:Message></wsnt:Message></wsnt:NotificationMessage>
		<wsnt:NotificationMessage><wsnt:Topic Dialect="x">tns1:RuleEngine/CellMotionDetector/Motion</wsnt:Topic></wsnt:NotificationMessage>
		<wsnt:NotificationMessage><wsnt:Topic Dialect="x">tns1:Device/Trigger/Relay</wsnt:Topic></wsnt:NotificationMessage>
		</tev:PullMessagesResponse></e:Body></e:Envelope>
		""";

	[Fact]
	public void Motion_and_person_signals_are_read_and_unknown_topics_ignored()
	{
		Assert.Equal(
			[new CameraSignal(EventKind.Motion, true), new CameraSignal(EventKind.Person, false), new CameraSignal(EventKind.Motion, true)],
			OnvifEvents.Parse(Pull));
		Assert.Empty(OnvifEvents.Parse("not xml"));
	}

	[Fact]
	public void One_open_event_per_kind_and_they_show_in_the_window()
	{
		using var db = new EngineDatabase(":memory:");
		db.OpenEvent("entrada", EventKind.Motion, 1000);
		db.OpenEvent("entrada", EventKind.Motion, 2000);
		db.OpenEvent("entrada", EventKind.Person, 2500);
		db.CloseEvent("entrada", EventKind.Motion, 1200);

		var events = db.EventsIn("entrada", new TimeRange(0, 10_000));
		Assert.Equal(2, events.Count);
		Assert.Equal((1000L, 2000L, EventKind.Motion), (events[0].StartMs, events[0].EndMs!.Value, events[0].Kind));
		Assert.Null(events[1].EndMs);
		db.CloseEventsOlderThan("entrada", 3000, 9000);
		Assert.Equal(9000, db.EventsIn("entrada", new TimeRange(0, 10_000))[1].EndMs);
	}
}

public class StorageTests : IDisposable
{
	readonly string _root = Directory.CreateTempSubdirectory("velador-storage").FullName;

	public void Dispose() => Directory.Delete(_root, true);

	sealed class Clock : TimeProvider
	{
		public DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

		public override DateTimeOffset GetUtcNow() => Now;
	}

	[Fact]
	public void Without_an_external_folder_everything_stays_in_the_default_one()
	{
		var fallback = Path.Combine(_root, "internal");
		var storage = new Storage(new EngineSettings(), fallback, TimeProvider.System);

		Assert.False(storage.UsesExternal);
		Assert.Equal(fallback, storage.Activate());
		Assert.False(storage.OnFallback);
	}

	[Fact]
	public void A_missing_volume_falls_back_to_the_internal_folder()
	{
		var fallback = Path.Combine(_root, "internal");
		var storage = new Storage(new EngineSettings { RecordingsDir = OperatingSystem.IsWindows() ? @"Q:\Velador" : "/Volumes/NoExisteVelador/Velador" }, fallback, TimeProvider.System);

		Assert.True(storage.UsesExternal);
		Assert.False(storage.PrimaryAvailable);
		Assert.Equal(fallback, storage.Activate());
		Assert.True(storage.OnFallback);
	}

	[Fact]
	public void Releasing_the_disk_switches_to_the_internal_folder_for_a_while()
	{
		var clock = new Clock();
		var external = Path.Combine(_root, "external");
		var storage = new Storage(new EngineSettings { RecordingsDir = external }, Path.Combine(_root, "internal"), clock);

		Assert.Equal(external, storage.Activate());
		storage.Release(TimeSpan.FromMinutes(10));
		Assert.NotEqual(external, storage.Desired);
		clock.Now = clock.Now.AddMinutes(11);
		Assert.Equal(external, storage.Desired);
	}

	[Fact]
	public void What_was_recorded_inside_goes_back_to_the_external_disk()
	{
		var fallback = Path.Combine(_root, "internal");
		var external = Path.Combine(_root, "external");
		Directory.CreateDirectory(Path.Combine(fallback, "entradamain"));
		var stray = Path.Combine(fallback, "entradamain", "2026-11-12_21-15-09-228278-0600.mp4");
		File.WriteAllText(stray, "video");
		var storage = new Storage(new EngineSettings { RecordingsDir = external }, fallback, TimeProvider.System);
		storage.Activate();

		Assert.Equal(1, storage.Relocate());
		Assert.False(File.Exists(stray));
		Assert.Equal("video", File.ReadAllText(Path.Combine(external, "entradamain", "2026-11-12_21-15-09-228278-0600.mp4")));
		Assert.Equal(0, storage.Relocate());
	}

	[Theory]
	[InlineData("/Volumes/KINGSTON/Velador", "/Volumes/KINGSTON")]
	[InlineData("/Volumes/Mi Disco/a/b", "/Volumes/Mi Disco")]
	[InlineData("/Users/yo/Movies/Velador", null)]
	public void The_mount_point_of_an_external_path_is_its_volume(string path, string? mount)
	{
		if (OperatingSystem.IsWindows()) return;
		Assert.Equal(mount, Storage.MountPoint(path));
	}
}

public class DiscoveryAndClipTests
{
	[Fact]
	public void Discovery_replies_give_the_camera_address_and_port()
	{
		var reply = "<d:XAddrs>http://192.168.1.16:2020/onvif/device_service http://[fe80::1]:2020/onvif/device_service</d:XAddrs><d:XAddrs>http://10.0.0.7/onvif/device_service</d:XAddrs>";

		Assert.Equal([new FoundCamera("192.168.1.16", 2020), new FoundCamera("10.0.0.7", 80)], Discovery.Parse(reply));
		Assert.Empty(Discovery.Parse("<x>http://evil.example/onvif/</x>"));
	}

	[Fact]
	public void Clips_are_listed_newest_first_and_removed_by_id()
	{
		using var db = new EngineDatabase(":memory:");
		var first = db.AddClip("entrada", new TimeRange(1000, 31_000), "Uno", "/tmp/a.mp4", 10, 100);
		var second = db.AddClip("entrada", new TimeRange(5000, 9000), "Dos", "/tmp/b.mp4", 20, 200);

		Assert.Equal(["Dos", "Uno"], db.Clips().Select(c => c.Title));
		db.RemoveClip(second.Id);
		Assert.Equal(first, Assert.Single(db.Clips()));
	}

	[Fact]
	public void Signals_a_few_seconds_apart_are_one_event()
	{
		using var db = new EngineDatabase(":memory:");
		db.OpenEvent("entrada", EventKind.Motion, 1000);
		db.CloseEvent("entrada", EventKind.Motion, 3000);
		db.OpenEvent("entrada", EventKind.Motion, 8000);
		db.CloseEvent("entrada", EventKind.Motion, 9000);
		db.OpenEvent("entrada", EventKind.Motion, 40_000);

		var events = db.EventsIn("entrada", new TimeRange(0, 100_000));
		Assert.Equal(2, events.Count);
		Assert.Equal((1000L, 9000L), (events[0].StartMs, events[0].EndMs!.Value));
	}

	[Fact]
	public void The_last_event_is_the_most_recent_one()
	{
		using var db = new EngineDatabase(":memory:");
		Assert.Null(db.LastEvent("entrada"));
		db.OpenEvent("entrada", EventKind.Motion, 1000);
		db.OpenEvent("entrada", EventKind.Person, 5000);

		Assert.Equal(EventKind.Person, db.LastEvent("entrada")!.Kind);
	}
}

public class RelocationTests
{
	static readonly Camera Entrada = new("entrada", "Entrada", "192.168.1.16", "user", Serial: "ABC123");

	[Fact]
	public void The_camera_is_found_again_by_its_serial_at_a_new_address()
	{
		var place = Camera.Relocated(Entrada, [("192.168.1.40", 2020, "ZZZ"), ("192.168.1.77", 2020, "ABC123")]);

		Assert.Equal(("192.168.1.77", 2020), place);
	}

	[Fact]
	public void Nothing_changes_without_a_serial_or_without_a_single_clear_match()
	{
		Assert.Null(Camera.Relocated(Entrada with { Serial = "" }, [("192.168.1.77", 2020, "")]));
		Assert.Null(Camera.Relocated(Entrada, [("192.168.1.77", 2020, "OTHER")]));
		Assert.Null(Camera.Relocated(Entrada, [("192.168.1.16", 2020, "ABC123")]));
		Assert.Null(Camera.Relocated(Entrada, [("192.168.1.77", 2020, "ABC123"), ("192.168.1.78", 2020, "ABC123")]));
	}
}

public class WindowsTests
{
	[Fact]
	public void The_logon_task_runs_the_engine_for_this_user_and_restarts_it()
	{
		var xml = System.Xml.Linq.XDocument.Parse(Commands.TaskXml(@"C:\Users\Ana & Luis\Velador\Velador.Engine.exe", @"PC\ana").Replace("encoding=\"UTF-16\"", ""));
		string Value(string name) => xml.Descendants().First(e => e.Name.LocalName == name).Value;

		Assert.Equal(@"C:\Users\Ana & Luis\Velador\Velador.Engine.exe", Value("Command"));
		Assert.Equal(@"PC\ana", Value("UserId"));
		Assert.Equal("PT0S", Value("ExecutionTimeLimit"));
		Assert.Equal("false", Value("StopIfGoingOnBatteries"));
		Assert.Equal("PT1M", Value("Interval"));
	}

	[Fact]
	public void The_credential_manager_keeps_and_returns_a_secret()
	{
		if (!OperatingSystem.IsWindows()) return;
		var store = new WindowsCredentialStore();
		var id = $"prueba{Environment.ProcessId}";
		store.Set(id, "p@ss wörd/1.");

		Assert.Equal("p@ss wörd/1.", store.Get(id));
		Assert.Null(store.Get($"{id}no"));
	}

	[Fact]
	public void The_power_request_turns_on_and_off()
	{
		if (!OperatingSystem.IsWindows()) return;
		using var guard = new WindowsPowerGuard();
		guard.KeepAwake(true);
		guard.KeepAwake(true);
		guard.KeepAwake(false);
	}
}
