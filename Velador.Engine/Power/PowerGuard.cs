using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Velador.Engine.Power;

public interface IPowerGuard : IDisposable
{
	void KeepAwake(bool on);
}

public static class PowerGuards
{
	public static IPowerGuard ForThisPlatform() =>
		OperatingSystem.IsMacOS() ? new MacPowerGuard() : OperatingSystem.IsWindows() ? new WindowsPowerGuard() : OperatingSystem.IsLinux() ? new LinuxPowerGuard() : new NoPowerGuard();
}

public sealed class NoPowerGuard : IPowerGuard
{
	public void KeepAwake(bool on) { }

	public void Dispose() { }
}

[SupportedOSPlatform("macos")]
public sealed class MacPowerGuard() : ChildPowerGuard("/usr/bin/caffeinate", ["-i", "-w", Environment.ProcessId.ToString()]);

[SupportedOSPlatform("linux")]
public sealed class LinuxPowerGuard() : ChildPowerGuard("systemd-inhibit", ["--what=sleep", "--who=Velador", "--why=Grabando la cámara", "--mode=block", "tail", $"--pid={Environment.ProcessId}", "-f", "/dev/null"]);

public abstract class ChildPowerGuard(string program, string[] args) : IPowerGuard
{
	Process? _caffeinate;

	public void KeepAwake(bool on)
	{
		if (on == _caffeinate is { HasExited: false }) return;
		if (!on)
		{
			Dispose();
			return;
		}
		try
		{
			_caffeinate = Process.Start(new ProcessStartInfo(program, args) { UseShellExecute = false });
		}
		catch (System.ComponentModel.Win32Exception) { }
	}

	public void Dispose()
	{
		try
		{
			if (_caffeinate is { HasExited: false }) _caffeinate.Kill();
		}
		catch (InvalidOperationException) { }
		_caffeinate = null;
	}
}

[SupportedOSPlatform("windows")]
public sealed class WindowsPowerGuard : IPowerGuard
{
	const int SystemRequired = 1;
	const int SimpleString = 1;
	IntPtr _request;

	public void KeepAwake(bool on)
	{
		if (on == (_request != IntPtr.Zero)) return;
		if (!on)
		{
			Dispose();
			return;
		}
		var context = new ReasonContext { Version = 0, Flags = SimpleString, Reason = "Velador está de guardia: grabando la cámara" };
		_request = PowerCreateRequest(ref context);
		if (_request != IntPtr.Zero) PowerSetRequest(_request, SystemRequired);
	}

	public void Dispose()
	{
		if (_request == IntPtr.Zero) return;
		PowerClearRequest(_request, SystemRequired);
		CloseHandle(_request);
		_request = IntPtr.Zero;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	struct ReasonContext
	{
		public uint Version;
		public uint Flags;
		[MarshalAs(UnmanagedType.LPWStr)]
		public string Reason;
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern IntPtr PowerCreateRequest(ref ReasonContext context);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern bool PowerSetRequest(IntPtr request, int type);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern bool PowerClearRequest(IntPtr request, int type);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern bool CloseHandle(IntPtr handle);
}
