using Velador.Engine.Tapo;

namespace Velador.Engine.Api;

public static class TapoEndpoints
{
	static readonly Dictionary<string, string> Modes = new() { ["infrared"] = "inf_night_vision", ["white"] = "wtl_night_vision", ["smart"] = "md_night_vision" };

	public sealed record LightRequest(bool On);

	public sealed record NightRequest(string Mode);

	public sealed record FlipRequest(bool On);

	public sealed record WifiRequest(string Ssid, string? Password);

	public static void MapTapo(this RouteGroupBuilder api)
	{
		api.MapGet("/cameras/{id}/extras", (string id, TapoLinks links, CancellationToken ct) => Run(links, id, async tapo =>
		{
			var light = await tapo.LightAsync(ct);
			var image = await tapo.ImageAsync(ct);
			var modes = await tapo.NightModesAsync(ct);
			var mode = image["night_vision_mode"]?.GetValue<string>();
			return Results.Ok(new
			{
				light = new { on = light.On, restSeconds = light.RestSeconds },
				nightMode = Modes.FirstOrDefault(m => m.Value == mode).Key ?? "infrared",
				nightModes = Modes.Where(m => modes.Contains(m.Value)).Select(m => m.Key),
				flipped = image["flip_type"]?.GetValue<string>() is { } flip && flip != "off",
			});
		}));
		api.MapPost("/cameras/{id}/light", (string id, LightRequest request, TapoLinks links, CancellationToken ct) =>
			Run(links, id, async tapo => Results.Ok(new { on = await tapo.SetLightAsync(request.On, ct) })));
		api.MapPut("/cameras/{id}/night", (string id, NightRequest request, TapoLinks links, CancellationToken ct) =>
			Modes.TryGetValue(request.Mode, out var mode)
				? Run(links, id, async tapo =>
				{
					await tapo.SetImageAsync("night_vision_mode", mode, ct);
					return Results.NoContent();
				})
				: Task.FromResult(Results.BadRequest(new { error = "Ese modo no existe." })));
		api.MapPut("/cameras/{id}/flip", (string id, FlipRequest request, TapoLinks links, CancellationToken ct) => Run(links, id, async tapo =>
		{
			await tapo.SetImageAsync("flip_type", request.On ? "center" : "off", ct);
			return Results.NoContent();
		}));
		api.MapGet("/cameras/{id}/wifi", (string id, TapoLinks links, CancellationToken ct) => Run(links, id, async tapo =>
			Results.Ok((await tapo.ScanAsync(ct)).OrderByDescending(n => n.Rssi).Select(n => new { n.Ssid, signal = n.Rssi, secured = n.Auth != 0 }))));
		api.MapPost("/cameras/{id}/wifi", (string id, WifiRequest request, TapoLinks links, CancellationToken ct) =>
			string.IsNullOrWhiteSpace(request.Ssid)
				? Task.FromResult(Results.BadRequest(new { error = "Falta el nombre de la red." }))
				: Run(links, id, async tapo =>
				{
					await tapo.JoinAsync(request.Ssid, request.Password ?? "", ct);
					return Results.Accepted();
				}));
	}

	static async Task<IResult> Run(TapoLinks links, string id, Func<TapoClient, Task<IResult>> work)
	{
		if (links.For(id) is not { } tapo) return Results.NotFound(new { error = "Falta la contraseña de la cuenta Tapo de esta cámara." });
		try
		{
			return await work(tapo);
		}
		catch (TapoException e)
		{
			if (e.Code is -40411 or -40404) links.Forget(id);
			return Results.Json(new { error = e.Message }, statusCode: StatusCodes.Status502BadGateway);
		}
	}
}
