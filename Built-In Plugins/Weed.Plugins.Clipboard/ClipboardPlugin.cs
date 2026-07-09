using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using TinyPinyin;
using Weed.Abstractions;
using IQueryProvider = Weed.Abstractions.IQueryProvider;

namespace Weed.Plugins.Clipboard;

public sealed class ClipboardPlugin : IWeedPlugin, IQueryProvider, ICommandHandler, IResidentPlugin, IPluginSettingsProvider
{
    public const string PluginId = "weed.clipboard";
    private const int MaxResultPreviewTextLength = 2000;
    private const int DefaultResultLimit = 100;
    private const int MinResultLimit = 10;
    private const int MaxResultLimit = 1000;
    private readonly List<ClipboardItem> _items = [];
    private readonly object _gate = new();
    private IWeedHost? _host;
    private PeriodicTimer? _timer;
    private CancellationTokenSource? _loopCts;
    private Task? _loop;
    private ClipboardMessageListener? _listener;
    private string? _databasePath;
    private string? _objectDirectory;
    private string? _lastHash;

    public string ProviderId => "clipboard";

    public static WeedPluginManifest Manifest => new()
    {
        Id = PluginId,
        Name = "Clipboard",
        Version = "0.1.0",
        SdkVersion = "0.1",
        Icon = "assets/plugins/clipboard.png",
        Runtime = new PluginRuntimeManifest { Resident = true },
        Activations =
        [
            new PluginActivationManifest
            {
                Type = "keyword",
                Keyword = "clip",
                Command = "clipboard.search"
            },
            new PluginActivationManifest
            {
                Type = "hotkey",
                Command = "clipboard.show",
                DefaultKeys = "Shift+Ctrl+C",
                Configurable = true,
                Behavior = "showPluginPanel"
            }
        ],
        Permissions =
        [
            "clipboard.read",
            "clipboard.write",
            "storage.local",
            "window.paste"
        ]
    };

    public ValueTask InitializeAsync(IWeedHost host, CancellationToken cancellationToken)
    {
        _host = host;
        var dataDirectory = host.Storage.GetPluginDataDirectory(PluginId);
        _databasePath = Path.Combine(dataDirectory, "clipboard.db");
        _objectDirectory = Path.Combine(dataDirectory, "objects");
        Directory.CreateDirectory(_objectDirectory);
        Load();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public IReadOnlyList<PluginSettingDefinition> GetSettings() =>
    [
        new()
        {
            Key = "captureImages",
            Label = "Capture images",
            Kind = PluginSettingKind.Boolean,
            DefaultValue = "true"
        },
        new()
        {
            Key = "captureFileLists",
            Label = "Capture file lists",
            Kind = PluginSettingKind.Boolean,
            DefaultValue = "true"
        },
        new()
        {
            Key = "retentionDays",
            Label = "Retention days",
            Kind = PluginSettingKind.Integer,
            DefaultValue = "180",
            Min = 1,
            Max = 3650
        },
        new()
        {
            Key = "maxItems",
            Label = "Maximum records",
            Kind = PluginSettingKind.Integer,
            DefaultValue = "100000",
            Min = 100,
            Max = 100000
        },
        new()
        {
            Key = "resultLimit",
            Label = "Search result limit",
            Kind = PluginSettingKind.Integer,
            Description = "Maximum clipboard results returned to the launcher.",
            DefaultValue = $"{DefaultResultLimit}",
            Min = MinResultLimit,
            Max = MaxResultLimit
        },
        new()
        {
            Key = "maxObjectMegabytes",
            Label = "Object storage MB",
            Kind = PluginSettingKind.Integer,
            DefaultValue = "2048",
            Min = 16,
            Max = 20480
        }
    ];

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        if (_host is null || _loop is not null)
        {
            return ValueTask.CompletedTask;
        }

        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener = TryStartNativeListener(_loopCts.Token);
        _timer = new PeriodicTimer(TimeSpan.FromMilliseconds(850));
        _loop = Task.Run(() => ObserveClipboardAsync(_loopCts.Token), CancellationToken.None);
        return ValueTask.CompletedTask;
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken)
    {
        _loopCts?.Cancel();
        _listener?.Dispose();
        _listener = null;
        _timer?.Dispose();
        if (_loop is not null)
        {
            try
            {
                await _loop.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch
            {
                // Best effort shutdown.
            }
        }
    }

    public ValueTask<IReadOnlyList<WeedResult>> QueryAsync(QueryContext context, CancellationToken cancellationToken)
    {
        var query = context.NormalizedText;
        var resultLimit = ResultLimit();
        var source = string.IsNullOrWhiteSpace(query) ? RecentItems(resultLimit) : SearchItems(query, resultLimit);
        var results = source
            .Select(item => (Item: item, Score: Score(item, query)))
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Item.Pinned)
            .ThenByDescending(item => item.Score)
            .ThenByDescending(item => item.Item.CreatedAt)
            .Take(resultLimit)
            .Select(item => ToResult(item.Item, item.Score))
            .ToArray();

        return ValueTask.FromResult<IReadOnlyList<WeedResult>>(results);
    }

