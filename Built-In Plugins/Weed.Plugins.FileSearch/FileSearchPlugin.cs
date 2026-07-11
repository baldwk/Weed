using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Weed.Abstractions;
using IQueryProvider = Weed.Abstractions.IQueryProvider;

namespace Weed.Plugins.FileSearch;

public sealed class FileSearchPlugin : IWeedPlugin, IQueryProvider, ICommandHandler, IPluginSettingsProvider
{
    public const string PluginId = "weed.fileSearch";
    private readonly IEverythingSearchClient _client;
    private IWeedHost? _host;

    public FileSearchPlugin()
        : this(new EverythingSdkSearchClient())
    {
    }

    public FileSearchPlugin(IEverythingSearchClient client)
    {
        _client = client;
    }

    public string ProviderId => "fileSearch";

    public static WeedPluginManifest Manifest => new()
    {
        Id = PluginId,
        Name = "File Search",
        Version = "0.1.0",
        SdkVersion = "0.1",
        Icon = "assets/plugins/file-search.png",
        Activations =
        [
            new PluginActivationManifest
            {
                Type = "keyword",
                Keyword = "file",
                Command = "file.search"
            }
        ],
        Permissions =
        [
            "file.read",
            "shell.launch"
        ],
        ExternalDependencies =
        [
            new PluginExternalDependencyManifest
            {
                Id = "everything",
                Name = "Everything",
                Executables = ["Everything.exe"],
                AutoStart = true,
                ReadinessProbe = "everythingIpc"
            }
        ]
    };

    public ValueTask InitializeAsync(IWeedHost host, CancellationToken cancellationToken)
    {
        _host = host;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public IReadOnlyList<PluginSettingDefinition> GetSettings() =>
    [
        new()
        {
            Key = "includeFolders",
            Label = "Include folders",
            Kind = PluginSettingKind.Boolean,
            DefaultValue = "true"
        },
        new()
        {
            Key = "maxResults",
            Label = "Maximum results",
            Kind = PluginSettingKind.Integer,
            DefaultValue = "50",
            Min = 5,
            Max = 200
        },
        new()
        {
            Key = "sort",
            Label = "Sort order",
            Kind = PluginSettingKind.Select,
            DefaultValue = EverythingSortOption.NameAscending.Value,
            Description = "Uses Everything SDK sort types.",
            Options = EverythingSortOption.All
                .Select(option => new PluginSettingOption { Value = option.Value, Label = option.Label })
                .ToArray()
        }
    ];

    public async ValueTask<IReadOnlyList<WeedResult>> QueryAsync(QueryContext context, CancellationToken cancellationToken)
    {
        if (_host is null)
        {
            return [];
        }

        var query = context.RawText.Trim();
        if (query.Length == 0)
        {
            return [];
        }

        var settings = FileSearchSettings.FromHost(_host.Settings);
        try
        {
            var results = await _client.SearchAsync(query, settings, cancellationToken);
            return results
                .Select((entry, index) => ToResult(entry, Score(index)))
                .ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is EverythingUnavailableException or DllNotFoundException or EntryPointNotFoundException)
        {
            _host.Logger.Warn($"Everything search unavailable: {ex.Message}");
            return
            [
                DiagnosticResult(ex.Message)
            ];
        }
        catch (Exception ex)
        {
            _host.Logger.Error("Everything search failed.", ex);
            return
            [
                DiagnosticResult(ex.Message)
            ];
        }
    }

    public async ValueTask<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (_host is null)
        {
            return CommandResult.Failed("File Search is not initialized.");
        }

        if (context.Command == "file.noop")
        {
            return CommandResult.Ok(CommandBehavior.KeepLauncherOpen, context.Data.GetValueOrDefault("error") ?? "Everything is unavailable.");
        }

        if (!context.Data.TryGetValue("path", out var path) || string.IsNullOrWhiteSpace(path))
        {
            return CommandResult.Failed("File path is missing.");
        }

        switch (context.Command)
        {
            case "file.open":
                await _host.Shell.OpenAsync(path, cancellationToken);
                return CommandResult.Ok(message: "Opened file result.");
            case "file.openLocation":
                await _host.Shell.OpenContainingFolderAsync(path, cancellationToken);
                return CommandResult.Ok(CommandBehavior.KeepLauncherOpen, "Opened containing folder.");
            case "file.copyPath":
                await _host.Shell.CopyPathAsync(path, cancellationToken);
                return CommandResult.Ok(CommandBehavior.KeepLauncherOpen, "Copied path.");
            default:
                return CommandResult.Failed($"Unknown file search command: {context.Command}");
        }
    }

