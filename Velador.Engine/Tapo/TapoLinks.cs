using Velador.Engine.Secrets;

namespace Velador.Engine.Tapo;

public sealed class TapoLinks(EngineSettings settings, ISecretStore secrets) : IDisposable
{
	readonly Dictionary<string, (string Host, TapoClient Client)> _clients = [];
	readonly Lock _lock = new();

	public static string SecretId(string cameraId) => $"{cameraId}tapo";

	public TapoClient? For(string cameraId)
	{
		lock (_lock)
		{
			var camera = settings.Cameras.FirstOrDefault(c => c.Id == cameraId);
			if (camera is null) return null;
			if (_clients.TryGetValue(cameraId, out var known) && known.Host == camera.Host) return known.Client;
			known.Client?.Dispose();
			var password = secrets.Get(SecretId(cameraId)) ?? secrets.Get(cameraId);
			if (string.IsNullOrEmpty(password)) return null;
			var client = new TapoClient(camera.Host, password);
			_clients[cameraId] = (camera.Host, client);
			return client;
		}
	}

	public void Forget(string cameraId)
	{
		lock (_lock)
		{
			if (_clients.Remove(cameraId, out var known)) known.Client.Dispose();
		}
	}

	public void Dispose()
	{
		lock (_lock)
		{
			foreach (var (_, client) in _clients.Values) client.Dispose();
			_clients.Clear();
		}
	}
}
