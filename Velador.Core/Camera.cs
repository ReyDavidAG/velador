namespace Velador.Core;

public sealed record Camera(string Id, string Name, string Host, string User, int RtspPort = 554, string MainPath = "stream1", string MinorPath = "stream2", int OnvifPort = 2020)
{
	public const string MainStream = "main";
	public const string MinorStream = "minor";

	public string MediaPath(string stream) => $"{Id}{stream}";

	public static bool IsValidId(string id) => id.Length is > 0 and <= 24 && id.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9');
}