    private static WeedResult ToResult(EverythingSearchResult entry, double score)
    {
        var title = string.IsNullOrWhiteSpace(Path.GetFileName(entry.Path))
            ? entry.Path
            : Path.GetFileName(entry.Path);
        var subtitle = entry.Kind == FileSearchResultKind.Folder
            ? entry.Path
            : Path.GetDirectoryName(entry.Path) ?? entry.Path;

        return new WeedResult
        {
            Id = $"file-{StableHash(entry.Path)}",
            PluginId = PluginId,
            Title = title,
            Subtitle = subtitle,
            Icon = WeedIcon.FromPath(PluginIconPath()),
            MatchScore = Math.Clamp(score, 1, 30),
            DefaultCommand = "file.open",
            Actions =
            [
                new WeedAction { Command = "file.open", Title = "Open", Shortcut = "Enter" },
                new WeedAction { Command = "file.openLocation", Title = "Open location" },
                new WeedAction { Command = "file.copyPath", Title = "Copy path" }
            ],
            Data = new Dictionary<string, string>
            {
                ["path"] = entry.Path,
                ["kind"] = entry.Kind == FileSearchResultKind.Folder ? "folder" : "file"
            }
        };
    }

    private static WeedResult DiagnosticResult(string message) => new()
    {
        Id = "file-search-unavailable",
        PluginId = PluginId,
        Title = "Everything is unavailable",
        Subtitle = message,
        Icon = WeedIcon.FromPath(PluginIconPath()),
        MatchScore = 12,
        DefaultCommand = "file.noop",
        Actions =
        [
            new WeedAction { Command = "file.noop", Title = "Keep open", Shortcut = "Enter" }
        ],
        Data = new Dictionary<string, string>
        {
            ["error"] = message
        }
    };

    private static double Score(int index) => Math.Max(30 - index, 1);

    private static string StableHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    private static string PluginIconPath() =>
        Path.Combine(AppContext.BaseDirectory, "assets", "plugins", "file-search.png");
}

public interface IEverythingSearchClient
{
    ValueTask<IReadOnlyList<EverythingSearchResult>> SearchAsync(
        string query,
        FileSearchSettings settings,
        CancellationToken cancellationToken);
}

public sealed record EverythingSearchResult(string Path, FileSearchResultKind Kind);

public enum FileSearchResultKind
{
    File,
    Folder
}

public sealed record FileSearchSettings(
    bool IncludeFolders,
    int MaxResults,
    EverythingSortOption Sort)
{
    public static FileSearchSettings FromHost(IWeedSettings settings) => new(
        settings.GetPluginSetting(FileSearchPlugin.PluginId, "includeFolders", true),
        Math.Clamp(settings.GetPluginSetting(FileSearchPlugin.PluginId, "maxResults", 50), 5, 200),
        EverythingSortOption.FromValue(settings.GetPluginSetting(
            FileSearchPlugin.PluginId,
            "sort",
            EverythingSortOption.NameAscending.Value)));
}

public sealed record EverythingSortOption(string Value, string Label, uint SortType)
{
    public static readonly EverythingSortOption NameAscending = new("nameAscending", "Name asc", 1);
    public static readonly EverythingSortOption NameDescending = new("nameDescending", "Name desc", 2);
    public static readonly EverythingSortOption PathAscending = new("pathAscending", "Path asc", 3);
    public static readonly EverythingSortOption PathDescending = new("pathDescending", "Path desc", 4);
    public static readonly EverythingSortOption SizeAscending = new("sizeAscending", "Size asc", 5);
    public static readonly EverythingSortOption SizeDescending = new("sizeDescending", "Size desc", 6);
    public static readonly EverythingSortOption ExtensionAscending = new("extensionAscending", "Extension asc", 7);
    public static readonly EverythingSortOption ExtensionDescending = new("extensionDescending", "Extension desc", 8);
    public static readonly EverythingSortOption TypeNameAscending = new("typeNameAscending", "Type name asc", 9);
    public static readonly EverythingSortOption TypeNameDescending = new("typeNameDescending", "Type name desc", 10);
    public static readonly EverythingSortOption DateCreatedAscending = new("dateCreatedAscending", "Created asc", 11);
    public static readonly EverythingSortOption DateCreatedDescending = new("dateCreatedDescending", "Created desc", 12);
    public static readonly EverythingSortOption DateModifiedAscending = new("dateModifiedAscending", "Modified asc", 13);
    public static readonly EverythingSortOption DateModifiedDescending = new("dateModifiedDescending", "Modified desc", 14);
    public static readonly EverythingSortOption AttributesAscending = new("attributesAscending", "Attributes asc", 15);
    public static readonly EverythingSortOption AttributesDescending = new("attributesDescending", "Attributes desc", 16);
    public static readonly EverythingSortOption FileListFilenameAscending = new("fileListFilenameAscending", "File-list name asc", 17);
    public static readonly EverythingSortOption FileListFilenameDescending = new("fileListFilenameDescending", "File-list name desc", 18);
    public static readonly EverythingSortOption RunCountAscending = new("runCountAscending", "Run count asc", 19);
    public static readonly EverythingSortOption RunCountDescending = new("runCountDescending", "Run count desc", 20);
    public static readonly EverythingSortOption DateRecentlyChangedAscending = new("dateRecentlyChangedAscending", "Recently changed asc", 21);
    public static readonly EverythingSortOption DateRecentlyChangedDescending = new("dateRecentlyChangedDescending", "Recently changed desc", 22);
    public static readonly EverythingSortOption DateAccessedAscending = new("dateAccessedAscending", "Accessed asc", 23);
    public static readonly EverythingSortOption DateAccessedDescending = new("dateAccessedDescending", "Accessed desc", 24);
    public static readonly EverythingSortOption DateRunAscending = new("dateRunAscending", "Date run asc", 25);
    public static readonly EverythingSortOption DateRunDescending = new("dateRunDescending", "Date run desc", 26);

