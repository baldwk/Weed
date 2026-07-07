using Microsoft.Data.Sqlite;

namespace Weed.Core;

public sealed record UsageHistoryItem
{
    public required string PluginId { get; init; }

    public required string ResultId { get; init; }

    public required string CommandId { get; init; }

    public int SelectedCount { get; init; }

    public DateTimeOffset? LastSelectedAt { get; init; }
}

public sealed class UsageHistoryStore
{
    private readonly object _gate = new();
    private readonly string _databasePath;

    public UsageHistoryStore(string databasePath)
    {
        _databasePath = databasePath;
    }

    public void Load()
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
            using var connection = OpenConnection();
            ApplyMigrations(connection);
        }
    }

    public void Record(string pluginId, string resultId, string commandId)
    {
        lock (_gate)
        {
            using var connection = OpenConnection();
            ApplyMigrations(connection);
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO usage_history(plugin_id, result_id, command_id, selected_count, last_selected_at)
                VALUES($pluginId, $resultId, $commandId, 1, $lastSelectedAt)
                ON CONFLICT(plugin_id, result_id, command_id)
                DO UPDATE SET
                    selected_count = selected_count + 1,
                    last_selected_at = excluded.last_selected_at;
                """;
            command.Parameters.AddWithValue("$pluginId", pluginId);
            command.Parameters.AddWithValue("$resultId", resultId);
            command.Parameters.AddWithValue("$commandId", commandId);
            command.Parameters.AddWithValue("$lastSelectedAt", DateTimeOffset.Now.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    public double GetScore(string pluginId, string resultId, string commandId)
    {
        lock (_gate)
        {
            using var connection = OpenConnection();
            ApplyMigrations(connection);
            var items = ReadPluginItems(connection, pluginId);
            if (items.Count == 0)
            {
                return 0;
            }

            var max = items.Max(WeightedValue);
            if (max <= 0)
            {
                return 0;
            }

            var item = items.FirstOrDefault(i =>
                i.PluginId.Equals(pluginId, StringComparison.OrdinalIgnoreCase) &&
                i.ResultId.Equals(resultId, StringComparison.OrdinalIgnoreCase) &&
                i.CommandId.Equals(commandId, StringComparison.OrdinalIgnoreCase));
            return item is null ? 0 : Math.Clamp(WeightedValue(item) / max * 30d, 0d, 30d);
        }
    }

    public DateTimeOffset? GetLastSelectedAt(string pluginId, string resultId, string commandId)
    {
        lock (_gate)
        {
            using var connection = OpenConnection();
            ApplyMigrations(connection);
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT last_selected_at
                FROM usage_history
                WHERE plugin_id = $pluginId AND result_id = $resultId AND command_id = $commandId;
                """;
            command.Parameters.AddWithValue("$pluginId", pluginId);
            command.Parameters.AddWithValue("$resultId", resultId);
            command.Parameters.AddWithValue("$commandId", commandId);
            var value = command.ExecuteScalar()?.ToString();
            return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
        }
    }

    public IReadOnlyList<UsageHistoryItem> ReadAll()
    {
        lock (_gate)
        {
            using var connection = OpenConnection();
            ApplyMigrations(connection);
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT plugin_id, result_id, command_id, selected_count, last_selected_at
                FROM usage_history
                ORDER BY last_selected_at DESC;
                """;
            using var reader = command.ExecuteReader();
            return ReadItems(reader);
        }
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();

        using var wal = connection.CreateCommand();
        wal.CommandText = "PRAGMA journal_mode=WAL;";
        wal.ExecuteNonQuery();

        using var foreignKeys = connection.CreateCommand();
        foreignKeys.CommandText = "PRAGMA foreign_keys=ON;";
        foreignKeys.ExecuteNonQuery();

        return connection;
    }

    private static void ApplyMigrations(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                version INTEGER PRIMARY KEY,
                applied_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS usage_history (
                plugin_id TEXT NOT NULL,
                result_id TEXT NOT NULL,
                command_id TEXT NOT NULL,
                selected_count INTEGER NOT NULL DEFAULT 0,
                last_selected_at TEXT,
                PRIMARY KEY (plugin_id, result_id, command_id)
            );

            INSERT OR IGNORE INTO schema_migrations(version, applied_at)
            VALUES(1, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
            """;
        command.ExecuteNonQuery();
    }

    private static List<UsageHistoryItem> ReadPluginItems(SqliteConnection connection, string pluginId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT plugin_id, result_id, command_id, selected_count, last_selected_at
            FROM usage_history
            WHERE plugin_id = $pluginId;
            """;
        command.Parameters.AddWithValue("$pluginId", pluginId);
        using var reader = command.ExecuteReader();
        return ReadItems(reader);
    }

    private static List<UsageHistoryItem> ReadItems(SqliteDataReader reader)
    {
        var items = new List<UsageHistoryItem>();
        while (reader.Read())
        {
            items.Add(new UsageHistoryItem
            {
                PluginId = reader.GetString(0),
                ResultId = reader.GetString(1),
                CommandId = reader.GetString(2),
                SelectedCount = reader.GetInt32(3),
                LastSelectedAt = DateTimeOffset.TryParse(reader.IsDBNull(4) ? null : reader.GetString(4), out var parsed)
                    ? parsed
                    : null
            });
        }

        return items;
    }

    private static double WeightedValue(UsageHistoryItem item)
    {
        var count = Math.Max(0, item.SelectedCount);
        if (item.LastSelectedAt is null)
        {
            return count;
        }

        var age = DateTimeOffset.Now - item.LastSelectedAt.Value;
        var recency = Math.Max(0.15, 1d - age.TotalDays / 30d);
        return count * recency;
    }
}
