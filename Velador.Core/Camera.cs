namespace Velador.Core;

public sealed record Camera(string Id, string Name, string Host, string User, int RtspPort = 554, string MainPath = "stream1", string MinorPath = "stream2", int OnvifPort = 2020, int Rotation = 0, string Serial = "")
{
	public const string MainStream = "main";
	public const string MinorStream = "minor";

	public string MediaPath(string stream) => $"{Id}{stream}";

	public static (string Host, int OnvifPort)? Relocated(Camera camera, IEnumerable<(string Host, int OnvifPort, string Serial)> seen)
	{
		if (camera.Serial.Length == 0) return null;
		var matches = seen.Where(s => s.Serial == camera.Serial).ToList();
		if (matches.Count != 1 || matches[0].Host == camera.Host) return null;
		return (matches[0].Host, matches[0].OnvifPort);
	}

	public static bool IsValidId(string id) => id.Length is > 0 and <= 24 && id.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9');
}