    public static IReadOnlyList<EverythingSortOption> All { get; } =
    [
        NameAscending,
        NameDescending,
        PathAscending,
        PathDescending,
        SizeAscending,
        SizeDescending,
        ExtensionAscending,
        ExtensionDescending,
        TypeNameAscending,
        TypeNameDescending,
        DateCreatedAscending,
        DateCreatedDescending,
        DateModifiedAscending,
        DateModifiedDescending,
        AttributesAscending,
        AttributesDescending,
        FileListFilenameAscending,
        FileListFilenameDescending,
        RunCountAscending,
        RunCountDescending,
        DateRecentlyChangedAscending,
        DateRecentlyChangedDescending,
        DateAccessedAscending,
        DateAccessedDescending,
        DateRunAscending,
        DateRunDescending
    ];

    public static EverythingSortOption FromValue(string? value) =>
        All.FirstOrDefault(option => option.Value.Equals(value, StringComparison.OrdinalIgnoreCase))
        ?? NameAscending;
}

public sealed class EverythingUnavailableException : Exception
{
    public EverythingUnavailableException(string message)
        : base(message)
    {
    }
}

public sealed class EverythingSdkSearchClient : IEverythingSearchClient
{
    public async ValueTask<IReadOnlyList<EverythingSearchResult>> SearchAsync(
        string query,
        FileSearchSettings settings,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sdk = EverythingSdk.Current;
            var search = settings.IncludeFolders ? query : $"file: {query}";
            sdk.SetSearch(search);
            sdk.SetRequestFlags(EverythingSdk.RequestFileName | EverythingSdk.RequestPath);
            sdk.SetMax((uint)settings.MaxResults);
            sdk.SetSort(settings.Sort.SortType);
            if (!sdk.Query(wait: true))
            {
                throw new EverythingUnavailableException(EverythingErrorMessage(sdk.LastError));
            }

            var count = Math.Min(sdk.NumResults, (uint)settings.MaxResults);
            var results = new List<EverythingSearchResult>((int)count);
            for (uint i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = sdk.GetResultFullPath(i);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    results.Add(new EverythingSearchResult(
                        path,
                        sdk.IsFolderResult(i) ? FileSearchResultKind.Folder : FileSearchResultKind.File));
                }
            }

            return (IReadOnlyList<EverythingSearchResult>)results;
        }, cancellationToken);
    }

    private static string EverythingErrorMessage(uint error) => error switch
    {
        2 => "Everything IPC is unavailable. Make sure Everything is installed and running.",
        3 => "Everything search query is invalid.",
        4 => "Everything search ran out of memory.",
        _ => $"Everything SDK query failed with error {error}."
    };
}

internal abstract class EverythingSdk
{
    public const uint RequestFileName = 0x00000001;
    public const uint RequestPath = 0x00000002;

    public static EverythingSdk Current { get; } = Environment.Is64BitProcess
        ? new EverythingSdk64()
        : new EverythingSdk32();

    public abstract uint LastError { get; }

    public abstract uint NumResults { get; }

    public abstract void SetSearch(string search);

    public abstract void SetRequestFlags(uint flags);

    public abstract void SetMax(uint max);

    public abstract void SetSort(uint sortType);

    public abstract bool Query(bool wait);

    public abstract bool IsFolderResult(uint index);

    public abstract string GetResultFullPath(uint index);
}

internal sealed class EverythingSdk64 : EverythingSdk
{
    public override uint LastError => Native.Everything_GetLastError();

    public override uint NumResults => Native.Everything_GetNumResults();

