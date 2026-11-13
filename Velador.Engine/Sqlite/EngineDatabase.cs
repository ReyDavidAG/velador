using Microsoft.Data.Sqlite;
using Velador.Core;

namespace Velador.Engine.Sqlite;

public sealed class EngineDatabase : IDisposable
{
	readonly SqliteConnection _connection;
	readonly Lock _lock = new();

	public EngineDatabase(string dataSource)
	{
		_connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dataSource }.ToString());
		_connection.Open();
		Run("PRAGMA journal_mode = WAL");
		Run("CREATE TABLE IF NOT EXISTS gaps (id INTEGER PRIMARY KEY, camera_id TEXT NOT NULL, start_ms INTEGER NOT NULL, end_ms INTEGER NULL, cause TEXT NOT NULL)");
		Run("CREATE INDEX IF NOT EXISTS gaps_camera_start ON gaps (camera_id, start_ms)");
		Run("CREATE TABLE IF NOT EXISTS engine_state (key TEXT PRIMARY KEY, value INTEGER NOT NULL)");
		Run("CREATE TABLE IF NOT EXISTS events (id INTEGER PRIMARY KEY, camera_id TEXT NOT NULL, start_ms INTEGER NOT NULL, end_ms INTEGER NULL, kind TEXT NOT NULL)");
		Run("CREATE INDEX IF NOT EXISTS events_camera_start ON events (camera_id, start_ms)");
		Run("CREATE TABLE IF NOT EXISTS clips (id INTEGER PRIMARY KEY, camera_id TEXT NOT NULL, start_ms INTEGER NOT NULL, end_ms INTEGER NOT NULL, title TEXT NOT NULL, file TEXT NOT NULL, bytes INTEGER NOT NULL, created_ms INTEGER NOT NULL)");
	}

	public void OpenGap(string cameraId, RecordedGap gap)
	{
		lock (_lock)
		{
			if (Scalar("SELECT COUNT(*) FROM gaps WHERE camera_id = $c AND end_ms IS NULL", ("$c", cameraId)) > 0) return;
			Run("INSERT INTO gaps (camera_id, start_ms, end_ms, cause) VALUES ($c, $s, NULL, $k)", ("$c", cameraId), ("$s", gap.StartMs), ("$k", gap.Cause.ToCode()));
		}
	}

	public void CloseGap(string cameraId, long endMs)
	{
		lock (_lock) Run("UPDATE gaps SET end_ms = MAX($e, start_ms) WHERE camera_id = $c AND end_ms IS NULL", ("$c", cameraId), ("$e", endMs));
	}

	public RecordedGap? OpenGapOf(string cameraId) => Gaps("camera_id = $c AND end_ms IS NULL", ("$c", cameraId)).FirstOrDefault();

	public IReadOnlyList<RecordedGap> GapsIn(string cameraId, TimeRange window) =>
		Gaps("camera_id = $c AND start_ms < $e AND (end_ms IS NULL OR end_ms > $s)", ("$c", cameraId), ("$s", window.StartMs), ("$e", window.EndMs));

	public long? LastGapEnd(string cameraId)
	{
		lock (_lock)
		{
			var value = Scalar("SELECT COALESCE(MAX(end_ms), -1) FROM gaps WHERE camera_id = $c", ("$c", cameraId));
			return value < 0 ? null : value;
		}
	}

	public const long EventJoinMs = 10_000;

	public void OpenEvent(string cameraId, EventKind kind, long nowMs)
	{
		lock (_lock)
		{
			if (Scalar("SELECT COUNT(*) FROM events WHERE camera_id = $c AND kind = $k AND end_ms IS NULL", ("$c", cameraId), ("$k", kind.ToCode())) > 0) return;
			Run("UPDATE events SET end_ms = NULL WHERE id = (SELECT id FROM events WHERE camera_id = $c AND kind = $k AND end_ms >= $r ORDER BY start_ms DESC LIMIT 1)", ("$c", cameraId), ("$k", kind.ToCode()), ("$r", nowMs - EventJoinMs));
			if (Scalar("SELECT changes()") > 0) return;
			Run("INSERT INTO events (camera_id, start_ms, end_ms, kind) VALUES ($c, $s, NULL, $k)", ("$c", cameraId), ("$s", nowMs), ("$k", kind.ToCode()));
		}
	}

	public void CloseEvent(string cameraId, EventKind kind, long nowMs)
	{
		lock (_lock) Run("UPDATE events SET end_ms = MAX($e, start_ms + 1000) WHERE camera_id = $c AND kind = $k AND end_ms IS NULL", ("$c", cameraId), ("$k", kind.ToCode()), ("$e", nowMs));
	}

	public void CloseEvents(string cameraId, long nowMs)
	{
		lock (_lock) Run("UPDATE events SET end_ms = MAX($e, start_ms + 1000) WHERE camera_id = $c AND end_ms IS NULL", ("$c", cameraId), ("$e", nowMs));
	}

	public void CloseEventsOlderThan(string cameraId, long startedBeforeMs, long nowMs)
	{
		lock (_lock) Run("UPDATE events SET end_ms = $e WHERE camera_id = $c AND end_ms IS NULL AND start_ms < $b", ("$c", cameraId), ("$e", nowMs), ("$b", startedBeforeMs));
	}

	public IReadOnlyList<CameraEvent> EventsIn(string cameraId, TimeRange window, int limit = 2000)
	{
		lock (_lock)
		{
			using var command = Command("SELECT id, start_ms, end_ms, kind FROM events WHERE camera_id = $c AND start_ms < $e AND (end_ms IS NULL OR end_ms > $s) ORDER BY start_ms LIMIT $l", ("$c", cameraId), ("$s", window.StartMs), ("$e", window.EndMs), ("$l", limit));
			using var reader = command.ExecuteReader();
			var events = new List<CameraEvent>();
			while (reader.Read())
				events.Add(new CameraEvent(reader.GetInt64(0), cameraId, reader.GetInt64(1), reader.IsDBNull(2) ? null : reader.GetInt64(2), Enum.Parse<EventKind>(reader.GetString(3), true)));
			return events;
		}
	}

	public CameraEvent? LastEvent(string cameraId)
	{
		lock (_lock)
		{
			using var command = Command("SELECT id, start_ms, end_ms, kind FROM events WHERE camera_id = $c ORDER BY start_ms DESC LIMIT 1", ("$c", cameraId));
			using var reader = command.ExecuteReader();
			return reader.Read() ? new CameraEvent(reader.GetInt64(0), cameraId, reader.GetInt64(1), reader.IsDBNull(2) ? null : reader.GetInt64(2), Enum.Parse<EventKind>(reader.GetString(3), true)) : null;
		}
	}

	public Clip AddClip(string cameraId, TimeRange range, string title, string file, long bytes, long createdMs)
	{
		lock (_lock)
		{
			Run("INSERT INTO clips (camera_id, start_ms, end_ms, title, file, bytes, created_ms) VALUES ($c, $s, $e, $t, $f, $b, $n)", ("$c", cameraId), ("$s", range.StartMs), ("$e", range.EndMs), ("$t", title), ("$f", file), ("$b", bytes), ("$n", createdMs));
			return new Clip(Scalar("SELECT last_insert_rowid()"), cameraId, range.StartMs, range.EndMs, title, file, bytes, createdMs);
		}
	}

	public IReadOnlyList<Clip> Clips()
	{
		lock (_lock)
		{
			using var command = Command("SELECT id, camera_id, start_ms, end_ms, title, file, bytes, created_ms FROM clips ORDER BY created_ms DESC");
			using var reader = command.ExecuteReader();
			var clips = new List<Clip>();
			while (reader.Read()) clips.Add(new Clip(reader.GetInt64(0), reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetString(4), reader.GetString(5), reader.GetInt64(6), reader.GetInt64(7)));
			return clips;
		}
	}

	public void RemoveClip(long id)
	{
		lock (_lock) Run("DELETE FROM clips WHERE id = $i", ("$i", id));
	}

	public long? State(string key)
	{
		lock (_lock)
		{
			using var command = Command("SELECT value FROM engine_state WHERE key = $k", ("$k", key));
			return command.ExecuteScalar() is long value ? value : null;
		}
	}

	public void SetState(string key, long value)
	{
		lock (_lock) Run("INSERT INTO engine_state (key, value) VALUES ($k, $v) ON CONFLICT (key) DO UPDATE SET value = excluded.value", ("$k", key), ("$v", value));
	}

	List<RecordedGap> Gaps(string where, params (string, object)[] args)
	{
		lock (_lock)
		{
			using var command = Command($"SELECT start_ms, end_ms, cause FROM gaps WHERE {where} ORDER BY start_ms", args);
			using var reader = command.ExecuteReader();
			var gaps = new List<RecordedGap>();
			while (reader.Read())
				gaps.Add(new RecordedGap(reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetInt64(1), GapCauseExtensions.ParseCause(reader.GetString(2))));
			return gaps;
		}
	}

	long Scalar(string sql, params (string, object)[] args)
	{
		using var command = Command(sql, args);
		return (long)command.ExecuteScalar()!;
	}

	void Run(string sql, params (string, object)[] args)
	{
		using var command = Command(sql, args);
		command.ExecuteNonQuery();
	}

	SqliteCommand Command(string sql, params (string, object)[] args)
	{
		var command = _connection.CreateCommand();
		command.CommandText = sql;
		foreach (var (name, value) in args) command.Parameters.AddWithValue(name, value);
		return command;
	}

	public void Dispose() => _connection.Dispose();
}
