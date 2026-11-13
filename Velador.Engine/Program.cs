using Velador.Engine;
using Velador.Engine.Api;
using Velador.Engine.Continuity;
using Velador.Engine.MediaMtx;
using Velador.Engine.Power;
using Velador.Engine.Secrets;
using Velador.Engine.Sqlite;

var paths = EnginePaths.Resolve();
var settings = EngineSettings.Load(paths);
if (int.TryParse(Environment.GetEnvironmentVariable("VELADOR_PORT"), out var port)) settings.Port = port;
var session = new Session(paths, settings.Port);

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, WebRootPath = Environment.GetEnvironmentVariable("VELADOR_WEB") });
builder.WebHost.UseUrls($"http://127.0.0.1:{settings.Port}");
builder.Logging.AddSimpleConsole(o => o.SingleLine = true);
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
builder.Services.AddSingleton<MediaSupervisor>();
builder.Services.AddSingleton<ContinuityService>();
builder.Services.AddHostedService(s => s.GetRequiredService<MediaSupervisor>());
builder.Services.AddHostedService(s => s.GetRequiredService<ContinuityService>());
builder.Services.AddSingleton<IRecordingStore>(new RecordingStore(paths.Recordings(settings)));
builder.Services.AddHostedService<RetentionService>();

var app = builder.Build();
app.UseSession(session);
if (app.Environment.WebRootPath is not null)
{
	app.UseDefaultFiles();
	app.UseStaticFiles();
}
app.MapEngine();
app.Run();

public partial class Program;
