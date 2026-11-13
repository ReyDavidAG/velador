using System.Security.Cryptography;

namespace Velador.Engine.Api;

public sealed class Session
{
	public const string Cookie = "velador_session";
	public const string Header = "X-Velador-Token";

	readonly string _token;
	readonly HashSet<string> _hosts;

	public Session(EnginePaths paths, int port)
	{
		if (!File.Exists(paths.Token) || File.ReadAllText(paths.Token).Trim().Length < 32)
		{
			File.WriteAllText(paths.Token, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
			if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(paths.Token, UnixFileMode.UserRead | UnixFileMode.UserWrite);
		}
		_token = File.ReadAllText(paths.Token).Trim();
		_hosts = [$"127.0.0.1:{port}", $"localhost:{port}"];
	}

	public bool HostAllowed(string? host) => host is not null && _hosts.Contains(host);

	public bool OriginAllowed(string? origin) => origin is null || _hosts.Any(h => origin == $"http://{h}");

	public bool Matches(string? candidate) =>
		candidate is not null
		&& CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(candidate), System.Text.Encoding.UTF8.GetBytes(_token));

	public string Token => _token;
}

public static class SessionMiddleware
{
	public static IApplicationBuilder UseSession(this IApplicationBuilder app, Session session) =>
		app.Use(async (context, next) =>
		{
			var request = context.Request;
			if (!session.HostAllowed(request.Host.Value))
			{
				context.Response.StatusCode = StatusCodes.Status421MisdirectedRequest;
				return;
			}
			var writes = !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method);
			if (writes && !session.OriginAllowed(request.Headers.Origin.FirstOrDefault()))
			{
				context.Response.StatusCode = StatusCodes.Status403Forbidden;
				return;
			}
			if (request.Path == "/" && session.Matches(request.Query["t"].FirstOrDefault()))
			{
				context.Response.Cookies.Append(Session.Cookie, session.Token, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/" });
				context.Response.Redirect("/");
				return;
			}
			var authorized = session.Matches(request.Cookies[Session.Cookie]) || session.Matches(request.Headers[Session.Header].FirstOrDefault());
			if (!authorized && request.Path.StartsWithSegments("/api"))
			{
				context.Response.StatusCode = StatusCodes.Status401Unauthorized;
				return;
			}
			await next();
		});
}
