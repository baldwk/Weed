using System.Text;
using System.Text.Json;
using Weed.Abstractions;
using IQueryProvider = Weed.Abstractions.IQueryProvider;

namespace Weed.Plugins.Toolbox;

public sealed class ToolboxPlugin : IWeedPlugin, IQueryProvider, ICommandHandler, IPluginSettingsProvider
{
    public const string PluginId = "weed.toolbox";
    private const int MaxInputLength = 65_536;
    private const string CopyCommand = "toolbox.copy";
    private const string PasteCommand = "toolbox.paste";
    private const string CopySourceCommand = "toolbox.copySource";
    private const string SelectOperationCommand = "toolbox.selectOperation";

    private static readonly ToolDefinition[] ToolDefinitions =
    [
        new(ToolKind.Uuid, "uuidCommand", "uuid", "UUID command"),
        new(ToolKind.Timestamp, "timestampCommand", "timestamp", "Timestamp command"),
        new(ToolKind.Base64, "base64Command", "base64", "Base64 command"),
        new(ToolKind.Url, "urlCommand", "url", "URL command"),
        new(ToolKind.Hash, "hashCommand", "hash", "Hash command"),
        new(ToolKind.Json, "jsonCommand", "json", "JSON command")
    ];

    private readonly HashSet<string> _warnedSettings = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _warningGate = new();
    private IWeedHost? _host;

    public string ProviderId => "toolbox";

    public static WeedPluginManifest Manifest => new()
    {
        Id = PluginId,
        Name = "Toolbox",
        Version = "0.1.0",
        SdkVersion = "0.1",
        Assembly = "Weed.Plugins.Toolbox.dll",
        EntryType = "Weed.Plugins.Toolbox.ToolboxPlugin",
        Runtime = new PluginRuntimeManifest { Resident = false },
        Activations =
        [
            new PluginActivationManifest
            {
                Type = "implicitQuery",
                Provider = "toolbox"
            }
        ],
        Permissions = ["clipboard.write", "window.paste"]
    };

    public ValueTask InitializeAsync(IWeedHost host, CancellationToken cancellationToken)
    {
        _host = host;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public IReadOnlyList<PluginSettingDefinition> GetSettings() => ToolDefinitions
        .Select(definition => new PluginSettingDefinition
        {
            Key = definition.SettingKey,
            Label = definition.SettingLabel,
            Kind = PluginSettingKind.Text,
            Description = "One unique word. Changes take effect immediately.",
            DefaultValue = definition.DefaultCommand
        })
        .ToArray();

    public ValueTask<IReadOnlyList<WeedResult>> QueryAsync(QueryContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_host is null)
        {
            return ValueTask.FromResult<IReadOnlyList<WeedResult>>([]);
        }

        var input = SplitFirst(context.RawText);
        if (string.IsNullOrEmpty(input.Token))
        {
            return ValueTask.FromResult<IReadOnlyList<WeedResult>>([]);
        }

        var command = NormalizeToken(input.Token);
        var matches = GetBindings()
            .Where(binding => binding.NormalizedCommand.Equals(command, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length == 0)
        {
            return ValueTask.FromResult<IReadOnlyList<WeedResult>>([]);
        }

        if (matches.Length > 1)
        {
            var tools = string.Join(", ", matches.Select(match => match.Definition.DefaultCommand));
            return Results(ErrorResult(
                "configuration-conflict",
                "Tool command conflict",
                $"'{matches[0].DisplayCommand}' is assigned to: {tools}. Change it in Toolbox settings."));
        }

        if (context.RawText.Length > MaxInputLength)
        {
            return Results(ErrorResult(
                "input-too-long",
                "Toolbox input is too long",
                $"The maximum input length is {MaxInputLength:N0} characters."));
        }

        var binding = matches[0];
        try
        {
            return ValueTask.FromResult<IReadOnlyList<WeedResult>>(binding.Definition.Kind switch
            {
                ToolKind.Uuid => QueryUuid(input.Remainder),
                ToolKind.Timestamp => QueryTimestamp(input.Remainder),
                ToolKind.Base64 => QueryBase64(binding, input.Remainder),
                ToolKind.Url => QueryUrl(binding, input.Remainder),
                ToolKind.Hash => QueryHash(binding, input.Remainder),
                ToolKind.Json => QueryJson(binding, input.Remainder),
                _ => []
            });
        }
        catch (JsonException ex)
        {
            return Results(ErrorResult("invalid-json", "Invalid JSON", ex.Message));
        }
        catch (UriFormatException ex)
        {
            return Results(ErrorResult("invalid-url", "Invalid URL encoding", ex.Message));
        }
        catch (FormatException ex)
        {
            return Results(ErrorResult("invalid-format", "Invalid input", ex.Message));
        }
        catch (DecoderFallbackException)
        {
            return Results(ErrorResult("invalid-utf8", "Invalid UTF-8 text", "The decoded bytes are not valid UTF-8 text."));
        }
    }

    public async ValueTask<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (_host is null)
        {
            return CommandResult.Failed("Toolbox is not initialized.");
        }

        switch (context.Command)
        {
            case SelectOperationCommand:
                if (!context.Data.TryGetValue("nextQuery", out var nextQuery))
                {
                    return CommandResult.Failed("The next Toolbox query is missing.");
                }

                return new CommandResult
                {
                    Succeeded = true,
                    Behavior = CommandBehavior.ShowLauncher,
                    InitialQuery = nextQuery
                };
            case CopyCommand:
                if (!context.Data.TryGetValue("value", out var copyValue))
                {
                    return CommandResult.Failed("The Toolbox result is missing.");
                }

                await _host.Clipboard.SetTextAsync(copyValue, cancellationToken);
                return CommandResult.Ok(message: "Copied result.");
            case PasteCommand:
                if (!context.Data.TryGetValue("value", out var pasteValue))
                {
                    return CommandResult.Failed("The Toolbox result is missing.");
                }

                await _host.Clipboard.PasteTextAsync(pasteValue, cancellationToken);
                return CommandResult.Ok(message: "Pasted result.");
            case CopySourceCommand:
                if (!context.Data.TryGetValue("source", out var source))
                {
                    return CommandResult.Failed("The Toolbox source is missing.");
                }

                await _host.Clipboard.SetTextAsync(source, cancellationToken);
                return CommandResult.Ok(message: "Copied source.");
            default:
                return CommandResult.Failed($"Unknown Toolbox command: {context.Command}");
        }
    }

