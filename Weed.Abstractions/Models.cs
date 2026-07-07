namespace Weed.Abstractions;

public enum ActivationKind
{
    Keyword,
    Hotkey,
    ImplicitQuery
}

public sealed record QueryActivation
{
    public required ActivationKind Type { get; init; }

    public string? Keyword { get; init; }

    public string? Command { get; init; }

    public string? Provider { get; init; }

    public static QueryActivation ForKeyword(string keyword, string command) => new()
    {
        Type = ActivationKind.Keyword,
        Keyword = keyword,
        Command = command
    };

    public static QueryActivation ForHotkey(string command) => new()
    {
        Type = ActivationKind.Hotkey,
        Command = command
    };

    public static QueryActivation ForImplicit(string provider) => new()
    {
        Type = ActivationKind.ImplicitQuery,
        Provider = provider
    };
}

public sealed record QueryContext
{
    public required string RawText { get; init; }

    public required string NormalizedText { get; init; }

    public required QueryActivation Activation { get; init; }

    public string? Keyword { get; init; }

    public string? Command { get; init; }

    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.Now;
}

public sealed record CommandContext
{
    public required string PluginId { get; init; }

    public required string Command { get; init; }

    public string? ResultId { get; init; }

    public string? Query { get; init; }

    public IReadOnlyDictionary<string, string> Data { get; init; } = new Dictionary<string, string>();
}

public enum CommandBehavior
{
    None,
    CloseLauncher,
    KeepLauncherOpen,
    ShowLauncher,
    ShowPluginPanel,
    ShowMessage
}

public sealed record CommandResult
{
    public bool Succeeded { get; init; } = true;

    public CommandBehavior Behavior { get; init; } = CommandBehavior.CloseLauncher;

    public string? Message { get; init; }

    public string? InitialQuery { get; init; }

    public static CommandResult Ok(CommandBehavior behavior = CommandBehavior.CloseLauncher, string? message = null) => new()
    {
        Succeeded = true,
        Behavior = behavior,
        Message = message
    };

    public static CommandResult Failed(string message) => new()
    {
        Succeeded = false,
        Behavior = CommandBehavior.ShowMessage,
        Message = message
    };
}

public sealed record WeedResult
{
    public required string Id { get; init; }

    public required string PluginId { get; init; }

    public required string Title { get; init; }

    public string? Subtitle { get; init; }

    public WeedIcon? Icon { get; init; }

    public double MatchScore { get; init; }

    public required string DefaultCommand { get; init; }

    public IReadOnlyList<WeedAction> Actions { get; init; } = [];

    public IReadOnlyDictionary<string, string> Data { get; init; } = new Dictionary<string, string>();
}

public sealed record WeedAction
{
    public required string Command { get; init; }

    public required string Title { get; init; }

    public string? Shortcut { get; init; }
}

public sealed record WeedIcon
{
    public string? Glyph { get; init; }

    public string? Path { get; init; }

    public static WeedIcon FromGlyph(string glyph) => new() { Glyph = glyph };

    public static WeedIcon FromPath(string path) => new() { Path = path };
}

public enum PluginSettingKind
{
    Boolean,
    Integer,
    Text,
    Select,
    Path
}

public sealed record PluginSettingOption
{
    public required string Value { get; init; }

    public required string Label { get; init; }
}

public sealed record PluginSettingDefinition
{
    public required string Key { get; init; }

    public required string Label { get; init; }

    public PluginSettingKind Kind { get; init; }

    public string? Description { get; init; }

    public string? DefaultValue { get; init; }

    public int? Min { get; init; }

    public int? Max { get; init; }

    public IReadOnlyList<PluginSettingOption> Options { get; init; } = [];
}

public sealed record ScreenCaptureResult
{
    public required string FilePath { get; init; }

    public byte[]? ImagePng { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    public bool CopiedToClipboard { get; init; }
}

public enum ClipboardContentKind
{
    Text,
    Image,
    Files,
    Rtf,
    Html
}

public sealed record ClipboardSnapshot
{
    public required ClipboardContentKind Kind { get; init; }

    public string? TextContent { get; init; }

    public IReadOnlyList<string> Files { get; init; } = [];

    public byte[]? ImagePng { get; init; }

    public string? Rtf { get; init; }

    public string? Html { get; init; }

    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.Now;
}
