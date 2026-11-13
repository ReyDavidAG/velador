using Microsoft.Data.Sqlite;
using Velador.Core;
using Velador.Core.Vision;

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
		Run("CREATE TABLE IF NOT EXISTS people (id INTEGER PRIMARY KEY, camera_id TEXT NOT NULL, day TEXT NOT NULL, descriptor BLOB NOT NULL, first_ms INTEGER NOT NULL, last_ms INTEGER NOT NULL)");
		Run("CREATE INDEX IF NOT EXISTS people_camera_day ON people (camera_id, day)");
		Run("CREATE TABLE IF NOT EXISTS sightings (id INTEGER PRIMARY KEY, camera_id TEXT NOT NULL, person_id INTEGER NOT NULL, start_ms INTEGER NOT NULL, end_ms INTEGER NOT NULL, file TEXT NOT NULL, score REAL NOT NULL)");
		Run("CREATE INDEX IF NOT EXISTS sightings_person ON sightings (person_id, start_ms)");
		if (Scalar("SELECT COUNT(*) FROM pragma_table_info('sightings') WHERE name = 'descriptor'") == 0) Run("ALTER TABLE sightings ADD COLUMN descriptor BLOB NULL");
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

	public IReadOnlyList<(long Id, float[] Descriptor)> PeopleOn(string cameraId, string day)
	{
		lock (_lock)
		{
			using var command = Command("SELECT id, descriptor FROM people WHERE camera_id = $c AND day = $d", ("$c", cameraId), ("$d", day));
			using var reader = command.ExecuteReader();
			var people = new List<(long, float[])>();
			while (reader.Read()) people.Add((reader.GetInt64(0), Appearance.FromBytes((byte[])reader[1])));
			return people;
		}
	}

	public long AddSighting(string cameraId, string day, long? personId, float[] descriptor, TimeRange range, string file, double score)
	{
		lock (_lock)
		{
			if (personId is { } known)
			{
				using var read = Command("SELECT descriptor FROM people WHERE id = $p", ("$p", known));
				var mean = Appearance.FromBytes((byte[])read.ExecuteScalar()!);
				var count = (int)Scalar("SELECT COUNT(*) FROM sightings WHERE person_id = $p", ("$p", known));
				Run("UPDATE people SET last_ms = MAX(last_ms, $e), first_ms = MIN(first_ms, $s), descriptor = $x WHERE id = $p", ("$p", known), ("$s", range.StartMs), ("$e", range.EndMs), ("$x", Appearance.ToBytes(PeopleGrouping.Blend(mean, count, descriptor))));
			}
			else
			{
				Run("INSERT INTO people (camera_id, day, descriptor, first_ms, last_ms) VALUES ($c, $d, $x, $s, $e)", ("$c", cameraId), ("$d", day), ("$x", Appearance.ToBytes(descriptor)), ("$s", range.StartMs), ("$e", range.EndMs));
				personId = Scalar("SELECT last_insert_rowid()");
			}
			Run("INSERT INTO sightings (camera_id, person_id, start_ms, end_ms, file, score, descriptor) VALUES ($c, $p, $s, $e, $f, $k, $x)", ("$c", cameraId), ("$p", personId.Value), ("$s", range.StartMs), ("$e", range.EndMs), ("$f", file), ("$k", score), ("$x", Appearance.ToBytes(descriptor)));
			return personId.Value;
		}
	}

	public sealed record Sighting(long Id, long PersonId, long StartMs, long EndMs, string File, double Score = 0);

	public sealed record StoredSighting(long Id, string CameraId, string Day, long StartMs, long EndMs, string File, double Score, float[]? Descriptor);

	public IReadOnlyList<StoredSighting> SightingsToRegroup()
	{
		lock (_lock)
		{
			using var command = Command("SELECT s.id, s.camera_id, p.day, s.start_ms, s.end_ms, s.file, s.score, s.descriptor FROM sightings s JOIN people p ON p.id = s.person_id WHERE p.day IN (SELECT DISTINCT p2.day FROM sightings s2 JOIN people p2 ON p2.id = s2.person_id WHERE s2.descriptor IS NULL) ORDER BY s.start_ms");
			using var reader = command.ExecuteReader();
			var list = new List<StoredSighting>();
			while (reader.Read())
				list.Add(new StoredSighting(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3), reader.GetInt64(4), reader.GetString(5), reader.GetDouble(6), reader.IsDBNull(7) ? null : Appearance.FromBytes((byte[])reader[7])));
			return list;
		}
	}

	public void Regroup(string cameraId, string day, IReadOnlyList<(StoredSighting Sighting, float[] Descriptor)> sightings, IReadOnlyList<List<int>> groups)
	{
		lock (_lock)
		{
			using var tx = _connection.BeginTransaction();
			Run("DELETE FROM people WHERE camera_id = $c AND day = $d", ("$c", cameraId), ("$d", day));
			foreach (var group in groups)
			{
				var members = group.Select(i => sightings[i]).ToList();
				var mean = members[0].Descriptor;
				for (var k = 1; k < members.Count; k++) mean = PeopleGrouping.Blend(mean, k, members[k].Descriptor);
				Run("INSERT INTO people (camera_id, day, descriptor, first_ms, last_ms) VALUES ($c, $d, $x, $s, $e)", ("$c", cameraId), ("$d", day), ("$x", Appearance.ToBytes(mean)), ("$s", members.Min(m => m.Sighting.StartMs)), ("$e", members.Max(m => m.Sighting.EndMs)));
				var person = Scalar("SELECT last_insert_rowid()");
				foreach (var (sighting, descriptor) in members)
					Run("UPDATE sightings SET person_id = $p, descriptor = $x WHERE id = $i", ("$p", person), ("$x", Appearance.ToBytes(descriptor)), ("$i", sighting.Id));
			}
			tx.Commit();
		}
	}

	public sealed record Person(long Id, string Day, long FirstMs, long LastMs, IReadOnlyList<Sighting> Sightings);

	public IReadOnlyList<Person> People(string cameraId, string day)
	{
		lock (_lock)
		{
			using var command = Command("SELECT p.id, p.day, p.first_ms, p.last_ms, s.id, s.start_ms, s.end_ms, s.file, s.score FROM people p JOIN sightings s ON s.person_id = p.id WHERE p.camera_id = $c AND p.day = $d ORDER BY p.last_ms DESC, s.start_ms", ("$c", cameraId), ("$d", day));
			using var reader = command.ExecuteReader();
			var people = new List<Person>();
			while (reader.Read())
			{
				var id = reader.GetInt64(0);
				if (people.Count == 0 || people[^1].Id != id) people.Add(new Person(id, reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3), new List<Sighting>()));
				((List<Sighting>)people[^1].Sightings).Add(new Sighting(reader.GetInt64(4), id, reader.GetInt64(5), reader.GetInt64(6), reader.GetString(7), reader.GetDouble(8)));
			}
			return people;
		}
	}

	public Sighting? SightingOf(long id)
	{
		lock (_lock)
		{
			using var command = Command("SELECT id, person_id, start_ms, end_ms, file FROM sightings WHERE id = $i", ("$i", id));
			using var reader = command.ExecuteReader();
			return reader.Read() ? new Sighting(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetString(4)) : null;
		}
	}

	public IReadOnlyList<string> RemovePerson(long personId)
	{
		lock (_lock)
		{
			var files = SightingFiles("person_id = $p", ("$p", personId));
			Run("DELETE FROM sightings WHERE person_id = $p", ("$p", personId));
			Run("DELETE FROM people WHERE id = $p", ("$p", personId));
			return files;
		}
	}

	public IReadOnlyList<string> RemoveSightingsBefore(long beforeMs)
	{
		lock (_lock)
		{
			var files = SightingFiles("end_ms < $b", ("$b", beforeMs));
			Run("DELETE FROM sightings WHERE end_ms < $b", ("$b", beforeMs));
			Run("DELETE FROM people WHERE id NOT IN (SELECT DISTINCT person_id FROM sightings)");
			return files;
		}
	}

	List<string> SightingFiles(string where, params (string, object)[] args)
	{
		using var command = Command($"SELECT file FROM sightings WHERE {where}", args);
		using var reader = command.ExecuteReader();
		var files = new List<string>();
		while (reader.Read()) files.Add(reader.GetString(0));
		return files;
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
