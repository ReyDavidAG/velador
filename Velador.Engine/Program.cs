using Velador.Engine;
using Velador.Engine.Api;
using Velador.Engine.Cli;
using Velador.Engine.Continuity;
using Velador.Engine.MediaMtx;
using Velador.Engine.Onvif;
using Velador.Engine.Power;
using Velador.Engine.Secrets;
using Velador.Engine.Sqlite;

var paths = EnginePaths.Resolve();
if (Commands.Run(args, paths) is { } exit) return exit;
var settings = EngineSettings.Load(paths);
if (int.TryParse(Environment.GetEnvironmentVariable("VELADOR_PORT"), out var port)) settings.Port = port;
var session = new Session(paths, settings.Port);

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, WebRootPath = EnginePaths.WebRoot() });
builder.WebHost.UseUrls($"http://127.0.0.1:{settings.Port}");
builder.Logging.AddSimpleConsole(o => o.SingleLine = true);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(5));
builder.Services.AddSingleton(paths);
builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(session);
builder.Services.AddSingleton(MediaPorts.From(settings.Port));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new EngineDatabase(paths.Database));
builder.Services.AddSingleton(SecretStores.ForThisPlatform());
builder.Services.AddSingleton(PowerGuards.ForThisPlatform());
builder.Services.AddSingleton<IReachability, NetworkReachability>();
builder.Services.AddSingleton(new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan });
builder.Services.AddSingleton<MediaMtxClient>();
builder.Services.AddSingleton<MediaCache>();
builder.Services.AddSingleton<MediaSupervisor>();
builder.Services.AddSingleton<ContinuityService>();
builder.Services.AddHostedService(s => s.GetRequiredService<MediaSupervisor>());
builder.Services.AddHostedService(s => s.GetRequiredService<ContinuityService>());
builder.Services.AddSingleton<IRecordingStore>(new RecordingStore(paths.Recordings(settings)));
builder.Services.AddHostedService<RetentionService>();
builder.Services.AddSingleton<CameraLinks>();
builder.Services.AddHostedService<EventService>();

var app = builder.Build();
app.UseSession(session);
if (app.Environment.WebRootPath is not null)
{
	app.UseDefaultFiles();
	app.UseStaticFiles();
}
app.MapEngine();
app.Run();
return 0;

public partial class Program;
