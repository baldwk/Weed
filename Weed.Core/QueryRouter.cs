using Weed.Abstractions;
using IQueryProvider = Weed.Abstractions.IQueryProvider;

namespace Weed.Core;

public sealed record LoadedPlugin(
    WeedPluginManifest Manifest,
    IWeedPlugin Instance,
    IQueryProvider? QueryProvider,
    ICommandHandler? CommandHandler,
    IResidentPlugin? ResidentPlugin);

public sealed record RankedResult(
    WeedResult Result,
    string PluginName,
    double FinalScore,
    double UsageScore,
    double PriorityScore,
    int PluginOrder,
    int ResultOrder,
    DateTimeOffset? LastSelectedAt);

public sealed class QueryRouter
{
    private readonly SettingsRepository _settings;
    private readonly UsageHistoryStore _usage;
    private readonly IWeedLogger? _logger;
    private readonly List<LoadedPlugin> _plugins = [];

    public QueryRouter(SettingsRepository settings, UsageHistoryStore usage, IWeedLogger? logger = null)
    {
        _settings = settings;
        _usage = usage;
        _logger = logger;
    }

    public IReadOnlyList<LoadedPlugin> Plugins => _plugins;

    public void SetPlugins(IEnumerable<LoadedPlugin> plugins)
    {
        _plugins.Clear();
        _plugins.AddRange(plugins);
    }

    public async ValueTask<IReadOnlyList<RankedResult>> QueryAsync(string rawText, CancellationToken cancellationToken)
    {
        var normalized = TextNormalizer.Normalize(rawText);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return [];
        }

        var keywordMatch = TryMatchKeyword(normalized);
        if (keywordMatch is not null)
        {
            var (plugin, activation, remaining) = keywordMatch.Value;
            var context = new QueryContext
            {
                RawText = remaining.Raw,
                NormalizedText = remaining.Normalized,
                Keyword = activation.Keyword,
                Command = activation.Command,
                Activation = QueryActivation.ForKeyword(activation.Keyword!, activation.Command!)
            };

            return await QueryPluginAsync(plugin, context, 0, includePriority: false, cancellationToken);
        }

