using Weed.Abstractions;
using IQueryProvider = Weed.Abstractions.IQueryProvider;

namespace Example.Plugin;

public sealed class ExamplePlugin : IWeedPlugin, IQueryProvider, ICommandHandler
{
    private IWeedHost? _host;

    public string ProviderId => "example";

    public ValueTask InitializeAsync(IWeedHost host, CancellationToken cancellationToken)
    {
        _host = host;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public ValueTask<IReadOnlyList<WeedResult>> QueryAsync(QueryContext context, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult<IReadOnlyList<WeedResult>>(
        [
            new WeedResult
            {
                Id = "example-result",
                PluginId = "example.plugin",
                Title = $"Example: {context.RawText}",
                Subtitle = "Copies the query text",
                Icon = WeedIcon.FromGlyph("*"),
                MatchScore = 20,
                DefaultCommand = "example.copy",
                Data = new Dictionary<string, string>
                {
                    ["text"] = context.RawText
                }
            }
        ]);
    }

    public async ValueTask<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (_host is null)
        {
            return CommandResult.Failed("Plugin is not initialized.");
        }

        await _host.Clipboard.SetTextAsync(context.Data.GetValueOrDefault("text", ""), cancellationToken);
        return CommandResult.Ok(message: "Copied.");
    }
}