    public override void SetSearch(string search) => Native.Everything_SetSearchW(search);

    public override void SetRequestFlags(uint flags) => Native.Everything_SetRequestFlags(flags);

    public override void SetMax(uint max) => Native.Everything_SetMax(max);

    public override void SetSort(uint sortType) => Native.Everything_SetSort(sortType);

    public override bool Query(bool wait) => Native.Everything_QueryW(wait);

    public override bool IsFolderResult(uint index) => Native.Everything_IsFolderResult(index);

    public override string GetResultFullPath(uint index)
    {
        var buffer = new StringBuilder(32768);
        Native.Everything_GetResultFullPathNameW(index, buffer, (uint)buffer.Capacity);
        return buffer.ToString();
    }

    private static partial class Native
    {
        [DllImport("Everything64.dll", CharSet = CharSet.Unicode, EntryPoint = "Everything_SetSearchW")]
        internal static extern void Everything_SetSearchW(string search);

        [DllImport("Everything64.dll", EntryPoint = "Everything_SetRequestFlags")]
        internal static extern void Everything_SetRequestFlags(uint flags);

        [DllImport("Everything64.dll", EntryPoint = "Everything_SetMax")]
        internal static extern void Everything_SetMax(uint max);

        [DllImport("Everything64.dll", EntryPoint = "Everything_SetSort")]
        internal static extern void Everything_SetSort(uint sortType);

        [DllImport("Everything64.dll", EntryPoint = "Everything_QueryW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool Everything_QueryW([MarshalAs(UnmanagedType.Bool)] bool wait);

        [DllImport("Everything64.dll", EntryPoint = "Everything_GetLastError")]
        internal static extern uint Everything_GetLastError();

        [DllImport("Everything64.dll", EntryPoint = "Everything_GetNumResults")]
        internal static extern uint Everything_GetNumResults();

        [DllImport("Everything64.dll", EntryPoint = "Everything_IsFolderResult")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool Everything_IsFolderResult(uint index);

        [DllImport("Everything64.dll", CharSet = CharSet.Unicode, EntryPoint = "Everything_GetResultFullPathNameW")]
        internal static extern uint Everything_GetResultFullPathNameW(uint index, StringBuilder buffer, uint size);
    }
}

internal sealed class EverythingSdk32 : EverythingSdk
{
    public override uint LastError => Native.Everything_GetLastError();

    public override uint NumResults => Native.Everything_GetNumResults();

    public override void SetSearch(string search) => Native.Everything_SetSearchW(search);

    public override void SetRequestFlags(uint flags) => Native.Everything_SetRequestFlags(flags);

    public override void SetMax(uint max) => Native.Everything_SetMax(max);

    public override void SetSort(uint sortType) => Native.Everything_SetSort(sortType);

    public override bool Query(bool wait) => Native.Everything_QueryW(wait);

    public override bool IsFolderResult(uint index) => Native.Everything_IsFolderResult(index);

    public override string GetResultFullPath(uint index)
    {
        var buffer = new StringBuilder(32768);
        Native.Everything_GetResultFullPathNameW(index, buffer, (uint)buffer.Capacity);
        return buffer.ToString();
    }

    private static partial class Native
    {
        [DllImport("Everything32.dll", CharSet = CharSet.Unicode, EntryPoint = "Everything_SetSearchW")]
        internal static extern void Everything_SetSearchW(string search);

        [DllImport("Everything32.dll", EntryPoint = "Everything_SetRequestFlags")]
        internal static extern void Everything_SetRequestFlags(uint flags);

        [DllImport("Everything32.dll", EntryPoint = "Everything_SetMax")]
        internal static extern void Everything_SetMax(uint max);

        [DllImport("Everything32.dll", EntryPoint = "Everything_SetSort")]
        internal static extern void Everything_SetSort(uint sortType);

        [DllImport("Everything32.dll", EntryPoint = "Everything_QueryW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool Everything_QueryW([MarshalAs(UnmanagedType.Bool)] bool wait);

        [DllImport("Everything32.dll", EntryPoint = "Everything_GetLastError")]
        internal static extern uint Everything_GetLastError();

        [DllImport("Everything32.dll", EntryPoint = "Everything_GetNumResults")]
        internal static extern uint Everything_GetNumResults();

        [DllImport("Everything32.dll", EntryPoint = "Everything_IsFolderResult")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool Everything_IsFolderResult(uint index);

        [DllImport("Everything32.dll", CharSet = CharSet.Unicode, EntryPoint = "Everything_GetResultFullPathNameW")]
        internal static extern uint Everything_GetResultFullPathNameW(uint index, StringBuilder buffer, uint size);
    }
}
