using System.Globalization;
using Velador.Core;
using Velador.Engine.Continuity;
using Velador.Engine.MediaMtx;
using Velador.Engine.Onvif;
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
		api.MapGet("/cameras/{id}/ptz", Position);
		api.MapPost("/cameras/{id}/ptz", Move);
		api.MapPost("/cameras/{id}/recording", Recording);
		api.MapManagement();
		api.MapTapo();
		api.MapPost("/cameras/{id}/live", (string id, HttpContext context, EngineSettings settings, MediaMtxClient media) => Whep(id, null, context, settings, media));
		api.MapMethods("/cameras/{id}/live/{session}", ["PATCH", "DELETE"], (string id, string session, HttpContext context, EngineSettings settings, MediaMtxClient media) => Whep(id, session, context, settings, media));
	}

	static IResult Summary(EngineSettings settings, Storage storage, EngineDatabase database, ContinuityService continuity, RecordingControl recording, MediaSupervisor supervisor, TimeProvider time)
	{
		var now = time.GetUtcNow().ToUnixTimeMilliseconds();
		var cameras = settings.Cameras.Select(camera =>
		{
			var guard = continuity.Guard(camera.Id);
			var last = database.LastEvent(camera.Id);
			var pause = recording.State(camera.Id);
			return new
			{
				camera.Id,
				camera.Name,
				camera.Rotation,
				state = pause.Paused ? "paused" : guard.OpenGap is not null ? "gap" : guard.Online ? "on_guard" : "reconnecting",
				pausedUntilMs = pause.ResumeAtMs,
				gap = guard.OpenGap is null ? null : new { startMs = guard.OpenGap.StartMs, cause = guard.OpenGap.Cause.ToCode() },
				guardSinceMs = guard.GuardSinceMs,
				lastEvent = last is null ? null : new { last.Id, startMs = last.StartMs, kind = last.Kind.ToCode() },
			};
		});
		return Results.Ok(new
		{
			nowMs = now,
			state = settings.Cameras.Count == 0 ? "no_camera" : supervisor.Missing ? "no_media_server" : "running",
			cameras,
			disk = Disk(storage.Active),
			storage = new { active = storage.Active, primary = storage.Primary, external = storage.UsesExternal, onFallback = storage.OnFallback, released = storage.Released },
			retention = new { days = settings.RetentionDays, percent = settings.RetentionPercent },
		});
	}

	public static object? Disk(string dir)
	{
		try
		{
			var full = Path.GetFullPath(dir);
			var drive = DriveInfo.GetDrives().Where(d => d.IsReady && full.StartsWith(d.RootDirectory.FullName, StringComparison.Ordinal)).OrderByDescending(d => d.RootDirectory.FullName.Length).FirstOrDefault();
			return drive is null ? null : new { totalBytes = drive.TotalSize, freeBytes = drive.AvailableFreeSpace };
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
		{
			return null;
		}
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
			events = database.EventsIn(id, window).Select(e => new { e.Id, startMs = e.StartMs, endMs = e.EndMs ?? now, kind = e.Kind.ToCode() }),
		});
	}

	public sealed record MoveRequest(double X, double Y);

	public sealed record RecordingRequest(bool Paused, int? Minutes);

	static async Task<IResult> Recording(string id, RecordingRequest request, EngineSettings settings, RecordingControl recording, CancellationToken ct)
	{
		if (settings.Cameras.FirstOrDefault(c => c.Id == id) is not { } camera) return Results.NotFound();
		if (request.Minutes is < 1 or > RecordingControl.MaxMinutes) return Results.BadRequest(new { error = "La pausa dura entre 1 minuto y 24 horas." });
		var state = request.Paused ? await recording.PauseAsync(camera, request.Minutes, ct) : await recording.ResumeAsync(camera, ct);
		return Results.Ok(new { paused = state.Paused, resumeAtMs = state.ResumeAtMs });
	}

	static async Task<IResult> Position(string id, CameraLinks links, CancellationToken ct)
	{
		if (links.For(id) is not { } onvif) return Results.NotFound();
		try
		{
			return await onvif.PositionAsync(ct) is { } position ? Results.Ok(new { x = position.X, y = position.Y }) : Results.StatusCode(StatusCodes.Status502BadGateway);
		}
		catch (Exception e) when (e is HttpRequestException or OperationCanceledException or System.Xml.XmlException)
		{
			return Results.StatusCode(StatusCodes.Status502BadGateway);
		}
	}

	static async Task<IResult> Move(string id, MoveRequest move, CameraLinks links, CancellationToken ct)
	{
		if (links.For(id) is not { } onvif) return Results.NotFound();
		if (!double.IsFinite(move.X) || !double.IsFinite(move.Y)) return Results.BadRequest();
		try
		{
			await onvif.MoveAsync(move.X, move.Y, ct);
			return Results.NoContent();
		}
		catch (Exception e) when (e is HttpRequestException or OperationCanceledException or System.Xml.XmlException)
		{
			return Results.StatusCode(StatusCodes.Status502BadGateway);
		}
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
