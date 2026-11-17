namespace Velador.Core.Vision;

public sealed record PtzZone(double MinX, double MaxX, double MinY, double MaxY, double HomeX, double HomeY, double? RouteLeftY = null, double? RouteMidX = null, double? RouteMidY = null, double? RouteRightY = null)
{
	public (double X, double Y) RouteLeft => (MinX, Math.Clamp(RouteLeftY ?? MinY + (MaxY - MinY) * 0.35, MinY, MaxY));
	public (double X, double Y) RouteMid => (Math.Clamp(RouteMidX ?? (MinX + MaxX) / 2, MinX + 0.02, MaxX - 0.02), Math.Clamp(RouteMidY ?? MaxY - (MaxY - MinY) * 0.2, MinY, MaxY));
	public (double X, double Y) RouteRight => (MaxX, Math.Clamp(RouteRightY ?? MinY + (MaxY - MinY) * 0.35, MinY, MaxY));

	public double Route(double x)
	{
		var (x0, y0) = RouteLeft;
		var (x1, y1) = RouteMid;
		var (x2, y2) = RouteRight;
		x = Math.Clamp(x, x0 - 0.25, x2 + 0.25);
		var y = y0 * (x - x1) * (x - x2) / ((x0 - x1) * (x0 - x2)) + y1 * (x - x0) * (x - x2) / ((x1 - x0) * (x1 - x2)) + y2 * (x - x0) * (x - x1) / ((x2 - x0) * (x2 - x1));
		return Math.Clamp(y, MinY, MaxY);
	}

	public const double MinSpan = 0.05;

	public bool IsValid =>
		new[] { MinX, MaxX, MinY, MaxY, HomeX, HomeY, RouteLeftY ?? 0, RouteMidX ?? 0, RouteMidY ?? 0, RouteRightY ?? 0 }.All(v => double.IsFinite(v) && v is >= -1 and <= 1)
		&& MaxX - MinX >= MinSpan && MaxY - MinY >= MinSpan
		&& HomeX >= MinX && HomeX <= MaxX && HomeY >= MinY && HomeY <= MaxY;

	public bool Contains(double x, double y, double slack = 0.01) =>
		x >= MinX - slack && x <= MaxX + slack && y >= MinY - slack && y <= MaxY + slack;

	public (double Vx, double Vy) Limit(double x, double y, double vx, double vy, double lookAheadS = 0.25)
	{
		var nextX = x + vx * lookAheadS;
		var nextY = y + vy * lookAheadS;
		if ((vx > 0 && nextX > MaxX) || (vx < 0 && nextX < MinX)) vx = 0;
		if ((vy > 0 && nextY > MaxY) || (vy < 0 && nextY < MinY)) vy = 0;
		return (vx, vy);
	}

	public (double X, double Y) Clamp(double x, double y) => (Math.Clamp(x, MinX, MaxX), Math.Clamp(y, MinY, MaxY));
}

public sealed record SpeedCurve(IReadOnlyList<double> Levels, IReadOnlyList<double> Rates)
{
	public double Top => Rates.Count == 0 ? 0 : Rates.Max();

	public bool IsProportional => Rates.Count >= 2 && Rates[0] < Top * 0.75;

	public double Rate(double command)
	{
		var c = Math.Abs(command);
		if (Levels.Count == 0 || c <= 0) return 0;
		double l0 = 0, r0 = 0;
		for (var i = 0; i < Levels.Count; i++)
		{
			if (c <= Levels[i]) return r0 + (Rates[i] - r0) * (c - l0) / (Levels[i] - l0);
			(l0, r0) = (Levels[i], Rates[i]);
		}
		return r0;
	}

	public double Command(double rate)
	{
		var wanted = Math.Abs(rate);
		if (Levels.Count == 0 || wanted <= 0) return 0;
		double l0 = 0, r0 = 0;
		for (var i = 0; i < Levels.Count; i++)
		{
			if (Rates[i] >= wanted && Rates[i] > r0) return l0 + (Levels[i] - l0) * (wanted - r0) / (Rates[i] - r0);
			if (Rates[i] > r0) (l0, r0) = (Levels[i], Rates[i]);
		}
		return l0;
	}
}
