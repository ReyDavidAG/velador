using System.Diagnostics;
using System.Net.Sockets;
using Velador.Core;
using Velador.Engine.MediaMtx;
using Velador.Engine.Onvif;
using Velador.Engine.Secrets;
using Velador.Engine.Sqlite;
using Velador.Engine.Tapo;

namespace Velador.Engine.Api;

public static class ManageEndpoints
{
	public sealed record SettingsRequest(string? RecordingsDir, int RetentionDays, int RetentionPercent, bool KeepAwake, bool? DetectPeople = null);

	public sealed record CameraRequest(string Name, string Host, string User, string? Password, int Rotation, string? TapoPassword = null);

	public sealed record TestRequest(string Host, string User, string? Password, string? Id);

	public sealed record ClipRequest(long StartMs, long EndMs, string? Title);

	public static void MapManagement(this RouteGroupBuilder api)
	{
		api.MapGet("/settings", (EngineSettings settings, Storage storage, IRecordingStore store, TimeProvider time) => Results.Ok(SettingsView(settings, storage, store, time)));
		api.MapPut("/settings", SaveSettings);
		api.MapPost("/storage/release", (Storage storage) =>
		{
			storage.Release(TimeSpan.FromMinutes(10));
			return Results.NoContent();
		});
		api.MapPost("/storage/reclaim", (Storage storage) =>
		{
			storage.Reclaim();
			return Results.NoContent();
		});
		api.MapGet("/cameras", (EngineSettings settings, RelocateService relocate) => Results.Ok(settings.Cameras.Select(c => new
		{
			c.Id,
			c.Name,
			c.Host,
			c.User,
			c.Rotation,
			followsAddress = c.Serial.Length > 0,
			networks = (settings.Networks.GetValueOrDefault(c.Id) ?? []).Select(n => new { n.Id, n.Name, n.Host, current = n.Id == relocate.Network?.Id }),
		})));
		api.MapPost("/cameras/{id}/networks/{networkId}/use", (string id, string networkId, RelocateService relocate) => relocate.Use(id, networkId) ? Results.NoContent() : Results.NotFound());
		api.MapDelete("/cameras/{id}/networks/{networkId}", (string id, string networkId, RelocateService relocate) => relocate.Forget(id, networkId) ? Results.NoContent() : Results.NotFound());
		api.MapGet("/cameras/discover", async (EngineSettings settings, CancellationToken ct) =>
			Results.Ok((await Discovery.ProbeAsync(TimeSpan.FromSeconds(2), ct)).Select(f => new { f.Host, f.OnvifPort, known = settings.Cameras.Any(c => c.Host == f.Host) })));
		api.MapPost("/cameras/test", Test);
		api.MapPut("/cameras/{id}", SaveCamera);
		api.MapDelete("/cameras/{id}", RemoveCamera);
		api.MapGet("/clips", (EngineDatabase database) => Results.Ok(database.Clips().Select(ClipView)));
		api.MapPost("/cameras/{id}/clips", AddClip);
		api.MapGet("/clips/{clipId:long}/file", (long clipId, EngineDatabase database) =>
			database.Clips().FirstOrDefault(c => c.Id == clipId) is { } clip && File.Exists(clip.File) ? Results.File(clip.File, "video/mp4", enableRangeProcessing: true) : Results.NotFound());
		api.MapPost("/clips/{clipId:long}/reveal", Reveal);
		api.MapDelete("/clips/{clipId:long}", (long clipId, EngineDatabase database) =>
		{
			if (database.Clips().FirstOrDefault(c => c.Id == clipId) is not { } clip) return Results.NotFound();
			File.Delete(clip.File);
			database.RemoveClip(clipId);
			return Results.NoContent();
		});
	}

	static object SettingsView(EngineSettings settings, Storage storage, IRecordingStore store, TimeProvider time)
	{
		RetentionOutlook? outlook = null;
		try
		{
			var (total, free) = store.Disk();
			outlook = RetentionForecast.Outlook(store.Segments(), time.GetUtcNow().ToUnixTimeMilliseconds(), total, free, new RetentionLimits(settings.RetentionDays, settings.RetentionPercent));
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
		return View(settings, storage, outlook);
	}

