using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using Weed.Abstractions;

namespace Weed.Core;

public sealed record WeedAppSettings
{
    public string Theme { get; init; } = "system";

    public bool ShowTrayIcon { get; init; } = true;

    public bool LaunchAtStartup { get; init; }

    public bool AutoCheckUpdates { get; init; }

    public string UpdateManifestUrl { get; init; } = string.Empty;

    public string ExternalPluginRegistryUrl { get; init; } =
        "https://raw.githubusercontent.com/wky/Weed/master/plugins.registry.json";

    public string MainHotkey { get; init; } = "Alt+Space";

    public bool CloseOnLostFocus { get; init; } = true;
}

public sealed record PluginUserSetting
{
    public bool Enabled { get; init; } = true;

    public int Priority { get; init; }
}

public sealed record HotkeyUserSetting
{
    public string Keys { get; init; } = string.Empty;

    public bool Enabled { get; init; } = true;
}

public sealed class AppPaths
{
    public AppPaths(string? appDataRoot = null, string? localDataRoot = null)
    {
        AppDataRoot = appDataRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Weed");
        LocalDataRoot = localDataRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Weed");
        Logs = Path.Combine(LocalDataRoot, "logs");
        Plugins = Path.Combine(LocalDataRoot, "plugins");
        Cache = Path.Combine(LocalDataRoot, "cache");
        ClipboardObjects = Path.Combine(LocalDataRoot, "clipboard-objects");
        Updates = Path.Combine(LocalDataRoot, "updates");
        Screenshots = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "Weed");
    }

    public string AppDataRoot { get; }

    public string LocalDataRoot { get; }

    public string Logs { get; }

    public string Plugins { get; }

    public string Cache { get; }

    public string ClipboardObjects { get; }

    public string Updates { get; }

    public string Screenshots { get; }

    public string SettingsFile => Path.Combine(AppDataRoot, "settings.json");

    public string HotkeysFile => Path.Combine(AppDataRoot, "hotkeys.json");

    public string PluginsFile => Path.Combine(AppDataRoot, "plugins.json");

    public string DatabaseFile => Path.Combine(LocalDataRoot, "weed.db");

    public string UsageFile => Path.Combine(LocalDataRoot, "usage-history.json");

    public string PluginSettingsDirectory => Path.Combine(AppDataRoot, "plugin-settings");

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(AppDataRoot);
        Directory.CreateDirectory(PluginSettingsDirectory);
        Directory.CreateDirectory(LocalDataRoot);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Plugins);
        Directory.CreateDirectory(Cache);
        Directory.CreateDirectory(ClipboardObjects);
        Directory.CreateDirectory(Updates);
        Directory.CreateDirectory(Screenshots);
    }
}

