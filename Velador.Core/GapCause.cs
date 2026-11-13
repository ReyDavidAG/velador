namespace Velador.Core;

public enum GapCause
{
	Unknown,
	Sleep,
	Camera,
	Network,
	Engine,
	Power,
	Update,
	Disk,
	Paused,
	Privacy,
	Credentials,
}

public static class GapCauseExtensions
{
	public static string ToCode(this GapCause cause) => cause.ToString().ToLowerInvariant();

	public static GapCause ParseCause(string? code) =>
		Enum.TryParse<GapCause>(code, ignoreCase: true, out var cause) ? cause : GapCause.Unknown;
}