	static object View(EngineSettings settings, Storage storage, RetentionOutlook? outlook) => new
	{
		recordingsDir = settings.RecordingsDir,
		defaultDir = storage.Fallback,
		activeDir = storage.Active,
		primaryAvailable = storage.PrimaryAvailable,
		onFallback = storage.OnFallback,
		released = storage.Released,
		retentionDays = settings.RetentionDays,
		retentionPercent = settings.RetentionPercent,
		keepAwake = settings.KeepAwake,
		detectPeople = settings.DetectPeople,
		detectorReady = EnginePaths.Ffmpeg() is not null && EnginePaths.PersonModel() is not null,
		disk = Endpoints.Disk(storage.Active),
		outlook = outlook is null ? null : new { outlook.RecordedBytes, outlook.OldestMs, outlook.BytesPerDay, outlook.DaysThatFit, outlook.LimitedBy, outlook.NextDeletionMs },
		volumes = Storage.Volumes().Select(v => new { v.Name, v.Path, v.TotalBytes, v.FreeBytes, suggested = Path.Combine(v.Path, "Velador") }),
	};

	static IResult SaveSettings(SettingsRequest request, EngineSettings settings, EnginePaths paths, Storage storage, IRecordingStore store, TimeProvider time)
	{
		if (request.RetentionDays is < 1 or > 365 || request.RetentionPercent is < 10 or > 98) return Results.BadRequest(new { error = "Los límites están fuera de rango." });
		var dir = string.IsNullOrWhiteSpace(request.RecordingsDir) ? null : request.RecordingsDir.Trim();
		if (dir is not null)
		{
			if (!Path.IsPathFullyQualified(dir)) return Results.BadRequest(new { error = "La carpeta debe ser una ruta completa." });
			if (!Storage.Available(dir)) return Results.BadRequest(new { error = "No se puede escribir en esa carpeta. ¿El disco está conectado?" });
		}
		settings.RecordingsDir = dir;
		settings.RetentionDays = request.RetentionDays;
		settings.RetentionPercent = request.RetentionPercent;
		settings.KeepAwake = request.KeepAwake;
		if (request.DetectPeople is { } detect) settings.DetectPeople = detect;
		settings.Save(paths);
		storage.Reclaim();
		return Results.Ok(SettingsView(settings, storage, store, time));
	}

	static async Task<IResult> Test(TestRequest request, ISecretStore secrets, HttpClient http, TimeProvider time, CancellationToken ct)
	{
		var password = string.IsNullOrEmpty(request.Password) && request.Id is not null ? secrets.Get(request.Id) : request.Password;
		if (string.IsNullOrWhiteSpace(request.Host) || string.IsNullOrWhiteSpace(request.User) || string.IsNullOrEmpty(password)) return Results.BadRequest(new { error = "Faltan la dirección, el usuario o la contraseña." });
		var camera = new Camera("test", "test", request.Host.Trim(), request.User.Trim());
		var video = await ReachesAsync(camera.Host, camera.RtspPort, ct);
		string? model = null;
		var account = false;
		try
		{
			model = await new OnvifClient(http, camera, password, time).ModelAsync(ct);
			account = true;
		}
		catch (Exception e) when (e is HttpRequestException or OperationCanceledException or System.Xml.XmlException) { }
		return Results.Ok(new { video, account, model });
	}

	static async Task<bool> ReachesAsync(string host, int port, CancellationToken ct)
	{
		using var client = new TcpClient();
		using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
		limit.CancelAfter(TimeSpan.FromSeconds(2));
		try
		{
			await client.ConnectAsync(host, port, limit.Token);
			return true;
		}
		catch (Exception e) when (e is SocketException or OperationCanceledException or ArgumentException)
		{
			return false;
		}
	}

