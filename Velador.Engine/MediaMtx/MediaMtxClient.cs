using System.Globalization;
using System.Text.Json;
using Velador.Core;

namespace Velador.Engine.MediaMtx;

public sealed record MediaPathState(string Name, bool Online, long InboundBytes);

public sealed class MediaMtxClient(HttpClient http, MediaPorts ports)
{
	string Api => $"http://127.0.0.1:{ports.Api}";
	string Playback => $"http://127.0.0.1:{ports.Playback}";
	public string WebRtc => $"http://127.0.0.1:{ports.WebRtc}";

	public async Task<IReadOnlyList<MediaPathState>?> PathsAsync(CancellationToken ct)
	{
		using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
		limit.CancelAfter(TimeSpan.FromSeconds(2));
		try
		{
			using var response = await http.GetAsync($"{Api}/v3/paths/list?itemsPerPage=500", limit.Token);
			if (!response.IsSuccessStatusCode) return null;
			using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(limit.Token));
			var list = new List<MediaPathState>();
			foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray())
			{
				var online = Bool(item, "online") ?? Bool(item, "ready") ?? false;
				var bytes = Long(item, "inboundBytes") ?? Long(item, "bytesReceived") ?? 0;
				list.Add(new MediaPathState(item.GetProperty("name").GetString() ?? "", online, bytes));
			}
			return list;
		}
		catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException)
		{
			ct.ThrowIfCancellationRequested();
			return null;
		}
	}

	public async Task<IReadOnlyList<TimeRange>> SpansAsync(string path, TimeRange window, CancellationToken ct)
	{
		var url = $"{Playback}/list?path={Uri.EscapeDataString(path)}&start={Stamp(window.StartMs)}&end={Stamp(window.EndMs)}";
		using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
		limit.CancelAfter(TimeSpan.FromSeconds(5));
		try
		{
			using var response = await http.GetAsync(url, limit.Token);
			if (!response.IsSuccessStatusCode) return [];
			using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(limit.Token));
			var spans = new List<TimeRange>();
			foreach (var item in doc.RootElement.EnumerateArray())
			{
				var start = DateTimeOffset.Parse(item.GetProperty("start").GetString()!, CultureInfo.InvariantCulture).ToUnixTimeMilliseconds();
				spans.Add(new TimeRange(start, start + (long)(item.GetProperty("duration").GetDouble() * 1000)));
			}
			return spans;
		}
		catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException)
		{
			ct.ThrowIfCancellationRequested();
			return [];
		}
	}

	public Task<HttpResponseMessage> MediaAsync(string path, long startMs, double seconds, CancellationToken ct) =>
		http.GetAsync(
			$"{Playback}/get?path={Uri.EscapeDataString(path)}&start={Stamp(startMs)}&duration={seconds.ToString("0.###", CultureInfo.InvariantCulture)}&format=mp4",
			HttpCompletionOption.ResponseHeadersRead,
			ct);

	public async Task<bool> RecordAsync(string path, bool record, CancellationToken ct)
	{
		using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
		limit.CancelAfter(TimeSpan.FromSeconds(3));
		try
		{
			using var content = new StringContent(record ? "{\"record\":true}" : "{\"record\":false}", System.Text.Encoding.UTF8, "application/json");
			using var response = await http.PatchAsync($"{Api}/v3/config/paths/patch/{Uri.EscapeDataString(path)}", content, limit.Token);
			return response.IsSuccessStatusCode;
		}
		catch (Exception e) when (e is HttpRequestException or OperationCanceledException)
		{
			ct.ThrowIfCancellationRequested();
			return false;
		}
	}

	public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => http.SendAsync(request, ct);

	static string Stamp(long ms) => Uri.EscapeDataString(DateTimeOffset.FromUnixTimeMilliseconds(ms).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));

	static bool? Bool(JsonElement item, string name) =>
		item.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

	static long? Long(JsonElement item, string name) =>
		item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : null;
}