public sealed class SettingsRepository : IWeedSettings, IWeedStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object _gate = new();

    private Dictionary<string, Dictionary<string, JsonElement>> _pluginSettings = [];

    public SettingsRepository(AppPaths paths)
    {
        Paths = paths;
        Paths.EnsureDirectories();
    }

    public AppPaths Paths { get; }

    public WeedAppSettings AppSettings { get; private set; } = new();

    public Dictionary<string, HotkeyUserSetting> Hotkeys { get; private set; } = [];

    public Dictionary<string, PluginUserSetting> Plugins { get; private set; } = [];

    public void Load()
    {
        AppSettings = ReadJson(Paths.SettingsFile, new WeedAppSettings());
        Hotkeys = ReadJson(Paths.HotkeysFile, new Dictionary<string, HotkeyUserSetting>());
        Plugins = ReadJson(Paths.PluginsFile, new Dictionary<string, PluginUserSetting>());
        _pluginSettings = ReadPluginSettings();
        SaveDefaultsIfMissing();
        Save();
    }

    public void Save()
    {
        WriteJson(Paths.SettingsFile, AppSettings);
        WriteJson(Paths.HotkeysFile, Hotkeys);
        WriteJson(Paths.PluginsFile, Plugins);
        WritePluginSettings();
    }

    public void SetAppSettings(WeedAppSettings settings)
    {
        AppSettings = settings;
        Save();
    }

    public void EnsurePluginDefaults(IEnumerable<WeedPluginManifest> manifests)
    {
        var changed = false;
        foreach (var manifest in manifests)
        {
            if (!Plugins.ContainsKey(manifest.Id))
            {
                Plugins[manifest.Id] = new PluginUserSetting();
                changed = true;
            }

            foreach (var activation in manifest.Activations.Where(a => a.Type.Equals("hotkey", StringComparison.OrdinalIgnoreCase)))
            {
                if (string.IsNullOrWhiteSpace(activation.Command) || string.IsNullOrWhiteSpace(activation.DefaultKeys))
                {
                    continue;
                }

                var key = $"{manifest.Id}:{activation.Command}";
                if (!Hotkeys.ContainsKey(key))
                {
                    Hotkeys[key] = new HotkeyUserSetting
                    {
                        Keys = HotkeyText.Normalize(activation.DefaultKeys),
                        Enabled = true
                    };
                    changed = true;
                }
            }
        }

        if (changed)
        {
            Save();
        }
    }

    public bool IsPluginEnabled(string pluginId) =>
        !Plugins.TryGetValue(pluginId, out var setting) || setting.Enabled;

    public int GetPluginPriority(string pluginId) =>
        Plugins.TryGetValue(pluginId, out var setting) ? Math.Clamp(setting.Priority, 0, 100) : 0;

    public void SetPluginEnabled(string pluginId, bool enabled)
    {
        var current = Plugins.TryGetValue(pluginId, out var value) ? value : new PluginUserSetting();
        Plugins[pluginId] = current with { Enabled = enabled };
        Save();
    }

    public void SetPluginPriority(string pluginId, int priority)
    {
        var current = Plugins.TryGetValue(pluginId, out var value) ? value : new PluginUserSetting();
        Plugins[pluginId] = current with { Priority = Math.Clamp(priority, 0, 100) };
        Save();
    }

    public T GetPluginSetting<T>(string pluginId, string key, T defaultValue)
    {
        lock (_gate)
        {
            if (!_pluginSettings.TryGetValue(pluginId, out var settings) ||
                !settings.TryGetValue(key, out var value))
            {
                return defaultValue;
            }

            try
            {
                return value.Deserialize<T>(JsonOptions) ?? defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }
    }

    public void SetPluginSetting<T>(string pluginId, string key, T value)
    {
        lock (_gate)
        {
            if (!_pluginSettings.TryGetValue(pluginId, out var settings))
            {
                settings = [];
                _pluginSettings[pluginId] = settings;
            }

            settings[key] = JsonSerializer.SerializeToElement(value, JsonOptions);
            Save();
        }
    }

    public string GetPluginDataDirectory(string pluginId)
    {
        var path = Path.Combine(Paths.LocalDataRoot, "plugins-data", Sanitize(pluginId));
        Directory.CreateDirectory(path);
        return path;
    }

    public string GetPluginCacheDirectory(string pluginId)
    {
        var path = Path.Combine(Paths.Cache, Sanitize(pluginId));
        Directory.CreateDirectory(path);
        return path;
    }

    private void SaveDefaultsIfMissing()
    {
        if (!File.Exists(Paths.SettingsFile))
        {
            WriteJson(Paths.SettingsFile, AppSettings);
        }

        if (!File.Exists(Paths.HotkeysFile))
        {
            WriteJson(Paths.HotkeysFile, Hotkeys);
        }

        if (!File.Exists(Paths.PluginsFile))
        {
            WriteJson(Paths.PluginsFile, Plugins);
        }
    }

    private static string Sanitize(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '-');
        }

        return value;
    }

    private Dictionary<string, Dictionary<string, JsonElement>> ReadPluginSettings()
    {
        var pluginSettings = new Dictionary<string, Dictionary<string, JsonElement>>();

        if (!Directory.Exists(Paths.PluginSettingsDirectory))
        {
            Directory.CreateDirectory(Paths.PluginSettingsDirectory);
            return pluginSettings;
        }

        foreach (var path in Directory.EnumerateFiles(Paths.PluginSettingsDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            var pluginId = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(pluginId))
            {
                continue;
            }

            var settings = ReadJson(path, new Dictionary<string, JsonElement>());
            pluginSettings[pluginId] = settings;
        }

        return pluginSettings;
    }

    private void WritePluginSettings()
    {
        Directory.CreateDirectory(Paths.PluginSettingsDirectory);
        foreach (var (pluginId, settings) in _pluginSettings)
        {
            WriteJson(PluginSettingsPath(pluginId), settings);
        }
    }

    private string PluginSettingsPath(string pluginId) =>
        Path.Combine(Paths.PluginSettingsDirectory, $"{Sanitize(pluginId)}.json");

    private static T ReadJson<T>(string path, T fallback)
    {
        if (!File.Exists(path))
        {
            return fallback;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = $"{path}.tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(value, JsonOptions));
        File.Move(tempPath, path, true);
    }
}

public static class HotkeyText
{
    public static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? mainKey = null;

        foreach (var part in parts)
        {
            var normalized = part.Equals("Control", StringComparison.OrdinalIgnoreCase) ? "Ctrl" : part;
            if (normalized.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("Shift", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("Alt", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("Win", StringComparison.OrdinalIgnoreCase))
            {
                keys.Add(ToTitle(normalized));
            }
            else
            {
                mainKey = normalized.Length == 1 ? normalized.ToUpperInvariant() : ToTitle(normalized);
            }
        }

        var ordered = new List<string>();
        foreach (var modifier in new[] { "Ctrl", "Shift", "Alt", "Win" })
        {
            if (keys.Contains(modifier))
            {
                ordered.Add(modifier);
            }
        }

        if (!string.IsNullOrWhiteSpace(mainKey))
        {
            ordered.Add(mainKey);
        }

        return string.Join("+", ordered);
    }

    private static string ToTitle(string value)
    {
        if (value.Length == 0)
        {
            return value;
        }

        return char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
    }
}