	static IResult SaveCamera(string id, CameraRequest request, EngineSettings settings, EnginePaths paths, ISecretStore secrets, CameraLinks links, TapoLinks tapo)
	{
		if (!Camera.IsValidId(id)) return Results.BadRequest(new { error = "El identificador sólo lleva minúsculas y números." });
		if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Host) || string.IsNullOrWhiteSpace(request.User)) return Results.BadRequest(new { error = "Faltan el nombre, la dirección o el usuario." });
		if (request.Rotation is not (0 or 180)) return Results.BadRequest(new { error = "El giro sólo puede ser 0 o 180." });
		var existing = settings.Cameras.FirstOrDefault(c => c.Id == id);
		if (existing is null && string.IsNullOrEmpty(request.Password)) return Results.BadRequest(new { error = "Falta la contraseña de la cuenta de cámara." });
		if (!string.IsNullOrEmpty(request.Password)) secrets.Set(id, request.Password);
		if (!string.IsNullOrEmpty(request.TapoPassword)) secrets.Set(TapoLinks.SecretId(id), request.TapoPassword);
		tapo.Forget(id);
		var camera = (existing ?? new Camera(id, "", "", "")) with { Name = request.Name.Trim(), Host = request.Host.Trim(), User = request.User.Trim(), Rotation = request.Rotation };
		settings.Cameras.RemoveAll(c => c.Id == id);
		settings.Cameras.Add(camera);
		settings.Save(paths);
		if (existing is not null && existing.Host != camera.Host) camera = camera with { Serial = "" };
		settings.Cameras[^1] = camera;
		settings.Save(paths);
		var streamChanged = existing is null || existing.Host != camera.Host || existing.User != camera.User || !string.IsNullOrEmpty(request.Password);
		if (streamChanged)
		{
			links.Forget(id);
			settings.Revision++;
		}
		return Results.Ok(new { camera.Id, camera.Name, camera.Host, camera.User, camera.Rotation });
	}

	static IResult RemoveCamera(string id, EngineSettings settings, EnginePaths paths, CameraLinks links)
	{
		if (settings.Cameras.RemoveAll(c => c.Id == id) == 0) return Results.NotFound();
		settings.Save(paths);
		links.Forget(id);
		settings.Revision++;
		return Results.NoContent();
	}

	static object ClipView(Clip clip) => new { clip.Id, clip.CameraId, clip.StartMs, clip.EndMs, clip.Title, clip.Bytes, clip.CreatedMs, available = File.Exists(clip.File) };

	static async Task<IResult> AddClip(string id, ClipRequest request, EngineSettings settings, Storage storage, MediaMtxClient media, EngineDatabase database, TimeProvider time, CancellationToken ct)
	{
		if (settings.Cameras.FirstOrDefault(c => c.Id == id) is not { } camera) return Results.NotFound();
		var length = request.EndMs - request.StartMs;
		if (length is < 1000 or > 1_800_000) return Results.BadRequest(new { error = "Un recorte dura entre 1 segundo y 30 minutos." });
		var title = string.IsNullOrWhiteSpace(request.Title) ? $"Recorte de {camera.Name}" : request.Title.Trim();
		var dir = Path.Combine(storage.Active, "clips", id);
		Directory.CreateDirectory(dir);
		var stamp = DateTimeOffset.FromUnixTimeMilliseconds(request.StartMs).ToLocalTime().ToString("yyyy-MM-dd_HH-mm-ss");
		var file = Path.Combine(dir, $"{stamp}_{length / 1000}s.mp4");
		using var upstream = await media.MediaAsync(camera.MediaPath(Camera.MainStream), request.StartMs, length / 1000.0, ct);
		if (!upstream.IsSuccessStatusCode) return Results.NotFound(new { error = "No hay grabación en ese rango." });
		await using (var target = File.Create(file)) await upstream.Content.CopyToAsync(target, ct);
		var clip = database.AddClip(id, new TimeRange(request.StartMs, request.EndMs), title, file, new FileInfo(file).Length, time.GetUtcNow().ToUnixTimeMilliseconds());
		return Results.Ok(ClipView(clip));
	}

	static IResult Reveal(long clipId, EngineDatabase database)
	{
		if (database.Clips().FirstOrDefault(c => c.Id == clipId) is not { } clip || !File.Exists(clip.File)) return Results.NotFound();
		if (OperatingSystem.IsMacOS()) Process.Start(new ProcessStartInfo("/usr/bin/open", ["-R", clip.File]) { UseShellExecute = false })?.Dispose();
		else if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{clip.File}\"") { UseShellExecute = false })?.Dispose();
		return Results.NoContent();
	}
}
