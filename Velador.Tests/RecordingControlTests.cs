using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Velador.Core;
using Velador.Engine;
using Velador.Engine.Continuity;
using Velador.Engine.MediaMtx;
using Velador.Engine.Sqlite;

namespace Velador.Tests;

public class RecordingControlTests
{
	static readonly Camera Entrada = new("entrada", "Entrada", "10.0.0.9", "user");

	sealed class Recorder : HttpMessageHandler
	{
		public List<(string Method, string Path, string Body)> Calls { get; } = [];

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
		{
			Calls.Add((request.Method.Method, request.RequestUri!.AbsolutePath, request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct)));
			return new HttpResponseMessage(HttpStatusCode.OK);
		}
	}

	sealed class Clock(long ms) : TimeProvider
	{
		public long Ms { get; set; } = ms;
		public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(Ms);
	}

	static (RecordingControl Control, EngineDatabase Db, Recorder Http, EngineSettings Settings, Clock Clock) Make()
	{
		var paths = new EnginePaths(Path.Combine(Path.GetTempPath(), $"velador-pause-{Guid.NewGuid():N}"));
		var settings = new EngineSettings { Cameras = [Entrada] };
		var db = new EngineDatabase(":memory:");
		var http = new Recorder();
		var clock = new Clock(1_000_000);
		var control = new RecordingControl(settings, paths, db, new MediaMtxClient(new HttpClient(http), MediaPorts.From(8352)), clock, NullLogger<RecordingControl>.Instance);
		return (control, db, http, settings, clock);
	}

	[Fact]
	public void A_paused_camera_is_written_without_recording()
	{
		var yaml = MediaMtxConfig.Build(MediaPorts.From(8352), "/data/rec", [Entrada, Entrada with { Id = "patio" }], new HashSet<string> { "entrada" });

		Assert.Contains("  entradamain:\n    source: rtsp://127.0.0.1:1/none\n    record: no\n", yaml);
		Assert.Contains("  patiomain:\n    source: rtsp://127.0.0.1:1/none\n  patiominor:", yaml);
	}

	[Fact]
	public async Task Pausing_opens_a_paused_gap_and_turns_recording_off_for_both_streams()
	{
		var (control, db, http, settings, _) = Make();

		var state = await control.PauseAsync(Entrada, 15, CancellationToken.None);

		Assert.True(state.Paused);
		Assert.Equal(1_000_000 + 15 * 60_000, state.ResumeAtMs);
		Assert.Equal(GapCause.Paused, db.OpenGapOf("entrada")!.Cause);
		Assert.Equal(["/v3/config/paths/patch/entradamain", "/v3/config/paths/patch/entradaminor"], http.Calls.Select(c => c.Path));
		Assert.All(http.Calls, c => Assert.Equal(("PATCH", "{\"record\":false}"), (c.Method, c.Body)));
		Assert.True(settings.Paused.ContainsKey("entrada"));
	}

	[Fact]
	public async Task Resuming_closes_only_the_paused_gap()
	{
		var (control, db, http, settings, clock) = Make();
		await control.PauseAsync(Entrada, null, CancellationToken.None);
		clock.Ms += 60_000;

		var state = await control.ResumeAsync(Entrada, CancellationToken.None);

		Assert.False(state.Paused);
		Assert.Null(db.OpenGapOf("entrada"));
		var gap = Assert.Single(db.GapsIn("entrada", new TimeRange(0, 2_000_000)));
		Assert.Equal((1_000_000L, 1_060_000L, GapCause.Paused), (gap.StartMs, gap.EndMs!.Value, gap.Cause));
		Assert.Equal("{\"record\":true}", http.Calls[^1].Body);
		Assert.Empty(settings.Paused);
	}

	[Fact]
	public async Task Resuming_leaves_a_camera_gap_open()
	{
		var (control, db, _, _, _) = Make();
		db.OpenGap("entrada", new RecordedGap(500_000, null, GapCause.Camera));

		await control.ResumeAsync(Entrada, CancellationToken.None);

		Assert.Equal(GapCause.Camera, db.OpenGapOf("entrada")!.Cause);
	}

	[Fact]
	public async Task Pausing_during_a_camera_gap_ends_it_and_starts_the_pause()
	{
		var (control, db, _, _, _) = Make();
		db.OpenGap("entrada", new RecordedGap(500_000, null, GapCause.Camera));

		await control.PauseAsync(Entrada, null, CancellationToken.None);

		var gaps = db.GapsIn("entrada", new TimeRange(0, 2_000_000));
		Assert.Equal([GapCause.Camera, GapCause.Paused], gaps.Select(g => g.Cause));
		Assert.Equal(1_000_000, gaps[0].EndMs);
	}
}