    public async ValueTask<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (_host is null)
        {
            return CommandResult.Failed("Clipboard is not initialized.");
        }

        if (context.Command == "clipboard.show")
        {
            await _host.Windows.ShowClipboardPanelAsync(cancellationToken);
            return CommandResult.Ok(CommandBehavior.ShowPluginPanel);
        }

        if (!context.Data.TryGetValue("id", out var id))
        {
            return CommandResult.Failed("Clipboard item is missing.");
        }

        var item = FindItem(id);
        if (item is null)
        {
            return CommandResult.Failed("Clipboard item was not found.");
        }

        switch (context.Command)
        {
            case "clipboard.copy":
                await CopyItemAsync(item, cancellationToken);
                Touch(item.Id);
                return CommandResult.Ok(message: "Copied clipboard item.");
            case "clipboard.paste":
                await CopyItemAsync(item, cancellationToken);
                await _host.Clipboard.PasteCurrentAsync(cancellationToken);
                Touch(item.Id);
                return CommandResult.Ok(message: "Pasted clipboard item.");
            case "clipboard.delete":
                Delete(item.Id);
                return CommandResult.Ok(CommandBehavior.KeepLauncherOpen, "Deleted clipboard item.");
            case "clipboard.pin":
                TogglePin(item.Id);
                return CommandResult.Ok(CommandBehavior.KeepLauncherOpen, item.Pinned ? "Unpinned item." : "Pinned item.");
            case "clipboard.open":
                await OpenItemAsync(item, cancellationToken);
                return CommandResult.Ok(CommandBehavior.KeepLauncherOpen);
            default:
                return CommandResult.Failed($"Unknown clipboard command: {context.Command}");
        }
    }

    public IReadOnlyList<ClipboardItem> RecentItems(int limit = 100)
    {
        lock (_gate)
        {
            return _items
                .OrderByDescending(i => i.Pinned)
                .ThenByDescending(i => i.CreatedAt)
                .Take(limit)
                .ToArray();
        }
    }

    private async Task ObserveClipboardAsync(CancellationToken cancellationToken)
    {
        if (_host is null || _timer is null)
        {
            return;
        }

        while (await _timer.WaitForNextTickAsync(cancellationToken))
        {
            await CaptureCurrentClipboardAsync(cancellationToken);
        }
    }

    private async Task CaptureCurrentClipboardAsync(CancellationToken cancellationToken)
    {
        if (_host is null)
        {
            return;
        }

        try
        {
            var snapshot = await _host.Clipboard.TryReadAsync(cancellationToken);
            if (snapshot is null)
            {
                return;
            }

            var hash = SnapshotHash(snapshot);
            if (hash == _lastHash)
            {
                return;
            }

            _lastHash = hash;
            AddSnapshot(snapshot, hash);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _host.Logger.Warn($"Clipboard capture failed: {ex.Message}");
        }
    }

    private ClipboardMessageListener? TryStartNativeListener(CancellationToken cancellationToken)
    {
        if (_host is null)
        {
            return null;
        }

        try
        {
            var listener = new ClipboardMessageListener(() =>
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    _ = Task.Run(() => CaptureCurrentClipboardAsync(cancellationToken), CancellationToken.None);
                }
            });
            listener.Start();
            _host.Logger.Info("Started native clipboard listener.");
            return listener;
        }
        catch (Exception ex)
        {
            _host.Logger.Warn($"Native clipboard listener unavailable, using polling only: {ex.Message}");
            return null;
        }
    }

    private void AddSnapshot(ClipboardSnapshot snapshot, string hash)
    {
        var item = BuildItem(snapshot, hash);
        if (item is null)
        {
            return;
        }

        lock (_gate)
        {
            _items.RemoveAll(i => i.ContentHash == hash);
            _items.Insert(0, item);
            UpsertItem(item);

            var cutoff = DateTimeOffset.Now.AddDays(-RetentionDays());
            foreach (var expired in _items.Where(i => !i.Pinned && i.CreatedAt < cutoff).ToArray())
            {
                DeleteItem(expired.Id);
            }

            _items.RemoveAll(i => !i.Pinned && i.CreatedAt < cutoff);
            var maxItems = MaxItems();
            if (_items.Count > maxItems)
            {
                foreach (var overflow in _items.Skip(maxItems).ToArray())
                {
                    DeleteItem(overflow.Id);
                }

                _items.RemoveRange(maxItems, _items.Count - maxItems);
            }

            CleanupUnreferencedObjects();
            EnforceObjectQuota();
        }
    }

    private ClipboardItem? BuildItem(ClipboardSnapshot snapshot, string hash)
    {
        var now = DateTimeOffset.Now;
        return snapshot.Kind switch
        {
            ClipboardContentKind.Text when !string.IsNullOrWhiteSpace(snapshot.TextContent) => new ClipboardItem
            {
                Id = $"clip-{hash}",
                ContentHash = hash,
                Kind = "text",
                Title = FirstLine(snapshot.TextContent),
                TextContent = snapshot.TextContent,
                SourceFormat = "text",
                CreatedAt = now
            },
            ClipboardContentKind.Image when CaptureImages() && snapshot.ImagePng is not null => CreateObjectItem(
                hash,
                "image",
                "Image clip",
                snapshot.TextContent ?? "Image",
                "image/png",
                ".png",
                snapshot.ImagePng,
                now),
            ClipboardContentKind.Files when CaptureFileLists() && snapshot.Files.Count > 0 => new ClipboardItem
            {
                Id = $"clip-{hash}",
                ContentHash = hash,
                Kind = "files",
                Title = snapshot.Files.Count == 1
                    ? Path.GetFileName(snapshot.Files[0])
                    : $"{snapshot.Files.Count} files",
                TextContent = string.Join(Environment.NewLine, snapshot.Files),
                Files = snapshot.Files.ToArray(),
                SourceFormat = "file-drop",
                CreatedAt = now
            },
            ClipboardContentKind.Rtf when !string.IsNullOrWhiteSpace(snapshot.Rtf) => CreateObjectItem(
                hash,
                "rtf",
                FirstLine(snapshot.TextContent ?? "Rich text"),
                snapshot.TextContent ?? "Rich text",
                "text/rtf",
                ".rtf",
                System.Text.Encoding.UTF8.GetBytes(snapshot.Rtf),
                now),
            ClipboardContentKind.Html when !string.IsNullOrWhiteSpace(snapshot.Html) => CreateObjectItem(
                hash,
                "html",
                FirstLine(snapshot.TextContent ?? StripHtml(snapshot.Html)),
                snapshot.TextContent ?? StripHtml(snapshot.Html),
                "text/html",
                ".html",
                System.Text.Encoding.UTF8.GetBytes(snapshot.Html),
                now),
            _ => null
        };
    }

    private ClipboardItem CreateObjectItem(
        string hash,
        string kind,
        string title,
        string textContent,
        string sourceFormat,
        string extension,
        byte[] bytes,
        DateTimeOffset createdAt)
    {
        var path = ObjectPath(hash, extension);
        File.WriteAllBytes(path, bytes);
        return new ClipboardItem
        {
            Id = $"clip-{hash}",
            ContentHash = hash,
            Kind = kind,
            Title = title,
            TextContent = textContent,
            ObjectPath = path,
            SourceFormat = sourceFormat,
            SizeBytes = bytes.LongLength,
            CreatedAt = createdAt
        };
    }

    private async ValueTask CopyItemAsync(ClipboardItem item, CancellationToken cancellationToken)
    {
        if (_host is null)
        {
            return;
        }

        switch (item.Kind)
        {
            case "image" when item.ObjectPath is not null && File.Exists(item.ObjectPath):
                await _host.Clipboard.SetImageAsync(item.ObjectPath, cancellationToken);
                break;
            case "files" when item.Files.Count > 0:
                await _host.Clipboard.SetFilesAsync(item.Files, cancellationToken);
                break;
            default:
                await _host.Clipboard.SetTextAsync(item.TextContent, cancellationToken);
                break;
        }
    }

    private async ValueTask OpenItemAsync(ClipboardItem item, CancellationToken cancellationToken)
    {
        if (_host is null)
        {
            return;
        }

        if (item.Kind == "files" && item.Files.Count > 0)
        {
            await _host.Shell.OpenContainingFolderAsync(item.Files[0], cancellationToken);
        }
        else if (item.ObjectPath is not null && File.Exists(item.ObjectPath))
        {
            await _host.Shell.OpenAsync(item.ObjectPath, cancellationToken);
        }
    }

    private ClipboardItem? FindItem(string id)
    {
        lock (_gate)
        {
            return _items.FirstOrDefault(i => i.Id == id);
        }
    }

    private void Touch(string id)
    {
        lock (_gate)
        {
            var index = _items.FindIndex(i => i.Id == id);
            if (index >= 0)
            {
                _items[index] = _items[index] with { LastUsedAt = DateTimeOffset.Now };
                UpsertItem(_items[index]);
            }
        }
    }

    private void Delete(string id)
    {
        lock (_gate)
        {
            _items.RemoveAll(i => i.Id == id);
            DeleteItem(id);
        }
    }

    private void TogglePin(string id)
    {
        lock (_gate)
        {
            var index = _items.FindIndex(i => i.Id == id);
            if (index >= 0)
            {
                _items[index] = _items[index] with { Pinned = !_items[index].Pinned };
                UpsertItem(_items[index]);
            }
        }
    }

    private void Load()
    {
        if (_databasePath is null)
        {
            return;
        }

        try
        {
            using var connection = OpenConnection();
            ApplyMigrations(connection);
            var items = ReadItems(connection, limit: 1000);
            lock (_gate)
            {
                _items.Clear();
                _items.AddRange(items);
                _lastHash = _items.FirstOrDefault()?.ContentHash;
            }
        }
        catch
        {
            // A corrupt clipboard cache should not stop the launcher.
        }
    }

    private IReadOnlyList<ClipboardItem> SearchItems(string query, int limit)
    {
        if (_databasePath is null)
        {
            lock (_gate)
            {
                return _items.ToArray();
            }
        }

        try
        {
            using var connection = OpenConnection();
            ApplyMigrations(connection);
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT c.id, c.content_hash, c.kind, c.title, c.text_content, c.object_path,
                       c.files_json, c.source_format, c.created_at, c.last_used_at, c.pinned, c.size_bytes
                FROM clipboard_items c
                JOIN clipboard_items_fts f ON f.id = c.id
                WHERE clipboard_items_fts MATCH $query
                ORDER BY c.pinned DESC, rank, c.created_at DESC
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$query", ToFtsQuery(query));
            command.Parameters.AddWithValue("$limit", limit);
            using var reader = command.ExecuteReader();
            var items = ReadItems(reader);
            return items.Count == 0 ? FallbackSearch(query, limit) : items;
        }
        catch
        {
            return FallbackSearch(query, limit);
        }
    }

    private IReadOnlyList<ClipboardItem> FallbackSearch(string query, int limit)
    {
        lock (_gate)
        {
            return _items
                .Where(item => Score(item, query) > 0)
                .OrderByDescending(item => item.Pinned)
                .ThenByDescending(item => item.CreatedAt)
                .Take(limit)
                .ToArray();
        }
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        using var wal = connection.CreateCommand();
        wal.CommandText = "PRAGMA journal_mode=WAL;";
        wal.ExecuteNonQuery();
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

            CREATE TABLE IF NOT EXISTS clipboard_items (
                id TEXT PRIMARY KEY,
                content_hash TEXT NOT NULL,
                kind TEXT NOT NULL,
                title TEXT,
                text_content TEXT,
                object_path TEXT,
                files_json TEXT,
                source_format TEXT,
                created_at TEXT NOT NULL,
                last_used_at TEXT,
                pinned INTEGER NOT NULL DEFAULT 0,
                size_bytes INTEGER NOT NULL DEFAULT 0
            );

            CREATE VIRTUAL TABLE IF NOT EXISTS clipboard_items_fts
            USING fts5(id UNINDEXED, title, text_content, kind);

            INSERT OR IGNORE INTO schema_migrations(version, applied_at)
            VALUES(1, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
            """;
        command.ExecuteNonQuery();
    }

    private void UpsertItem(ClipboardItem item)
    {
        if (_databasePath is null)
        {
            return;
        }

        using var connection = OpenConnection();
        ApplyMigrations(connection);
        using var transaction = connection.BeginTransaction();

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO clipboard_items(
                    id, content_hash, kind, title, text_content, object_path, files_json,
                    source_format, created_at, last_used_at, pinned, size_bytes)
                VALUES(
                    $id, $contentHash, $kind, $title, $textContent, $objectPath, $filesJson,
                    $sourceFormat, $createdAt, $lastUsedAt, $pinned, $sizeBytes)
                ON CONFLICT(id) DO UPDATE SET
                    content_hash = excluded.content_hash,
                    kind = excluded.kind,
                    title = excluded.title,
                    text_content = excluded.text_content,
                    object_path = excluded.object_path,
                    files_json = excluded.files_json,
                    source_format = excluded.source_format,
                    created_at = excluded.created_at,
                    last_used_at = excluded.last_used_at,
                    pinned = excluded.pinned,
                    size_bytes = excluded.size_bytes;
                """;
            AddItemParameters(command, item);
            command.ExecuteNonQuery();
        }

        using (var deleteFts = connection.CreateCommand())
        {
            deleteFts.Transaction = transaction;
            deleteFts.CommandText = "DELETE FROM clipboard_items_fts WHERE id = $id;";
            deleteFts.Parameters.AddWithValue("$id", item.Id);
            deleteFts.ExecuteNonQuery();
        }

        using (var insertFts = connection.CreateCommand())
        {
            insertFts.Transaction = transaction;
            insertFts.CommandText = """
                INSERT INTO clipboard_items_fts(id, title, text_content, kind)
                VALUES($id, $title, $textContent, $kind);
                """;
            insertFts.Parameters.AddWithValue("$id", item.Id);
            insertFts.Parameters.AddWithValue("$title", item.Title);
            insertFts.Parameters.AddWithValue("$textContent", item.TextContent);
            insertFts.Parameters.AddWithValue("$kind", item.Kind);
            insertFts.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private void DeleteItem(string id)
    {
        if (_databasePath is null)
        {
            return;
        }

        string? objectPath = null;
        using var connection = OpenConnection();
        ApplyMigrations(connection);

        using (var select = connection.CreateCommand())
        {
            select.CommandText = "SELECT object_path FROM clipboard_items WHERE id = $id;";
            select.Parameters.AddWithValue("$id", id);
            objectPath = select.ExecuteScalar()?.ToString();
        }

        using var transaction = connection.BeginTransaction();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM clipboard_items WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM clipboard_items_fts WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
        DeleteObjectFile(objectPath);
    }

    private static IReadOnlyList<ClipboardItem> ReadItems(SqliteConnection connection, int limit)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, content_hash, kind, title, text_content, object_path,
                   files_json, source_format, created_at, last_used_at, pinned, size_bytes
            FROM clipboard_items
            ORDER BY pinned DESC, created_at DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader();
        return ReadItems(reader);
    }

    private static List<ClipboardItem> ReadItems(SqliteDataReader reader)
    {
        var items = new List<ClipboardItem>();
        while (reader.Read())
        {
            var filesJson = reader.IsDBNull(6) ? "[]" : reader.GetString(6);
            IReadOnlyList<string> files;
            try
            {
                files = JsonSerializer.Deserialize<List<string>>(filesJson) ?? [];
            }
            catch
            {
                files = [];
            }

            items.Add(new ClipboardItem
            {
                Id = reader.GetString(0),
                ContentHash = reader.GetString(1),
                Kind = reader.GetString(2),
                Title = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                TextContent = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                ObjectPath = reader.IsDBNull(5) ? null : reader.GetString(5),
                Files = files,
                SourceFormat = reader.IsDBNull(7) ? null : reader.GetString(7),
                CreatedAt = DateTimeOffset.TryParse(reader.GetString(8), out var createdAt) ? createdAt : DateTimeOffset.Now,
                LastUsedAt = DateTimeOffset.TryParse(reader.IsDBNull(9) ? null : reader.GetString(9), out var lastUsedAt) ? lastUsedAt : null,
                Pinned = reader.GetInt32(10) != 0,
                SizeBytes = reader.GetInt64(11)
            });
        }

        return items;
    }

    private static void AddItemParameters(SqliteCommand command, ClipboardItem item)
    {
        command.Parameters.AddWithValue("$id", item.Id);
        command.Parameters.AddWithValue("$contentHash", item.ContentHash);
        command.Parameters.AddWithValue("$kind", item.Kind);
        command.Parameters.AddWithValue("$title", item.Title);
        command.Parameters.AddWithValue("$textContent", item.TextContent);
        command.Parameters.AddWithValue("$objectPath", (object?)item.ObjectPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$filesJson", JsonSerializer.Serialize(item.Files));
        command.Parameters.AddWithValue("$sourceFormat", (object?)item.SourceFormat ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", item.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$lastUsedAt", item.LastUsedAt?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$pinned", item.Pinned ? 1 : 0);
        command.Parameters.AddWithValue("$sizeBytes", item.SizeBytes);
    }

    private static string ToFtsQuery(string query)
    {
        var terms = Regex.Matches(query, @"[\p{L}\p{N}_]+")
            .Select(m => m.Value)
            .Where(t => t.Length > 0)
            .Take(8)
            .Select(t => $"{t}*")
            .ToArray();
        return terms.Length == 0 ? "\"\"" : string.Join(" AND ", terms);
    }

    private string ObjectPath(string hash, string extension)
    {
        var first = hash[..2];
        var second = hash.Substring(2, 2);
        var directory = Path.Combine(_objectDirectory!, first, second);
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{hash}{extension}");
    }

    private int RetentionDays() =>
        Math.Clamp(_host?.Settings.GetPluginSetting(PluginId, "retentionDays", 180) ?? 180, 1, 3650);

    private int MaxItems() =>
        Math.Clamp(_host?.Settings.GetPluginSetting(PluginId, "maxItems", 100000) ?? 100000, 100, 100000);

    private int ResultLimit() =>
        Math.Clamp(_host?.Settings.GetPluginSetting(PluginId, "resultLimit", DefaultResultLimit) ?? DefaultResultLimit,
            MinResultLimit,
            MaxResultLimit);

    private long MaxObjectBytes() =>
        Math.Clamp(_host?.Settings.GetPluginSetting(PluginId, "maxObjectMegabytes", 2048) ?? 2048, 16, 20480) *
        1024L *
        1024L;

    private bool CaptureImages() =>
        _host?.Settings.GetPluginSetting(PluginId, "captureImages", true) ?? true;

    private bool CaptureFileLists() =>
        _host?.Settings.GetPluginSetting(PluginId, "captureFileLists", true) ?? true;

    private static WeedResult ToResult(ClipboardItem item, double score)
    {
        var actions = new List<WeedAction>
        {
            new() { Command = "clipboard.copy", Title = "Copy", Shortcut = "Enter" },
            new() { Command = "clipboard.paste", Title = "Paste" },
            new() { Command = "clipboard.pin", Title = item.Pinned ? "Unpin" : "Pin" },
            new() { Command = "clipboard.delete", Title = "Delete" }
        };

        if (item.Kind is "image" or "files" or "html" or "rtf")
        {
            actions.Insert(2, new WeedAction { Command = "clipboard.open", Title = "Open preview" });
        }

        var data = new Dictionary<string, string>
        {
            ["id"] = item.Id,
            ["kind"] = item.Kind,
            ["displayLayout"] = "detail",
            ["detailKind"] = item.Kind
        };
        if (!string.IsNullOrWhiteSpace(item.ObjectPath))
        {
            data["objectPath"] = item.ObjectPath;
        }
        if (!string.IsNullOrWhiteSpace(item.TextContent))
        {
            data["previewText"] = PreviewText(item.TextContent);
        }

        return new WeedResult
        {
            Id = item.Id,
            PluginId = PluginId,
            Title = item.Title,
            Subtitle = $"{item.Kind} - {item.CreatedAt:g}",
            Icon = WeedIcon.FromPath(PluginIconPath(item.Kind)),
            MatchScore = Math.Clamp(score, 1, 30),
            DefaultCommand = "clipboard.copy",
            Actions = actions,
            Data = data
        };
    }

    private static string PreviewText(string text)
    {
        text = text.Trim();
        return text.Length <= MaxResultPreviewTextLength
            ? text
            : string.Concat(text.AsSpan(0, MaxResultPreviewTextLength), "...");
    }

    private static string PluginIconPath(string kind) =>
        Path.Combine(AppContext.BaseDirectory, "assets", "plugins", kind switch
        {
            "image" => "clipboard-image.png",
            "files" => "clipboard-files.png",
            _ => "clipboard.png"
        });

    private static double Score(ClipboardItem item, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return item.Pinned ? 20 : 12;
        }

        query = Normalize(query);
        var parts = query.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 1 && parts[0].StartsWith("type:", StringComparison.OrdinalIgnoreCase))
        {
            var kind = parts[0][5..];
            if (!item.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            query = string.Join(' ', parts.Skip(1));
            if (string.IsNullOrWhiteSpace(query))
            {
                return item.Pinned ? 22 : 14;
            }
        }

        var haystack = Normalize(SearchableText(item));
        if (haystack.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 26;
        }

        if (haystack.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 20;
        }

        var compactQuery = query.Replace(" ", "", StringComparison.Ordinal);
        var pinyin = ToPinyin(haystack);
        if (pinyin.StartsWith(compactQuery, StringComparison.OrdinalIgnoreCase))
        {
            return 24;
        }

        if (pinyin.Contains(compactQuery, StringComparison.OrdinalIgnoreCase))
        {
            return 18;
        }

        var initials = ToPinyinInitials(haystack);
        if (initials.StartsWith(compactQuery, StringComparison.OrdinalIgnoreCase))
        {
            return 23;
        }

        if (initials.Contains(compactQuery, StringComparison.OrdinalIgnoreCase))
        {
            return 16;
        }

        return IsSubsequence(query, haystack) ? 8 : 0;
    }

    private void CleanupUnreferencedObjects()
    {
        if (string.IsNullOrWhiteSpace(_objectDirectory) || !Directory.Exists(_objectDirectory))
        {
            return;
        }

        var referenced = _items
            .Select(item => item.ObjectPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(_objectDirectory, "*", SearchOption.AllDirectories))
        {
            if (!referenced.Contains(file))
            {
                DeleteObjectFile(file);
            }
        }

        RemoveEmptyObjectDirectories();
    }

    private void EnforceObjectQuota()
    {
        if (string.IsNullOrWhiteSpace(_objectDirectory))
        {
            return;
        }

        var objectItems = _items
            .Where(item => !item.Pinned &&
                           !string.IsNullOrWhiteSpace(item.ObjectPath) &&
                           File.Exists(item.ObjectPath))
            .Select(item => (Item: item, Size: new FileInfo(item.ObjectPath!).Length))
            .OrderBy(item => item.Item.CreatedAt)
            .ToArray();
        var totalBytes = _items
            .Where(item => !string.IsNullOrWhiteSpace(item.ObjectPath) && File.Exists(item.ObjectPath))
            .Sum(item => new FileInfo(item.ObjectPath!).Length);
        var maxBytes = MaxObjectBytes();

        foreach (var (item, size) in objectItems)
        {
            if (totalBytes <= maxBytes)
            {
                break;
            }

            DeleteItem(item.Id);
            _items.RemoveAll(i => i.Id == item.Id);
            totalBytes -= size;
        }

        RemoveEmptyObjectDirectories();
    }

    private void RemoveEmptyObjectDirectories()
    {
        if (string.IsNullOrWhiteSpace(_objectDirectory) || !Directory.Exists(_objectDirectory))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(_objectDirectory, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
            catch
            {
                // Best-effort cache cleanup.
            }
        }
    }

    private static void DeleteObjectFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Object cleanup should never interrupt clipboard capture.
        }
    }

    private static bool IsSubsequence(string query, string target)
    {
        if (query.Length == 0)
        {
            return true;
        }

        var q = 0;
        foreach (var ch in target.ToLowerInvariant())
        {
            if (q < query.Length && ch == query[q])
            {
                q++;
            }
        }

        return q == query.Length;
    }

    private static string SearchableText(ClipboardItem item)
    {
        var text = $"{item.Title}\n{item.TextContent}\n{item.Kind}";
        return text.Length <= 4000 ? text : text[..4000];
    }

    private static string Normalize(string value) =>
        string.Join(' ', value.Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string ToPinyin(string text)
    {
        try
        {
            return Normalize(PinyinHelper.GetPinyin(text, "")).Replace(" ", "", StringComparison.Ordinal);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ToPinyinInitials(string text)
    {
        try
        {
            return Normalize(PinyinHelper.GetPinyinInitials(text)).Replace(" ", "", StringComparison.Ordinal);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string FirstLine(string text)
    {
        var line = text.Replace("\r", "", StringComparison.Ordinal).Split('\n').FirstOrDefault() ?? text;
        return line.Length <= 90 ? line : line[..90] + "...";
    }

    private static string StripHtml(string html)
    {
        var withoutTags = Regex.Replace(html, "<[^>]+>", " ");
        return System.Net.WebUtility.HtmlDecode(withoutTags).Trim();
    }

    private static string SnapshotHash(ClipboardSnapshot snapshot)
    {
        var bytes = snapshot.Kind switch
        {
            ClipboardContentKind.Image when snapshot.ImagePng is not null => snapshot.ImagePng,
            ClipboardContentKind.Files => System.Text.Encoding.UTF8.GetBytes(string.Join("\n", snapshot.Files)),
            ClipboardContentKind.Rtf => System.Text.Encoding.UTF8.GetBytes(snapshot.Rtf ?? ""),
            ClipboardContentKind.Html => System.Text.Encoding.UTF8.GetBytes(snapshot.Html ?? ""),
            _ => System.Text.Encoding.UTF8.GetBytes(snapshot.TextContent ?? "")
        };

        var prefix = System.Text.Encoding.UTF8.GetBytes(snapshot.Kind.ToString());
        var combined = new byte[prefix.Length + bytes.Length];
        Buffer.BlockCopy(prefix, 0, combined, 0, prefix.Length);
        Buffer.BlockCopy(bytes, 0, combined, prefix.Length, bytes.Length);
        return StableHash(combined);
    }

    private static string StableHash(byte[] bytes)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

internal sealed class ClipboardMessageListener : IDisposable
{
    private const int WmClipboardUpdate = 0x031D;
    private const int WmQuit = 0x0012;
    private static readonly IntPtr HwndMessage = new(-3);
    private readonly Action _onChanged;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly string _className = $"WeedClipboardListener-{Guid.NewGuid():N}";
    private readonly Thread _thread;
    private WndProc? _wndProc;
    private IntPtr _hwnd;
    private uint _threadId;
    private ushort _classAtom;
    private bool _disposed;

    public ClipboardMessageListener(Action onChanged)
    {
        _onChanged = onChanged;
        _thread = new Thread(MessageLoop)
        {
            IsBackground = true,
            Name = "Weed Clipboard Listener"
        };
        _thread.SetApartmentState(ApartmentState.STA);
    }

    public void Start()
    {
        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(2)) || _hwnd == IntPtr.Zero)
        {
            Dispose();
            throw new InvalidOperationException("Clipboard listener window was not created.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_threadId != 0)
        {
            PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        }

        if (_thread.IsAlive)
        {
            _thread.Join(TimeSpan.FromSeconds(2));
        }

        _ready.Dispose();
    }

    private void MessageLoop()
    {
        _threadId = GetCurrentThreadId();
        _wndProc = WindowProcedure;
        var hInstance = GetModuleHandle(null);
        var windowClass = new WndClass
        {
            lpfnWndProc = _wndProc,
            hInstance = hInstance,
            lpszClassName = _className
        };
        _classAtom = RegisterClass(ref windowClass);
        if (_classAtom == 0)
        {
            _ready.Set();
            return;
        }

        _hwnd = CreateWindowEx(
            0,
            _className,
            _className,
            0,
            0,
            0,
            0,
            0,
            HwndMessage,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);
        if (_hwnd != IntPtr.Zero)
        {
            AddClipboardFormatListener(_hwnd);
        }

        _ready.Set();
        while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }

        if (_hwnd != IntPtr.Zero)
        {
            RemoveClipboardFormatListener(_hwnd);
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        if (_classAtom != 0)
        {
            UnregisterClass(_className, hInstance);
            _classAtom = 0;
        }
    }

    private IntPtr WindowProcedure(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WmClipboardUpdate)
        {
            _onChanged();
            return IntPtr.Zero;
        }

        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private delegate IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClass
    {
        public uint style;
        public WndProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClass(ref WndClass lpWndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle,
        string lpClassName,
        string lpWindowName,
        int dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern sbyte GetMessage(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Msg lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Msg lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint idThread, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}

public sealed record ClipboardItem
{
    public required string Id { get; init; }

    public required string ContentHash { get; init; }

    public string Kind { get; init; } = "text";

    public required string Title { get; init; }

    public required string TextContent { get; init; }

    public string? ObjectPath { get; init; }

    public IReadOnlyList<string> Files { get; init; } = [];

    public string? SourceFormat { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? LastUsedAt { get; init; }

    public bool Pinned { get; init; }

    public long SizeBytes { get; init; }
}
