using System.Globalization;
using Velador.Engine.Sqlite;
using Velador.Engine.Vision;

namespace Velador.Engine.Api;

public static class VisionEndpoints
{
	public sealed record FollowRequest(bool Follow);

	public static void MapVision(this RouteGroupBuilder api)
	{
		api.MapGet("/cameras/{id}/vision", (string id, EngineSettings settings, VisionService vision) =>
			settings.Cameras.Any(c => c.Id == id) ? Results.Ok(View(id, vision)) : Results.NotFound());
		api.MapPut("/cameras/{id}/vision", Follow);
		api.MapPost("/cameras/{id}/vision/calibrate", Calibrate);
		api.MapGet("/cameras/{id}/people", People);
		api.MapGet("/sightings/{sightingId:long}/image", (long sightingId, EngineDatabase database) =>
			database.SightingOf(sightingId) is { } s && File.Exists(s.File) ? Results.File(s.File, "image/png") : Results.NotFound());
		api.MapDelete("/people/{personId:long}", (long personId, EngineDatabase database) =>
		{
			foreach (var file in database.RemovePerson(personId))
			{
				try
				{
					File.Delete(file);
				}
				catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
			}
			return Results.NoContent();
		});
	}

	static object View(string id, VisionService vision)
	{
		var s = vision.Snapshot(id);
		var c = vision.Calibration(id);
		return new
		{
			status = s.Status,
			detail = s.Detail,
			fps = s.Fps,
			device = vision.Device,
			following = s.Following,
			manualHold = s.ManualHold,
			targetId = s.TargetId,
			coasting = s.Coasting,
			command = new { x = Math.Round(s.Command.X, 2), y = Math.Round(s.Command.Y, 2) },
			calibration = c is null ? null : new { c.GainX, c.GainY, c.LagS, c.Continuous, c.AtMs },
			tracks = s.Tracks.Select(t => new { t.Id, x = R(t.Box.X), y = R(t.Box.Y), w = R(t.Box.W), h = R(t.Box.H), score = R(t.Score), seen = t.SeenNow }),
			atMs = s.AtMs,
		};
	}

	static double R(double v) => Math.Round(v, 4);

	static async Task<IResult> Follow(string id, FollowRequest request, EngineSettings settings, VisionService vision, CancellationToken ct)
	{
		if (settings.Cameras.FirstOrDefault(c => c.Id == id) is not { } camera) return Results.NotFound();
		if (request.Follow && !settings.DetectPeople) return Results.BadRequest(new { error = "Activa la detección de personas en Ajustes para poder seguirlas." });
		if (request.Follow && vision.Calibration(id) is null)
		{
			try
			{
				await vision.CalibrateAsync(camera, ct);
			}
			catch (InvalidOperationException e)
			{
				return Results.BadRequest(new { error = e.Message });
			}
			catch (Exception e) when (e is HttpRequestException or TimeoutException or System.Xml.XmlException)
			{
				return Results.BadRequest(new { error = "La cámara no respondió al calibrar el movimiento." });
			}
		}
		vision.SetFollow(id, request.Follow);
		return Results.Ok(View(id, vision));
	}

	static async Task<IResult> Calibrate(string id, EngineSettings settings, VisionService vision, CancellationToken ct)
	{
		if (settings.Cameras.FirstOrDefault(c => c.Id == id) is not { } camera) return Results.NotFound();
		try
		{
			await vision.CalibrateAsync(camera, ct);
			return Results.Ok(View(id, vision));
		}
		catch (Exception e) when (e is InvalidOperationException or HttpRequestException or TimeoutException or OperationCanceledException or System.Xml.XmlException)
		{
			return Results.BadRequest(new { error = e is InvalidOperationException ? e.Message : "La cámara no respondió al calibrar el movimiento.", steps = vision.CalibrationLog });
		}
	}

	static IResult People(string id, string? date, EngineSettings settings, EngineDatabase database)
	{
		if (settings.Cameras.All(c => c.Id != id)) return Results.NotFound();
		var day = date ?? DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
		if (!DateOnly.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return Results.BadRequest();
		return Results.Ok(database.People(id, day).Select(p => new
		{
			p.Id,
			firstMs = p.FirstMs,
			lastMs = p.LastMs,
			sightings = p.Sightings.Select(s => new { s.Id, startMs = s.StartMs, endMs = s.EndMs }),
		}));
	}
}
