using System.Globalization;
using Velador.Core;
using Velador.Engine.Continuity;
using Velador.Engine.MediaMtx;
using Velador.Engine.Sqlite;

namespace Velador.Engine.Api;

public static class Endpoints
{
	public static void MapEngine(this WebApplication app)
	{
		var api = app.MapGroup("/api");
		api.MapGet("/summary", Summary);
		api.MapGet("/cameras/{id}/rounds/{date}", RoundOf);
		api.MapGet("/cameras/{id}/media", Media);
		api.MapPost("/cameras/{id}/live", (string id, HttpContext context, EngineSettings settings, MediaMtxClient media) => Whep(id, null, context, settings, media));
		api.MapMethods("/cameras/{id}/live/{session}", ["PATCH", "DELETE"], (string id, string session, HttpContext context, EngineSettings settings, MediaMtxClient media) => Whep(id, session, context, settings, media));
	}

	static IResult Summary(EngineSettings settings, EnginePaths paths, ContinuityService continuity, MediaSupervisor supervisor, TimeProvider time)
	{
		var recordings = paths.Recordings(settings);
		var drive = Directory.Exists(recordings) ? new DriveInfo(Path.GetPathRoot(Path.GetFullPath(recordings))!) : null;
		var cameras = settings.Cameras.Select(camera =>
		{
			var guard = continuity.Guard(camera.Id);
			return new
			{
				camera.Id,
				camera.Name,
				state = guard.OpenGap is not null ? "gap" : guard.Online ? "on_guard" : "reconnecting",
				gap = guard.OpenGap is null ? null : new { startMs = guard.OpenGap.StartMs, cause = guard.OpenGap.Cause.ToCode() },
				guardSinceMs = guard.GuardSinceMs,
			};
		});
		return Results.Ok(new
		{
			nowMs = time.GetUtcNow().ToUnixTimeMilliseconds(),
			state = settings.Cameras.Count == 0 ? "no_camera" : supervisor.Missing ? "no_media_server" : "running",
			cameras,
			disk = drive is null ? null : new { totalBytes = drive.TotalSize, freeBytes = drive.AvailableFreeSpace },
			retention = new { days = settings.RetentionDays, percent = settings.RetentionPercent },
		});
	}

	static async Task<IResult> RoundOf(string id, string date, string? tz, EngineSettings settings, EngineDatabase database, MediaMtxClient media, TimeProvider time, CancellationToken ct)
	{
		if (settings.Cameras.FirstOrDefault(c => c.Id == id) is not { } camera) return Results.NotFound();
		if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) return Results.BadRequest();
		if (!TryZone(tz, out var zone)) return Results.BadRequest();
		var window = Days.Bounds(day, zone);
		var now = time.GetUtcNow().ToUnixTimeMilliseconds();
		var watchedSince = database.State(ContinuityService.WatchedSince(id)) ?? now;
		IReadOnlyList<TimeRange> spans = watchedSince >= window.EndMs || window.StartMs > now ? [] : await media.SpansAsync(camera.MediaPath(Camera.MainStream), window, ct);
		var round = RoundBuilder.Build(window, now, spans, database.GapsIn(id, window), watchedSince);
		return Results.Ok(new
		{
			date,
			tz = zone.Id,
			startMs = window.StartMs,
			endMs = window.EndMs,
			watchedFromMs = round.WatchedFromMs,
			nowMs = round.EndMs,
			live = now >= window.StartMs && now < window.EndMs,
			coverage = Math.Round(round.Coverage, 3),
			spans = round.Spans.Select(s => new { startMs = s.StartMs, endMs = s.EndMs }),
			gaps = round.Gaps.Select(g => new { startMs = g.Range.StartMs, endMs = g.Range.EndMs, cause = g.Cause.ToCode() }),
			events = Array.Empty<object>(),
		});
	}

	static async Task<IResult> Media(string id, long startMs, long durationMs, string? stream, EngineSettings settings, MediaCache cache, IHostApplicationLifetime lifetime, CancellationToken ct)
	{
		var camera = settings.Cameras.FirstOrDefault(c => c.Id == id);
		var path = stream is null or Camera.MainStream ? Camera.MainStream : stream == Camera.MinorStream ? Camera.MinorStream : null;
		if (camera is null) return Results.NotFound();
		if (path is null || startMs <= 0 || durationMs is <= 0 or > 600_000) return Results.BadRequest();
		using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.ApplicationStopping);
		var file = await cache.FileAsync(camera.MediaPath(path), startMs, durationMs, stop.Token);
		return file is null ? Results.NotFound() : Results.File(file, "video/mp4", enableRangeProcessing: true);
	}

	static async Task Whep(string id, string? session, HttpContext context, EngineSettings settings, MediaMtxClient media)
	{
		if (settings.Cameras.FirstOrDefault(c => c.Id == id) is not { } camera || (session is not null && !Guid.TryParse(session, out _)))
		{
			context.Response.StatusCode = StatusCodes.Status404NotFound;
			return;
		}
		var target = $"{media.WebRtc}/{camera.MediaPath(Camera.MainStream)}/whep{(session is null ? "" : $"/{session}")}";
		using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), target);
		if (!HttpMethods.IsDelete(context.Request.Method))
		{
			request.Content = new StreamContent(context.Request.Body);
			if (context.Request.ContentType is { } type) request.Content.Headers.TryAddWithoutValidation("Content-Type", type);
		}
		if (context.Request.Headers.IfMatch.FirstOrDefault() is { } etag) request.Headers.TryAddWithoutValidation("If-Match", etag);
		using var upstream = await media.SendAsync(request, context.RequestAborted);
		context.Response.StatusCode = (int)upstream.StatusCode;
		if (upstream.Headers.Location is { } location)
			context.Response.Headers.Location = $"/api/cameras/{id}/live/{location.OriginalString.TrimEnd('/').Split('/')[^1]}";
		if (upstream.Headers.ETag is { } tag) context.Response.Headers.ETag = tag.ToString();
		if (upstream.Content.Headers.ContentType is { } contentType) context.Response.ContentType = contentType.ToString();
		await upstream.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
	}

	static bool TryZone(string? id, out TimeZoneInfo zone)
	{
		zone = TimeZoneInfo.Local;
		return id is null || TimeZoneInfo.TryFindSystemTimeZoneById(id, out zone!);
	}
}

public static class Days
{
	public static TimeRange Bounds(DateOnly day, TimeZoneInfo zone) => new(Midnight(day, zone), Midnight(day.AddDays(1), zone));

	static long Midnight(DateOnly day, TimeZoneInfo zone)
	{
		var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
		while (zone.IsInvalidTime(local)) local = local.AddMinutes(30);
		return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUnixTimeMilliseconds();
	}
}
