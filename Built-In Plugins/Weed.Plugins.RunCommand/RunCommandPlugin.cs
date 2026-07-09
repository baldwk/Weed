using Weed.Abstractions;
using IQueryProvider = Weed.Abstractions.IQueryProvider;

namespace Weed.Plugins.RunCommand;

public sealed class RunCommandPlugin : IWeedPlugin, IQueryProvider, ICommandHandler
{
    public const string PluginId = "weed.runCommand";
    private static readonly RunCommandEntry[] Commands =
    [
        new("cmd", "Command Prompt", "cmd.exe"),
        new("regedit", "Registry Editor", "regedit.exe"),
        new("taskmgr", "Task Manager", "taskmgr.exe"),
        new("services.msc", "Services", "services.msc"),
        new("devmgmt.msc", "Device Manager", "devmgmt.msc"),
        new("diskmgmt.msc", "Disk Management", "diskmgmt.msc"),
        new("control", "Control Panel", "control"),
        new("appwiz.cpl", "Programs and Features", "appwiz.cpl"),
        new("ncpa.cpl", "Network Connections", "ncpa.cpl"),
        new("sysdm.cpl", "System Properties", "sysdm.cpl"),
        new("mstsc", "Remote Desktop Connection", "mstsc"),
        new("notepad", "Notepad", "notepad"),
        new("calc", "Calculator", "calc"),
        new("explorer", "File Explorer", "explorer")
    ];

    private IWeedHost? _host;

    public string ProviderId => "runCommand";

    public static WeedPluginManifest Manifest => new()
    {
        Id = PluginId,
        Name = "Run Command",
        Version = "0.1.0",
        SdkVersion = "0.1",
        Icon = "assets/plugins/run-command.png",
        Activations =
        [
            new PluginActivationManifest
            {
                Type = "implicitQuery",
                Provider = "runCommand"
            }
        ],
        Permissions =
        [
            "shell.launch"
        ]
    };

    public ValueTask InitializeAsync(IWeedHost host, CancellationToken cancellationToken)
    {
        _host = host;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public ValueTask<IReadOnlyList<WeedResult>> QueryAsync(QueryContext context, CancellationToken cancellationToken)
    {
        var query = Normalize(context.NormalizedText);
        if (query.Length == 0)
        {
            return ValueTask.FromResult<IReadOnlyList<WeedResult>>([]);
        }

        var results = Commands
            .Select(command => (Command: command, Score: Score(command, query)))
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Command.Alias, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .Select(item => ToResult(item.Command, item.Score))
            .ToArray();

        return ValueTask.FromResult<IReadOnlyList<WeedResult>>(results);
    }

    public async ValueTask<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (_host is null)
        {
            return CommandResult.Failed("Run Command is not initialized.");
        }

        if (!context.Data.TryGetValue("alias", out var alias) ||
            Commands.FirstOrDefault(command => command.Alias.Equals(alias, StringComparison.OrdinalIgnoreCase)) is not { } command)
        {
            return CommandResult.Failed("Run command is not in the built-in command table.");
        }

        await _host.Shell.OpenAsync(command.Executable, cancellationToken);
        return CommandResult.Ok(message: $"Opened {command.Alias}.");
    }

    private static WeedResult ToResult(RunCommandEntry command, double score) => new()
    {
        Id = $"run-{command.Alias}",
        PluginId = PluginId,
        Title = command.Alias,
        Subtitle = command.Title,
        Icon = WeedIcon.FromPath(PluginIconPath()),
        MatchScore = Math.Clamp(score, 1, 30),
        DefaultCommand = "run.open",
        Actions =
        [
            new WeedAction { Command = "run.open", Title = "Open", Shortcut = "Enter" }
        ],
        Data = new Dictionary<string, string>
        {
            ["alias"] = command.Alias,
            ["executable"] = command.Executable
        }
    };

    private static double Score(RunCommandEntry command, string query)
    {
        var alias = Normalize(command.Alias);
        var title = Normalize(command.Title);
        if (alias.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            return 30;
        }

        if (title.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            return 28;
        }

        if (alias.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 24;
        }

        if (title.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 20;
        }

        if (alias.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 14;
        }

        return title.Contains(query, StringComparison.OrdinalIgnoreCase) ? 10 : 0;
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();

    private static string PluginIconPath() =>
        Path.Combine(AppContext.BaseDirectory, "assets", "plugins", "run-command.png");

    private sealed record RunCommandEntry(string Alias, string Title, string Executable);
}
