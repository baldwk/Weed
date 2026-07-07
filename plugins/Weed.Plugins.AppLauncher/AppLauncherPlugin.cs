using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using TinyPinyin;
using Weed.Abstractions;
using IQueryProvider = Weed.Abstractions.IQueryProvider;

namespace Weed.Plugins.AppLauncher;

public sealed class AppLauncherPlugin : IWeedPlugin, IQueryProvider, ICommandHandler, IPluginSettingsProvider
{
    public const string PluginId = "weed.appLauncher";
    private readonly List<AppEntry> _entries = [];
    private IWeedHost? _host;
    private string? _iconCacheDirectory;
    private string? _databasePath;
    private DateTimeOffset _indexedAt;

    public string ProviderId => "appLauncher";

    public static WeedPluginManifest Manifest => new()
    {
        Id = PluginId,
        Name = "App Launcher",
        Version = "0.1.0",
        SdkVersion = "0.1",
        Icon = "assets/plugins/app-launcher.png",
        Activations =
        [
            new PluginActivationManifest
            {
                Type = "implicitQuery",
                Provider = "appLauncher"
            }
        ],
        Permissions =
        [
            "shell.launch",
            "clipboard.write",
            "file.read"
        ]
    };

    public ValueTask InitializeAsync(IWeedHost host, CancellationToken cancellationToken)
    {
        _host = host;
        var dataDirectory = host.Storage.GetPluginDataDirectory(PluginId);
        _databasePath = Path.Combine(dataDirectory, "app-launcher.db");
        _iconCacheDirectory = Path.Combine(host.Storage.GetPluginCacheDirectory(PluginId), "icons");
        Directory.CreateDirectory(_iconCacheDirectory);
        LoadCache();
        if (_entries.Count == 0)
        {
            RefreshIndex();
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public IReadOnlyList<PluginSettingDefinition> GetSettings() =>
    [
        new()
        {
            Key = "hideMaintenanceShortcuts",
            Label = "Hide uninstall and maintenance shortcuts",
            Kind = PluginSettingKind.Boolean,
            DefaultValue = "true"
        }
    ];

    public ValueTask<IReadOnlyList<WeedResult>> QueryAsync(QueryContext context, CancellationToken cancellationToken)
    {
        if (_entries.Count == 0 || (DateTimeOffset.Now - _indexedAt).TotalMinutes > 15)
        {
            RefreshIndex();
        }

        var query = Normalize(context.NormalizedText);
        if (query.Length == 0)
        {
            return ValueTask.FromResult<IReadOnlyList<WeedResult>>([]);
        }

        if (IsRefreshQuery(query))
        {
            return ValueTask.FromResult<IReadOnlyList<WeedResult>>(
            [
                new WeedResult
                {
                    Id = "app-refresh-index",
                    PluginId = PluginId,
                    Title = "Refresh application index",
                    Subtitle = "Re-scan Start Menu shortcuts now",
                    Icon = WeedIcon.FromPath(PluginIconPath()),
                    MatchScore = 30,
                    DefaultCommand = "app.refreshIndex",
                    Actions =
                    [
                        new WeedAction { Command = "app.refreshIndex", Title = "Refresh", Shortcut = "Enter" }
                    ]
                }
            ]);
        }

        var maxResults = Math.Clamp(_host?.Settings.GetPluginSetting(PluginId, "maxResults", 12) ?? 12, 4, 50);
        var results = _entries
            .Select(entry => (Entry: entry, Score: Score(entry, query)))
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Entry.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Take(maxResults)
            .Select(item => ToResult(item.Entry, item.Score))
            .ToArray();

        return ValueTask.FromResult<IReadOnlyList<WeedResult>>(results);
    }

    public async ValueTask<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (_host is null)
        {
            return CommandResult.Failed("App Launcher is not initialized.");
        }

        if (context.Command == "app.refreshIndex")
        {
            RefreshIndex();
            return CommandResult.Ok(CommandBehavior.KeepLauncherOpen, $"Indexed {_entries.Count} apps.");
        }

        if (!context.Data.TryGetValue("shortcutPath", out var shortcutPath))
        {
            return CommandResult.Failed("App path is missing.");
        }

        context.Data.TryGetValue("targetPath", out var targetPath);
        context.Data.TryGetValue("arguments", out var arguments);
        context.Data.TryGetValue("workingDirectory", out var workingDirectory);

        switch (context.Command)
        {
            case "app.open":
                await _host.Shell.OpenAsync(shortcutPath, cancellationToken);
                return CommandResult.Ok(message: "Opened app.");
            case "app.openAdmin":
                await _host.Shell.OpenAsAdministratorAsync(
                    string.IsNullOrWhiteSpace(targetPath) ? shortcutPath : targetPath,
                    arguments,
                    workingDirectory,
                    cancellationToken);
                return CommandResult.Ok(message: "Opened app as administrator.");
            case "app.openLocation":
                await _host.Shell.OpenContainingFolderAsync(
                    string.IsNullOrWhiteSpace(targetPath) ? shortcutPath : targetPath,
                    cancellationToken);
                return CommandResult.Ok(CommandBehavior.KeepLauncherOpen);
            case "app.copyPath":
                await _host.Shell.CopyPathAsync(
                    string.IsNullOrWhiteSpace(targetPath) ? shortcutPath : targetPath,
                    cancellationToken);
                return CommandResult.Ok(CommandBehavior.KeepLauncherOpen, "Copied app path.");
            default:
                return CommandResult.Failed($"Unknown app command: {context.Command}");
        }
    }

    private void RefreshIndex()
    {
        _entries.Clear();
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs")
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                         .Where(path => path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ||
                                        path.EndsWith(".appref-ms", StringComparison.OrdinalIgnoreCase)))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!seen.Add(file) || string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var shortcut = ShortcutInfo.TryRead(file);
                if (!ShouldIndexEntry(name, file, shortcut, HideMaintenanceShortcuts()))
                {
                    continue;
                }

                var iconPath = TryExtractIcon(file, shortcut?.TargetPath);
                _entries.Add(CreateEntry(name, file, shortcut, iconPath));
            }
        }

        _indexedAt = DateTimeOffset.Now;
        SaveCache();
        _host?.Logger.Info($"Indexed {_entries.Count} Start Menu shortcuts.");
    }

    private void LoadCache()
    {
        if (_databasePath is null || !File.Exists(_databasePath))
        {
            return;
        }

        try
        {
            using var connection = OpenConnection();
            ApplyMigrations(connection);
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, display_name, normalized_name, pinyin, pinyin_initials, acronym,
                       target_path, arguments, working_directory, icon_path, shortcut_path, indexed_at
                FROM app_entries
                ORDER BY display_name COLLATE NOCASE;
                """;
            using var reader = command.ExecuteReader();
            _entries.Clear();
            while (reader.Read())
            {
                _entries.Add(new AppEntry
                {
                    Id = reader.GetString(0),
                    DisplayName = reader.GetString(1),
                    NormalizedName = reader.GetString(2),
                    Pinyin = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    PinyinInitials = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    Acronym = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                    TargetPath = reader.IsDBNull(6) ? null : reader.GetString(6),
                    Arguments = reader.IsDBNull(7) ? null : reader.GetString(7),
                    WorkingDirectory = reader.IsDBNull(8) ? null : reader.GetString(8),
                    IconPath = reader.IsDBNull(9) ? null : reader.GetString(9),
                    ShortcutPath = reader.GetString(10),
                    IndexedAt = DateTimeOffset.TryParse(reader.GetString(11), out var indexedAt) ? indexedAt : DateTimeOffset.MinValue
                });
            }

            _indexedAt = _entries.Count == 0 ? DateTimeOffset.MinValue : _entries.Max(entry => entry.IndexedAt);
            _host?.Logger.Info($"Loaded {_entries.Count} cached Start Menu shortcuts.");
        }
        catch (Exception ex)
        {
            _host?.Logger.Warn($"Failed to load app index cache: {ex.Message}");
            _entries.Clear();
            _indexedAt = DateTimeOffset.MinValue;
        }
    }

    private void SaveCache()
    {
        if (_databasePath is null)
        {
            return;
        }

        try
        {
            using var connection = OpenConnection();
            ApplyMigrations(connection);
            using var transaction = connection.BeginTransaction();

            using (var delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM app_entries;";
                delete.ExecuteNonQuery();
            }

            foreach (var entry in _entries)
            {
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO app_entries(
                        id, display_name, normalized_name, pinyin, pinyin_initials, acronym,
                        target_path, arguments, working_directory, icon_path, shortcut_path, indexed_at)
                    VALUES(
                        $id, $displayName, $normalizedName, $pinyin, $pinyinInitials, $acronym,
                        $targetPath, $arguments, $workingDirectory, $iconPath, $shortcutPath, $indexedAt);
                    """;
                insert.Parameters.AddWithValue("$id", entry.Id);
                insert.Parameters.AddWithValue("$displayName", entry.DisplayName);
                insert.Parameters.AddWithValue("$normalizedName", entry.NormalizedName);
                insert.Parameters.AddWithValue("$pinyin", entry.Pinyin);
                insert.Parameters.AddWithValue("$pinyinInitials", entry.PinyinInitials);
                insert.Parameters.AddWithValue("$acronym", entry.Acronym);
                insert.Parameters.AddWithValue("$targetPath", (object?)entry.TargetPath ?? DBNull.Value);
                insert.Parameters.AddWithValue("$arguments", (object?)entry.Arguments ?? DBNull.Value);
                insert.Parameters.AddWithValue("$workingDirectory", (object?)entry.WorkingDirectory ?? DBNull.Value);
                insert.Parameters.AddWithValue("$iconPath", (object?)entry.IconPath ?? DBNull.Value);
                insert.Parameters.AddWithValue("$shortcutPath", entry.ShortcutPath);
                insert.Parameters.AddWithValue("$indexedAt", entry.IndexedAt.ToString("O"));
                insert.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        catch (Exception ex)
        {
            _host?.Logger.Warn($"Failed to save app index cache: {ex.Message}");
        }
    }

    private SqliteConnection OpenConnection()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath!)!);
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
            CREATE TABLE IF NOT EXISTS app_entries (
                id TEXT PRIMARY KEY,
                display_name TEXT NOT NULL,
                normalized_name TEXT NOT NULL,
                pinyin TEXT,
                pinyin_initials TEXT,
                acronym TEXT,
                target_path TEXT,
                arguments TEXT,
                working_directory TEXT,
                icon_path TEXT,
                shortcut_path TEXT NOT NULL,
                indexed_at TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public static AppEntry CreateEntry(string displayName, string shortcutPath, ShortcutInfo? shortcut = null, string? iconPath = null)
    {
        var normalized = Normalize(displayName);
        var pinyin = ToPinyin(displayName);
        return new AppEntry
        {
            Id = $"app-{TextId(displayName)}-{StableHash(shortcutPath)[..12]}",
            DisplayName = displayName,
            NormalizedName = normalized,
            Pinyin = pinyin,
            PinyinInitials = ToPinyinInitials(displayName),
            Acronym = Acronym(displayName),
            TargetPath = shortcut?.TargetPath,
            Arguments = shortcut?.Arguments,
            WorkingDirectory = shortcut?.WorkingDirectory,
            IconPath = iconPath,
            ShortcutPath = shortcutPath,
            IndexedAt = DateTimeOffset.Now
        };
    }

    public static bool ShouldIndexEntry(
        string displayName,
        string shortcutPath,
        ShortcutInfo? shortcut,
        bool hideMaintenanceShortcuts = true)
    {
        if (!hideMaintenanceShortcuts)
        {
            return true;
        }

        if (LooksLikeMaintenanceName(displayName) ||
            LooksLikeMaintenanceName(Path.GetFileNameWithoutExtension(shortcutPath)))
        {
            return false;
        }

        var targetName = Path.GetFileName(shortcut?.TargetPath ?? string.Empty);
        if (LooksLikeUninstallerExecutable(targetName))
        {
            return false;
        }

        if (targetName.Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase) &&
            (shortcut?.Arguments?.Contains("/x", StringComparison.OrdinalIgnoreCase) == true ||
             shortcut?.Arguments?.Contains("-x", StringComparison.OrdinalIgnoreCase) == true ||
             shortcut?.Arguments?.Contains("/uninstall", StringComparison.OrdinalIgnoreCase) == true))
        {
            return false;
        }

        return true;
    }

    public static double Score(AppEntry entry, string query)
    {
        query = Normalize(query);
        if (query.Length == 0)
        {
            return 0;
        }

        if (entry.NormalizedName.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            return 30;
        }

        if (entry.NormalizedName.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 27;
        }

        if (entry.Pinyin.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            return 26;
        }

        if (entry.Pinyin.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 24;
        }

        if (entry.PinyinInitials.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 23;
        }

        if (entry.Acronym.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 22;
        }

        var containsScore = Math.Max(
            ContainsScore(entry.NormalizedName, query),
            ContainsScore(entry.Pinyin, query));
        if (containsScore > 0)
        {
            return containsScore;
        }

        if (IsSubsequence(query, entry.NormalizedName) ||
            IsSubsequence(query, entry.Pinyin) ||
            IsSubsequence(query, entry.PinyinInitials))
        {
            return 10;
        }

        return 0;
    }

    private WeedResult ToResult(AppEntry entry, double score)
    {
        var data = new Dictionary<string, string>
        {
            ["shortcutPath"] = entry.ShortcutPath
        };
        AddIfNotNull(data, "targetPath", entry.TargetPath);
        AddIfNotNull(data, "arguments", entry.Arguments);
        AddIfNotNull(data, "workingDirectory", entry.WorkingDirectory);

        return new WeedResult
        {
            Id = entry.Id,
            PluginId = PluginId,
            Title = entry.DisplayName,
            Subtitle = string.IsNullOrWhiteSpace(entry.TargetPath) ? entry.ShortcutPath : entry.TargetPath,
            Icon = string.IsNullOrWhiteSpace(entry.IconPath) ? WeedIcon.FromPath(PluginIconPath()) : WeedIcon.FromPath(entry.IconPath),
            MatchScore = Math.Clamp(score, 1, 30),
            DefaultCommand = "app.open",
            Actions =
            [
                new WeedAction { Command = "app.open", Title = "Open", Shortcut = "Enter" },
                new WeedAction { Command = "app.openAdmin", Title = "Run as administrator" },
                new WeedAction { Command = "app.openLocation", Title = "Open location" },
                new WeedAction { Command = "app.copyPath", Title = "Copy path" }
            ],
            Data = data
        };
    }

    private string? TryExtractIcon(string shortcutPath, string? targetPath)
    {
        if (string.IsNullOrWhiteSpace(_iconCacheDirectory))
        {
            return null;
        }

        var source = !string.IsNullOrWhiteSpace(targetPath) && File.Exists(targetPath) ? targetPath : shortcutPath;
        if (!File.Exists(source))
        {
            return null;
        }

        var iconPath = Path.Combine(_iconCacheDirectory, $"{Math.Abs(source.GetHashCode())}.png");
        if (File.Exists(iconPath))
        {
            return iconPath;
        }

        try
        {
            using var icon = Icon.ExtractAssociatedIcon(source);
            if (icon is null)
            {
                return null;
            }

            using var bitmap = icon.ToBitmap();
            bitmap.Save(iconPath, ImageFormat.Png);
            return iconPath;
        }
        catch
        {
            return null;
        }
    }

    private bool HideMaintenanceShortcuts() =>
        _host?.Settings.GetPluginSetting(PluginId, "hideMaintenanceShortcuts", true) ?? true;

    private static bool LooksLikeMaintenanceName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var normalized = Normalize(name);
        return normalized.StartsWith("uninstall", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("remove ", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("remove", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("解除安装", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("卸载", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains(" 卸载", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeUninstallerExecutable(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var name = fileName.ToLowerInvariant();
        return name.StartsWith("unins", StringComparison.Ordinal) ||
               name.StartsWith("uninst", StringComparison.Ordinal) ||
               name.StartsWith("uninstall", StringComparison.Ordinal) ||
               name.Contains("uninstall", StringComparison.Ordinal);
    }

    private static string PluginIconPath() =>
        Path.Combine(AppContext.BaseDirectory, "assets", "plugins", "app-launcher.png");

    private static bool IsSubsequence(string query, string target)
    {
        var queryIndex = 0;
        foreach (var ch in target)
        {
            if (queryIndex < query.Length && query[queryIndex] == ch)
            {
                queryIndex++;
            }
        }

        return queryIndex == query.Length;
    }

    private static double ContainsScore(string target, string query)
    {
        if (!target.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var matchCoverage = CountNonOverlappingOccurrences(target, query) * query.Length / (double)Math.Max(1, target.Length);
        return 18 + Math.Clamp(matchCoverage, 0, 1) * 3.9;
    }

    private static int CountNonOverlappingOccurrences(string target, string query)
    {
        var count = 0;
        var index = 0;
        while (index < target.Length)
        {
            var found = target.IndexOf(query, index, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                break;
            }

            count++;
            index = found + query.Length;
        }

        return count;
    }

    private static bool IsRefreshQuery(string query) =>
        query.Equals("refresh apps", StringComparison.OrdinalIgnoreCase) ||
        query.Equals("refresh applications", StringComparison.OrdinalIgnoreCase) ||
        query.Equals("app refresh", StringComparison.OrdinalIgnoreCase) ||
        query.Equals("apps refresh", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string text) =>
        string.Join(' ', text.Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string TextId(string text)
    {
        var normalized = Normalize(text);
        return new string(normalized.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
    }

    private static string StableHash(string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text.ToLowerInvariant());
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string Acronym(string text)
    {
        var words = Normalize(text).Split([' ', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= 1)
        {
            return new string(text.Where(char.IsUpper).Select(char.ToLowerInvariant).ToArray());
        }

        return string.Concat(words.Select(w => w[0]));
    }

    private static string ToPinyin(string text)
    {
        try
        {
            return Normalize(PinyinHelper.GetPinyin(text, ""));
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
            return Normalize(PinyinHelper.GetPinyinInitials(text));
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void AddIfNotNull(Dictionary<string, string> data, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            data[key] = value;
        }
    }
}

public sealed record AppEntry
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required string NormalizedName { get; init; }

    public string Pinyin { get; init; } = string.Empty;

    public string PinyinInitials { get; init; } = string.Empty;

    public string Acronym { get; init; } = string.Empty;

    public string? TargetPath { get; init; }

    public string? Arguments { get; init; }

    public string? WorkingDirectory { get; init; }

    public string? IconPath { get; init; }

    public required string ShortcutPath { get; init; }

    public DateTimeOffset IndexedAt { get; init; }
}

public sealed record ShortcutInfo
{
    public string? TargetPath { get; init; }

    public string? Arguments { get; init; }

    public string? WorkingDirectory { get; init; }

    public static ShortcutInfo? TryRead(string shortcutPath)
    {
        if (!shortcutPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        object? shell = null;
        object? shortcut = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return null;
            }

            shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return null;
            }

            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                shell,
                [shortcutPath]);
            if (shortcut is null)
            {
                return null;
            }

            var type = shortcut.GetType();
            return new ShortcutInfo
            {
                TargetPath = type.InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, shortcut, null)?.ToString(),
                Arguments = type.InvokeMember("Arguments", System.Reflection.BindingFlags.GetProperty, null, shortcut, null)?.ToString(),
                WorkingDirectory = type.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.GetProperty, null, shortcut, null)?.ToString()
            };
        }
        catch
        {
            return null;
        }
        finally
        {
            ReleaseCom(shortcut);
            ReleaseCom(shell);
        }
    }

    private static void ReleaseCom(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }
}
