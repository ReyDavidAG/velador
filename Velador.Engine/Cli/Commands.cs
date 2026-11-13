using System.Diagnostics;
using System.Security;
using Velador.Core;
using Velador.Engine.Secrets;

namespace Velador.Engine.Cli;

public static class Commands
{
	public const string Label = "com.reydavidag.velador.engine";

	public static int? Run(string[] args, EnginePaths paths)
	{
		return args switch
		{
			["install"] => Install(paths),
			["uninstall"] => Uninstall(),
			["camera", "set", var id, var name, var host, var user] => SetCamera(paths, id, name, host, user),
			["camera", ..] => Fail("Uso: camera set <id> <nombre> <host> <usuario>  (la contraseña se lee de la entrada estándar)"),
			_ => null,
		};
	}

	static int SetCamera(EnginePaths paths, string id, string name, string host, string user)
	{
		if (!Camera.IsValidId(id)) return Fail("El id sólo lleva minúsculas y números, hasta 24.");
		var secret = Console.In.ReadLine();
		if (string.IsNullOrEmpty(secret)) return Fail("Falta la contraseña en la entrada estándar.");
		var settings = EngineSettings.Load(paths);
		settings.Cameras.RemoveAll(c => c.Id == id);
		settings.Cameras.Add(new Camera(id, name, host, user));
		settings.Save(paths);
		SecretStores.ForThisPlatform().Set(id, secret);
		Console.WriteLine($"Cámara «{name}» guardada; la contraseña quedó en el llavero del sistema.");
		return 0;
	}

	static int Install(EnginePaths paths)
	{
		if (!OperatingSystem.IsMacOS()) return Fail("Por ahora el servicio sólo se instala en macOS.");
		var program = Environment.ProcessPath!;
		var log = Path.Combine(paths.Home, "engine.log");
		var plist = PlistPath();
		Directory.CreateDirectory(Path.GetDirectoryName(plist)!);
		File.WriteAllText(plist, $"""
			<?xml version="1.0" encoding="UTF-8"?>
			<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
			<plist version="1.0">
			<dict>
				<key>Label</key><string>{Label}</string>
				<key>ProgramArguments</key><array><string>{Xml(program)}</string></array>
				<key>RunAtLoad</key><true/>
				<key>KeepAlive</key><true/>
				<key>ProcessType</key><string>Interactive</string>
				<key>StandardOutPath</key><string>{Xml(log)}</string>
				<key>StandardErrorPath</key><string>{Xml(log)}</string>
			</dict>
			</plist>

			""");
		var domain = $"gui/{Uid()}";
		Launchctl("bootout", $"{domain}/{Label}");
		if (Launchctl("bootstrap", domain, plist) != 0) return Fail("launchctl no pudo cargar el servicio.");
		Console.WriteLine($"Motor instalado: {program}");
		return 0;
	}

	static int Uninstall()
	{
		if (!OperatingSystem.IsMacOS()) return Fail("Por ahora el servicio sólo se instala en macOS.");
		Launchctl("bootout", $"gui/{Uid()}/{Label}");
		File.Delete(PlistPath());
		Console.WriteLine("Motor desinstalado. Las grabaciones y los ajustes no se tocaron.");
		return 0;
	}

	static string PlistPath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", $"{Label}.plist");

	static string Uid()
	{
		using var process = Process.Start(new ProcessStartInfo("/usr/bin/id", ["-u"]) { RedirectStandardOutput = true })!;
		var uid = process.StandardOutput.ReadToEnd().Trim();
		process.WaitForExit();
		return uid;
	}

	static int Launchctl(params string[] args)
	{
		using var process = Process.Start(new ProcessStartInfo("/bin/launchctl", args) { RedirectStandardOutput = true, RedirectStandardError = true })!;
		process.WaitForExit();
		return process.ExitCode;
	}

	static string Xml(string text) => SecurityElement.Escape(text);

	static int Fail(string message)
	{
		Console.Error.WriteLine(message);
		return 1;
	}
}