    private IReadOnlyList<WeedResult> QueryUuid(string arguments)
    {
        if (!string.IsNullOrWhiteSpace(arguments))
        {
            return [ErrorResult("uuid-usage", "Usage: uuid", "UUID does not accept arguments.")];
        }

        return
        [
            ValueResult(
                "uuid.v4",
                Guid.NewGuid().ToString("D"),
                "UUID v4 - Enter copies the value",
                source: null)
        ];
    }

    private IReadOnlyList<WeedResult> QueryTimestamp(string arguments)
    {
        var value = arguments.Trim();
        if (value.Length == 0)
        {
            var now = DateTimeOffset.UtcNow;
            return
            [
                ValueResult("timestamp.current.milliseconds", now.ToUnixTimeMilliseconds().ToString(), "Unix timestamp in milliseconds", null, 30),
                ValueResult("timestamp.current.seconds", now.ToUnixTimeSeconds().ToString(), "Unix timestamp in seconds", null, 27)
            ];
        }

        if (ToolboxTransforms.TryParseUnixTimestamp(value, out var timestamp, out var timestampError))
        {
            return
            [
                ValueResult("timestamp.toDate.local", ToolboxTransforms.FormatLocalTime(timestamp), "Local time", value, 30),
                ValueResult("timestamp.toDate.utc", ToolboxTransforms.FormatUtcTime(timestamp), "UTC", value, 27)
            ];
        }

        if (timestampError is not null)
        {
            return [ErrorResult("timestamp-range", "Invalid timestamp", timestampError)];
        }

        if (ToolboxTransforms.LooksNumeric(value))
        {
            return [ErrorResult("timestamp-digits", "Invalid timestamp", "Use a 13-digit millisecond or 10-digit second timestamp.")];
        }

        if (!ToolboxTransforms.TryParseDate(value, out timestamp))
        {
            return
            [
                ErrorResult(
                    "timestamp-date",
                    "Invalid date",
                    "Use ISO 8601, yyyy-MM-dd HH:mm:ss, or yyyy-MM-dd.")
            ];
        }

        return
        [
            ValueResult("timestamp.fromDate.milliseconds", timestamp.ToUnixTimeMilliseconds().ToString(), "Unix timestamp in milliseconds", value, 30),
            ValueResult("timestamp.fromDate.seconds", timestamp.ToUnixTimeSeconds().ToString(), "Unix timestamp in seconds", value, 27)
        ];
    }

    private IReadOnlyList<WeedResult> QueryBase64(CommandBinding binding, string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return OperationMenu(binding,
            [
                new OperationDefinition("encode", "Encode", "Encode UTF-8 text as Base64"),
                new OperationDefinition("decode", "Decode", "Decode Base64 as UTF-8 text")
            ]);
        }

