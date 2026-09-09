using Microsoft.Data.Sqlite;

namespace TaskbarBanner.Core.Reporting;

public sealed class SqliteMinuteStore : IMinuteStore
{
    private const string DeviceIdMetaKey = "device_id";

    private readonly string _connectionString;

    private SqliteMinuteStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    public string DeviceId { get; private set; } = string.Empty;

    public static SqliteMinuteStore Open(string databasePath)
    {
        string connectionString = $"Data Source={databasePath};Pooling=True";
        var store = new SqliteMinuteStore(connectionString);
        store.Initialize();
        return store;
    }

    public void Enqueue(VerifiedMinute minute)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT OR IGNORE INTO minutes
                (idempotency_key, session_id, device_id, minute_start_utc, minute_end_utc, created_utc, state, attempt_count, last_attempt_utc)
            VALUES
                ($key, $session, $device, $start, $end, $created, 0, 0, NULL);
            """;
        command.Parameters.AddWithValue("$key", minute.IdempotencyKey);
        command.Parameters.AddWithValue("$session", minute.SessionId);
        command.Parameters.AddWithValue("$device", minute.DeviceId);
        command.Parameters.AddWithValue("$start", minute.MinuteStartUtc.ToString("O"));
        command.Parameters.AddWithValue("$end", minute.MinuteEndUtc.ToString("O"));
        command.Parameters.AddWithValue("$created", minute.CreatedUtc.ToString("O"));
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<VerifiedMinute> GetPending(int limit)
    {
        var result = new List<VerifiedMinute>();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, idempotency_key, session_id, device_id, minute_start_utc, minute_end_utc, created_utc, state, attempt_count, last_attempt_utc
            FROM minutes
            WHERE state = 0
            ORDER BY id
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(ReadMinute(reader));
        }

        return result;
    }

    public void MarkUploaded(IReadOnlyList<long> ids)
    {
        if (ids.Count == 0)
        {
            return;
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE minutes SET state = 1 WHERE id = $id;";
        var parameter = command.Parameters.Add("$id", SqliteType.Integer);

        foreach (long id in ids)
        {
            parameter.Value = id;
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void MarkAttemptFailed(IReadOnlyList<long> ids)
    {
        if (ids.Count == 0)
        {
            return;
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "UPDATE minutes SET attempt_count = attempt_count + 1, last_attempt_utc = $now WHERE id = $id AND state = 0;";
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        var parameter = command.Parameters.Add("$id", SqliteType.Integer);

        foreach (long id in ids)
        {
            parameter.Value = id;
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public long CountPending()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM minutes WHERE state = 0;";
        return (long)command.ExecuteScalar()!;
    }

    public long CountUploaded()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM minutes WHERE state = 1;";
        return (long)command.ExecuteScalar()!;
    }

    public long CountAll()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM minutes;";
        return (long)command.ExecuteScalar()!;
    }

    public void ClearPending()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM minutes WHERE state = 0;";
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
    }

    private void Initialize()
    {
        using var connection = OpenConnection();
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS minutes (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    idempotency_key TEXT NOT NULL UNIQUE,
                    session_id TEXT NOT NULL,
                    device_id TEXT NOT NULL,
                    minute_start_utc TEXT NOT NULL,
                    minute_end_utc TEXT NOT NULL,
                    created_utc TEXT NOT NULL,
                    state INTEGER NOT NULL DEFAULT 0,
                    attempt_count INTEGER NOT NULL DEFAULT 0,
                    last_attempt_utc TEXT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_minutes_state ON minutes(state);
                CREATE TABLE IF NOT EXISTS meta (
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }

        DeviceId = LoadOrCreateDeviceId(connection);
    }

    private string LoadOrCreateDeviceId(SqliteConnection connection)
    {
        using var select = connection.CreateCommand();
        select.CommandText = "SELECT value FROM meta WHERE key = $key;";
        select.Parameters.AddWithValue("$key", DeviceIdMetaKey);
        object? existing = select.ExecuteScalar();
        if (existing is string current)
        {
            return current;
        }

        string deviceId = Guid.NewGuid().ToString("N");
        using var insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO meta (key, value) VALUES ($key, $value);";
        insert.Parameters.AddWithValue("$key", DeviceIdMetaKey);
        insert.Parameters.AddWithValue("$value", deviceId);
        insert.ExecuteNonQuery();
        return deviceId;
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static VerifiedMinute ReadMinute(SqliteDataReader reader)
    {
        long id = reader.GetInt64(0);
        string key = reader.GetString(1);
        string sessionId = reader.GetString(2);
        string deviceId = reader.GetString(3);
        DateTimeOffset start = DateTimeOffset.Parse(reader.GetString(4), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);
        DateTimeOffset end = DateTimeOffset.Parse(reader.GetString(5), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);
        DateTimeOffset created = DateTimeOffset.Parse(reader.GetString(6), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);
        int state = reader.GetInt32(7);
        int attempts = reader.GetInt32(8);
        DateTimeOffset? lastAttempt = reader.IsDBNull(9)
            ? null
            : DateTimeOffset.Parse(reader.GetString(9), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);

        return new VerifiedMinute
        {
            Id = id,
            IdempotencyKey = key,
            SessionId = sessionId,
            DeviceId = deviceId,
            MinuteStartUtc = start,
            MinuteEndUtc = end,
            CreatedUtc = created,
            State = (MinuteReportState)state,
            AttemptCount = attempts,
            LastAttemptUtc = lastAttempt,
        };
    }
}