        var implicitPlugins = _plugins
            .Select((Plugin, Index) => (Plugin, Index))
            .Where(p => _settings.IsPluginEnabled(p.Plugin.Manifest.Id) &&
                        p.Plugin.QueryProvider is not null &&
                        p.Plugin.Manifest.Activations.Any(a => a.Type.Equals("implicitQuery", StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        var results = new List<RankedResult>();
        foreach (var (plugin, index) in implicitPlugins)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var activation = plugin.Manifest.Activations.FirstOrDefault(a =>
                a.Type.Equals("implicitQuery", StringComparison.OrdinalIgnoreCase));
            var context = new QueryContext
            {
                RawText = rawText,
                NormalizedText = normalized,
                Activation = QueryActivation.ForImplicit(activation?.Provider ?? plugin.QueryProvider!.ProviderId)
            };
            results.AddRange(await QueryPluginAsync(plugin, context, index, includePriority: true, cancellationToken));
        }

        return Sort(results);
    }

    public async ValueTask<CommandResult> ExecuteAsync(WeedResult result, string command, CancellationToken cancellationToken)
    {
        var plugin = _plugins.FirstOrDefault(p => p.Manifest.Id.Equals(result.PluginId, StringComparison.OrdinalIgnoreCase));
        if (plugin?.CommandHandler is null)
        {
            return CommandResult.Failed($"No command handler for {result.PluginId}.");
        }

        var context = new CommandContext
        {
            PluginId = result.PluginId,
            Command = command,
            ResultId = result.Id,
            Data = result.Data
        };
        var commandResult = await plugin.CommandHandler.ExecuteAsync(context, cancellationToken);
        if (commandResult.Succeeded)
        {
            _usage.Record(result.PluginId, result.Id, command);
        }

        return commandResult;
    }

    public async ValueTask<CommandResult> ExecutePluginCommandAsync(
        string pluginId,
        string command,
        IReadOnlyDictionary<string, string>? data,
        CancellationToken cancellationToken)
    {
        var plugin = _plugins.FirstOrDefault(p => p.Manifest.Id.Equals(pluginId, StringComparison.OrdinalIgnoreCase));
        if (plugin?.CommandHandler is null)
        {
            return CommandResult.Failed($"No command handler for {pluginId}.");
        }

        return await plugin.CommandHandler.ExecuteAsync(new CommandContext
        {
            PluginId = pluginId,
            Command = command,
            Data = data ?? new Dictionary<string, string>()
        }, cancellationToken);
    }

    private (LoadedPlugin Plugin, PluginActivationManifest Activation, (string Raw, string Normalized) Remaining)? TryMatchKeyword(string normalized)
    {
        var firstSpace = normalized.IndexOf(' ');
        var first = firstSpace >= 0 ? normalized[..firstSpace] : normalized;
        var remainingNormalized = firstSpace >= 0 ? normalized[(firstSpace + 1)..].Trim() : string.Empty;

        foreach (var plugin in _plugins.Where(p => _settings.IsPluginEnabled(p.Manifest.Id)))
        {
            foreach (var activation in plugin.Manifest.Activations.Where(a => a.Type.Equals("keyword", StringComparison.OrdinalIgnoreCase)))
            {
                if (activation.Keyword is null || activation.Command is null)
                {
                    continue;
                }

                if (TextNormalizer.Normalize(activation.Keyword).Equals(first, StringComparison.OrdinalIgnoreCase))
                {
                    return (plugin, activation, (remainingNormalized, remainingNormalized));
                }
            }
        }

        return null;
    }

    private async ValueTask<IReadOnlyList<RankedResult>> QueryPluginAsync(
        LoadedPlugin plugin,
        QueryContext context,
        int pluginOrder,
        bool includePriority,
        CancellationToken cancellationToken)
    {
        if (plugin.QueryProvider is null || !_settings.IsPluginEnabled(plugin.Manifest.Id))
        {
            return [];
        }

        try
        {
            var results = await plugin.QueryProvider.QueryAsync(context, cancellationToken);
            return results
                .Where(r => r.MatchScore > 0)
                .Select((result, index) =>
                {
                    var usage = _usage.GetScore(result.PluginId, result.Id, result.DefaultCommand);
                    var priority = includePriority ? _settings.GetPluginPriority(result.PluginId) : 0;
                    return new RankedResult(
                        result,
                        plugin.Manifest.Name,
                        Math.Clamp(result.MatchScore, 0, 30) + usage + priority,
                        usage,
                        priority,
                        pluginOrder,
                        index,
                        _usage.GetLastSelectedAt(result.PluginId, result.Id, result.DefaultCommand));
                })
                .ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.Error($"Plugin query failed: {plugin.Manifest.Id}", ex);
            return
            [
                new RankedResult(
                    new WeedResult
                    {
                        Id = $"error-{plugin.Manifest.Id}",
                        PluginId = plugin.Manifest.Id,
                        Title = $"{plugin.Manifest.Name} query failed",
                        Subtitle = "Open logs for diagnostic details.",
                        Icon = WeedIcon.FromGlyph("!"),
                        MatchScore = 1,
                        DefaultCommand = "__noop"
                    },
                    plugin.Manifest.Name,
                    1,
                    0,
                    0,
                    pluginOrder,
                    0,
                    null)
            ];
        }
    }

    private static IReadOnlyList<RankedResult> Sort(IEnumerable<RankedResult> results) =>
        results
            .OrderByDescending(r => r.FinalScore)
            .ThenByDescending(r => r.LastSelectedAt)
            .ThenBy(r => r.PluginOrder)
            .ThenBy(r => r.ResultOrder)
            .ThenBy(r => r.Result.PluginId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Result.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string ActivationLabel(PluginActivationManifest activation) =>
        activation.Type.ToLowerInvariant() switch
        {
            "keyword" => $"{activation.Keyword} ...",
            "hotkey" => activation.DefaultKeys ?? activation.Command ?? "hotkey",
            "implicitquery" => "implicit",
            _ => activation.Type
        };
}