        var operation = SplitFirst(arguments);
        if (operation.Remainder.Length == 0)
        {
            return [OperationUsage(binding, operation.Token, "encode|decode", "text")];
        }

        var normalizedOperation = NormalizeToken(operation.Token);
        var result = normalizedOperation switch
        {
            "encode" => ToolboxTransforms.Base64Encode(operation.Remainder),
            "decode" => ToolboxTransforms.Base64Decode(operation.Remainder),
            _ => throw new FormatException($"Unknown Base64 operation '{operation.Token}'. Use encode or decode.")
        };
        return [ValueResult($"base64.{normalizedOperation}", result, $"Base64 {normalizedOperation} result", operation.Remainder)];
    }

    private IReadOnlyList<WeedResult> QueryUrl(CommandBinding binding, string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return OperationMenu(binding,
            [
                new OperationDefinition("encode", "Encode", "Percent-encode a URL component"),
                new OperationDefinition("decode", "Decode", "Decode percent-encoded text")
            ]);
        }

        var operation = SplitFirst(arguments);
        if (operation.Remainder.Length == 0)
        {
            return [OperationUsage(binding, operation.Token, "encode|decode", "text")];
        }

        var normalizedOperation = NormalizeToken(operation.Token);
        var result = normalizedOperation switch
        {
            "encode" => ToolboxTransforms.UrlEncode(operation.Remainder),
            "decode" => ToolboxTransforms.UrlDecode(operation.Remainder),
            _ => throw new FormatException($"Unknown URL operation '{operation.Token}'. Use encode or decode.")
        };
        return [ValueResult($"url.{normalizedOperation}", result, $"URL {normalizedOperation} result", operation.Remainder)];
    }

    private IReadOnlyList<WeedResult> QueryHash(CommandBinding binding, string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return OperationMenu(binding,
            [
                new OperationDefinition("sha256", "SHA-256", "Hash UTF-8 text with SHA-256"),
                new OperationDefinition("sha512", "SHA-512", "Hash UTF-8 text with SHA-512"),
                new OperationDefinition("sha1", "SHA-1", "Compatibility only - not secure"),
                new OperationDefinition("md5", "MD5", "Compatibility only - not secure")
            ]);
        }

        var operation = SplitFirst(arguments);
        if (operation.Remainder.Length == 0)
        {
            return [OperationUsage(binding, operation.Token, "sha256|sha512|sha1|md5", "text")];
        }

        var algorithm = NormalizeToken(operation.Token);
        if (algorithm is not ("sha256" or "sha512" or "sha1" or "md5"))
        {
            throw new FormatException($"Unknown hash algorithm '{operation.Token}'.");
        }

        var warning = algorithm is "sha1" or "md5" ? " - compatibility only, not secure" : string.Empty;
        return
        [
            ValueResult(
                $"hash.{algorithm}",
                ToolboxTransforms.Hash(algorithm, operation.Remainder),
                $"{algorithm.ToUpperInvariant()} hash{warning}",
                operation.Remainder)
        ];
    }

    private IReadOnlyList<WeedResult> QueryJson(CommandBinding binding, string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return OperationMenu(binding,
            [
                new OperationDefinition("format", "Format", "Format JSON with two-space indentation"),
                new OperationDefinition("minify", "Minify", "Remove insignificant JSON whitespace")
            ]);
        }

        var operation = SplitFirst(arguments);
        if (operation.Remainder.Length == 0)
        {
            return [OperationUsage(binding, operation.Token, "format|minify", "JSON")];
        }

        var normalizedOperation = NormalizeToken(operation.Token);
        var formatted = normalizedOperation switch
        {
            "format" => ToolboxTransforms.FormatJson(operation.Remainder, indented: true),
            "minify" => ToolboxTransforms.FormatJson(operation.Remainder, indented: false),
            _ => throw new FormatException($"Unknown JSON operation '{operation.Token}'. Use format or minify.")
        };
        return [ValueResult($"json.{normalizedOperation}", formatted, $"JSON {normalizedOperation} result", operation.Remainder)];
    }

    private IReadOnlyList<WeedResult> OperationMenu(CommandBinding binding, IReadOnlyList<OperationDefinition> operations) =>
        operations.Select((operation, index) => new WeedResult
        {
            Id = $"{binding.Definition.DefaultCommand}.menu.{operation.Command}",
            PluginId = PluginId,
            Title = operation.Title,
            Subtitle = operation.Description,
            Icon = WeedIcon.FromGlyph(">"),
            MatchScore = Math.Max(1, 30 - index * 3),
            DefaultCommand = SelectOperationCommand,
            Actions =
            [
                new WeedAction { Command = SelectOperationCommand, Title = "Select", Shortcut = "Enter" }
            ],
            Data = new Dictionary<string, string>
            {
                ["nextQuery"] = $"{binding.DisplayCommand} {operation.Command} "
            }
        }).ToArray();

    private static WeedResult OperationUsage(CommandBinding binding, string operation, string expectedOperations, string valueLabel)
    {
        var normalizedOperation = NormalizeToken(operation);
        return string.IsNullOrEmpty(normalizedOperation) || !expectedOperations.Split('|').Contains(normalizedOperation, StringComparer.Ordinal)
            ? ErrorResult(
                $"{binding.Definition.DefaultCommand}-operation",
                $"Unknown {binding.Definition.DefaultCommand} operation",
                $"Use: {binding.DisplayCommand} {expectedOperations.Replace('|', ' ')} <{valueLabel}>")
            : ErrorResult(
                $"{binding.Definition.DefaultCommand}-{normalizedOperation}-usage",
                $"Enter {valueLabel} to continue",
                $"Usage: {binding.DisplayCommand} {normalizedOperation} <{valueLabel}>");
    }

    private static WeedResult ValueResult(string id, string value, string subtitle, string? source, double matchScore = 30)
    {
        var actions = new List<WeedAction>
        {
            new() { Command = CopyCommand, Title = "Copy result", Shortcut = "Enter" },
            new() { Command = PasteCommand, Title = "Paste result" }
        };
        if (source is not null)
        {
            actions.Add(new WeedAction { Command = CopySourceCommand, Title = "Copy source" });
        }

        var data = new Dictionary<string, string> { ["value"] = value };
        if (source is not null)
        {
            data["source"] = source;
        }

        return new WeedResult
        {
            Id = id,
            PluginId = PluginId,
            Title = value,
            Subtitle = subtitle,
            Icon = WeedIcon.FromGlyph("#"),
            MatchScore = matchScore,
            DefaultCommand = CopyCommand,
            Actions = actions,
            Data = data
        };
    }

    private static WeedResult ErrorResult(string id, string title, string subtitle) => new()
    {
        Id = $"toolbox.error.{id}",
        PluginId = PluginId,
        Title = title,
        Subtitle = subtitle,
        Icon = WeedIcon.FromGlyph("!"),
        MatchScore = 30,
        DefaultCommand = "__noop"
    };

    private CommandBinding[] GetBindings()
    {
        return ToolDefinitions.Select(definition =>
        {
            var configured = _host!.Settings.GetPluginSetting(PluginId, definition.SettingKey, definition.DefaultCommand);
            var trimmed = configured?.Trim() ?? string.Empty;
            if (trimmed.Length == 0 || trimmed.Any(char.IsWhiteSpace))
            {
                WarnInvalidSettingOnce(definition, configured);
                trimmed = definition.DefaultCommand;
            }

            return new CommandBinding(definition, trimmed, NormalizeToken(trimmed));
        }).ToArray();
    }

    private void WarnInvalidSettingOnce(ToolDefinition definition, string? configured)
    {
        lock (_warningGate)
        {
            if (!_warnedSettings.Add(definition.SettingKey))
            {
                return;
            }
        }

        _host?.Logger.Warn(
            $"Invalid Toolbox setting {definition.SettingKey}; using default command '{definition.DefaultCommand}'.");
    }

    private static ParsedInput SplitFirst(string value)
    {
        var start = 0;
        while (start < value.Length && char.IsWhiteSpace(value[start]))
        {
            start++;
        }

        var end = start;
        while (end < value.Length && !char.IsWhiteSpace(value[end]))
        {
            end++;
        }

        var remainderStart = end;
        while (remainderStart < value.Length && char.IsWhiteSpace(value[remainderStart]))
        {
            remainderStart++;
        }

        return new ParsedInput(
            start < end ? value[start..end] : string.Empty,
            remainderStart < value.Length ? value[remainderStart..] : string.Empty);
    }

    private static string NormalizeToken(string value) =>
        value.Normalize(NormalizationForm.FormKC).Trim().ToLowerInvariant();

    private static ValueTask<IReadOnlyList<WeedResult>> Results(params WeedResult[] results) =>
        ValueTask.FromResult<IReadOnlyList<WeedResult>>(results);

    private enum ToolKind
    {
        Uuid,
        Timestamp,
        Base64,
        Url,
        Hash,
        Json
    }

    private sealed record ToolDefinition(ToolKind Kind, string SettingKey, string DefaultCommand, string SettingLabel);

    private sealed record CommandBinding(ToolDefinition Definition, string DisplayCommand, string NormalizedCommand);

    private sealed record OperationDefinition(string Command, string Title, string Description);

    private readonly record struct ParsedInput(string Token, string Remainder);
}
