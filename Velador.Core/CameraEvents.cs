using System.Xml.Linq;

namespace Velador.Core;

public enum EventKind
{
	Motion,
	Person,
	Tamper,
}

public sealed record CameraSignal(EventKind Kind, bool Active);

public sealed record CameraEvent(long Id, string CameraId, long StartMs, long? EndMs, EventKind Kind);

public static class OnvifEvents
{
	static readonly (string Topic, string Item, EventKind Kind)[] Known =
	[
		("CellMotionDetector/Motion", "IsMotion", EventKind.Motion),
		("PeopleDetector/People", "IsPeople", EventKind.Person),
		("TamperDetector/Tamper", "IsTamper", EventKind.Tamper),
	];

	public static IReadOnlyList<CameraSignal> Parse(string pullMessagesResponse)
	{
		XDocument doc;
		try
		{
			doc = XDocument.Parse(pullMessagesResponse);
		}
		catch (System.Xml.XmlException)
		{
			return [];
		}
		var signals = new List<CameraSignal>();
		foreach (var message in doc.Descendants().Where(e => e.Name.LocalName == "NotificationMessage"))
		{
			var topic = message.Descendants().FirstOrDefault(e => e.Name.LocalName == "Topic")?.Value ?? "";
			foreach (var (suffix, item, kind) in Known)
			{
				if (!topic.EndsWith(suffix, StringComparison.Ordinal)) continue;
				var value = message.Descendants()
					.Where(e => e.Name.LocalName == "SimpleItem" && (string?)e.Attribute("Name") == item)
					.Select(e => (string?)e.Attribute("Value"))
					.FirstOrDefault();
				signals.Add(new CameraSignal(kind, value is null || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1"));
			}
		}
		return signals;
	}

	public static string ToCode(this EventKind kind) => kind.ToString().ToLowerInvariant();
}
