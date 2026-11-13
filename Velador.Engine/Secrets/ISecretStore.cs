using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Velador.Engine.Secrets;

public interface ISecretStore
{
	string? Get(string cameraId);
	void Set(string cameraId, string secret);
}

public static class SecretStores
{
	public const string Service = "Velador";

	public static ISecretStore ForThisPlatform() =>
		new EnvironmentFirst(OperatingSystem.IsWindows() ? new WindowsCredentialStore() : OperatingSystem.IsMacOS() ? new KeychainSecretStore() : new MemorySecretStore());
}

public sealed class MemorySecretStore : ISecretStore
{
	readonly Dictionary<string, string> _secrets = [];

	public string? Get(string cameraId) => _secrets.GetValueOrDefault(cameraId);

	public void Set(string cameraId, string secret) => _secrets[cameraId] = secret;
}

public sealed class EnvironmentFirst(ISecretStore inner) : ISecretStore
{
	public string? Get(string cameraId) =>
		Environment.GetEnvironmentVariable($"VELADOR_CAMERA_PASSWORD_{cameraId.ToUpperInvariant()}") ?? inner.Get(cameraId);

	public void Set(string cameraId, string secret) => inner.Set(cameraId, secret);
}

[SupportedOSPlatform("macos")]
public sealed class KeychainSecretStore : ISecretStore
{
	public string? Get(string cameraId)
	{
		var info = new ProcessStartInfo("/usr/bin/security", ["find-generic-password", "-s", SecretStores.Service, "-a", cameraId, "-w"])
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		};
		using var process = Process.Start(info)!;
		var output = process.StandardOutput.ReadToEnd();
		process.WaitForExit();
		return process.ExitCode == 0 ? output.TrimEnd('\n') : null;
	}

	public void Set(string cameraId, string secret)
	{
		var info = new ProcessStartInfo("/usr/bin/security", ["-i"]) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
		using var process = Process.Start(info)!;
		process.StandardInput.WriteLine($"add-generic-password -U -s {Quote(SecretStores.Service)} -a {Quote(cameraId)} -w {Quote(secret)}");
		process.StandardInput.Close();
		process.WaitForExit();
		if (process.ExitCode != 0) throw new InvalidOperationException("The keychain rejected the secret");
	}

	static string Quote(string value) => $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
}

[SupportedOSPlatform("windows")]
public sealed class WindowsCredentialStore : ISecretStore
{
	const int Generic = 1;
	const int LocalMachine = 2;

	public string? Get(string cameraId)
	{
		if (!CredRead(Target(cameraId), Generic, 0, out var pointer)) return null;
		try
		{
			var credential = Marshal.PtrToStructure<Credential>(pointer);
			return credential.BlobSize == 0 ? "" : Marshal.PtrToStringUni(credential.Blob, credential.BlobSize / 2);
		}
		finally
		{
			CredFree(pointer);
		}
	}

	public void Set(string cameraId, string secret)
	{
		var blob = Encoding.Unicode.GetBytes(secret);
		var handle = GCHandle.Alloc(blob, GCHandleType.Pinned);
		try
		{
			var credential = new Credential
			{
				Type = Generic,
				TargetName = Target(cameraId),
				UserName = cameraId,
				Blob = handle.AddrOfPinnedObject(),
				BlobSize = blob.Length,
				Persist = LocalMachine,
			};
			if (!CredWrite(ref credential, 0)) throw new InvalidOperationException("Credential Manager rejected the secret");
		}
		finally
		{
			handle.Free();
		}
	}

	static string Target(string cameraId) => $"{SecretStores.Service}:{cameraId}";

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	struct Credential
	{
		public int Flags;
		public int Type;
		public string TargetName;
		public string? Comment;
		public long LastWritten;
		public int BlobSize;
		public IntPtr Blob;
		public int Persist;
		public int AttributeCount;
		public IntPtr Attributes;
		public string? TargetAlias;
		public string UserName;
	}

	[DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
	static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

	[DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
	static extern bool CredWrite(ref Credential credential, int flags);

	[DllImport("advapi32.dll")]
	static extern void CredFree(IntPtr buffer);
}
