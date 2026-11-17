using Velador.Core.Vision;
using Velador.Engine.Onvif;
using Velador.Engine.Vision;

namespace Velador.Engine.Api;

public static class ZoneEndpoints
{
	public sealed record ZoneRequest(double MinX, double MaxX, double MinY, double MaxY, double HomeX, double HomeY, double? RouteLeftY = null, double? RouteMidX = null, double? RouteMidY = null, double? RouteRightY = null);

	public sealed record GotoRequest(double X, double Y);

	public static void MapZone(this RouteGroupBuilder api)
	{
		api.MapGet("/cameras/{id}/zone", View);
		api.MapPut("/cameras/{id}/zone", Save);
		api.MapDelete("/cameras/{id}/zone", (string id, EngineSettings settings, VisionService vision) =>
		{
			if (settings.Cameras.All(c => c.Id != id)) return Results.NotFound();
			vision.SetZone(id, null);
			return Results.NoContent();
		});
		api.MapPost("/cameras/{id}/ptz/goto", Goto);
		api.MapPost("/cameras/{id}/zone/survey", (string id, EngineSettings settings, VisionService vision) =>
		{
			if (settings.Cameras.FirstOrDefault(c => c.Id == id) is not { } camera) return Results.NotFound();
			try
			{
				vision.StartSurvey(camera);
				return Results.Accepted();
			}
			catch (InvalidOperationException e)
			{
				return Results.BadRequest(new { error = e.Message });
			}
		});
		api.MapDelete("/cameras/{id}/zone/survey", (string id, VisionService vision) =>
		{
			vision.StopSurvey(id);
			return Results.NoContent();
		});
		api.MapGet("/cameras/{id}/zone/survey/{index:int}", (string id, int index, EngineSettings settings, VisionService vision) =>
			settings.Cameras.Any(c => c.Id == id) && index is >= 0 and < 1000 && File.Exists(vision.SurveyTileFile(id, index))
				? Results.File(vision.SurveyTileFile(id, index), "image/png")
				: Results.NotFound());
	}

	static async Task<IResult> View(string id, EngineSettings settings, VisionService vision, CameraLinks links, CancellationToken ct)
	{
		if (settings.Cameras.All(c => c.Id != id)) return Results.NotFound();
		PanTilt? position = null;
		if (links.For(id) is { } onvif)
		{
			try
			{
				position = await onvif.PositionAsync(ct);
			}
			catch (Exception e) when (e is HttpRequestException or OperationCanceledException or System.Xml.XmlException) { }
		}
		var zone = vision.Zone(id);
		var calibration = vision.Calibration(id);
		var survey = vision.Survey(id);
		return Results.Ok(new
		{
			zone,
			route = zone is null ? null : new { left = new { x = zone.RouteLeft.X, y = zone.RouteLeft.Y }, mid = new { x = zone.RouteMid.X, y = zone.RouteMid.Y }, right = new { x = zone.RouteRight.X, y = zone.RouteRight.Y } },
			position = position is null ? null : new { x = position.X, y = position.Y },
			inside = zone is null || position is null || zone.Contains(position.X, position.Y),
			busy = vision.Busy(id),
			calibrated = calibration is not null,
			view = new { w = 1 / Math.Abs(calibration?.ViewPerX ?? 2.3), h = 1 / Math.Abs(calibration?.ViewPerY ?? 1.1), right = Math.Sign(calibration?.ViewPerX ?? 1), down = Math.Sign(calibration?.ViewPerY ?? 1) },
			survey = survey is null ? null : new { survey.Running, survey.Done, survey.Total, survey.AtMs, survey.Error, survey.TileW, survey.TileH, tiles = survey.Tiles.ToList() },
		});
	}

	static IResult Save(string id, ZoneRequest request, EngineSettings settings, VisionService vision)
	{
		if (settings.Cameras.All(c => c.Id != id)) return Results.NotFound();
		var zone = new PtzZone(request.MinX, request.MaxX, request.MinY, request.MaxY, request.HomeX, request.HomeY, request.RouteLeftY, request.RouteMidX, request.RouteMidY, request.RouteRightY);
		if (!zone.IsValid) return Results.BadRequest(new { error = "Los límites no son válidos: el reposo debe quedar dentro y la zona no puede ser tan angosta." });
		vision.SetZone(id, zone);
		return Results.Ok(zone);
	}

	static async Task<IResult> Goto(string id, GotoRequest request, EngineSettings settings, CameraLinks links, VisionService vision, CancellationToken ct)
	{
		if (links.For(id) is not { } onvif || settings.Cameras.All(c => c.Id != id)) return Results.NotFound();
		if (!double.IsFinite(request.X) || !double.IsFinite(request.Y)) return Results.BadRequest();
		if (vision.Busy(id)) return Results.BadRequest(new { error = "La cámara está ocupada midiendo; espera a que termine." });
		try
		{
			vision.ManualMove(id);
			await onvif.AbsoluteMoveAsync(new PanTilt(Math.Clamp(request.X, -1, 1), Math.Clamp(request.Y, -1, 1)), ct);
			return Results.NoContent();
		}
		catch (Exception e) when (e is HttpRequestException or OperationCanceledException or System.Xml.XmlException)
		{
			return Results.StatusCode(StatusCodes.Status502BadGateway);
		}
	}
}
