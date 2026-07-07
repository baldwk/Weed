using Weed.Abstractions;
using IQueryProvider = Weed.Abstractions.IQueryProvider;

namespace Weed.Plugins.Screenshot;

public sealed class ScreenshotPlugin : IWeedPlugin, IQueryProvider, ICommandHandler, IPluginSettingsProvider
{
    public const string PluginId = "weed.screenshot";
    private IWeedHost? _host;

    public string ProviderId => "screenshot";

    public static WeedPluginManifest Manifest => new()
    {
        Id = PluginId,
        Name = "Screenshot",
        Version = "0.1.0",
        SdkVersion = "0.1",
        Icon = "assets/plugins/screenshot.png",
        Activations =
        [
            new PluginActivationManifest
            {
                Type = "keyword",
                Keyword = "shot",
                Command = "screenshot.open"
            },
            new PluginActivationManifest
            {
                Type = "hotkey",
                Command = "screenshot.region",
                DefaultKeys = "Shift+Alt+A",
                Configurable = true,
                Behavior = "executeCommand"
            }
        ],
        Permissions =
        [
            "screen.capture",
            "clipboard.write",
            "file.write"
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
            Key = "defaultSaveDirectory",
            Label = "Save directory",
            Kind = PluginSettingKind.Path,
            DefaultValue = string.Empty
        },
        new()
        {
            Key = "defaultFormat",
            Label = "Default format",
            Kind = PluginSettingKind.Select,
            DefaultValue = "png",
            Options =
            [
                new PluginSettingOption { Value = "png", Label = "PNG" },
                new PluginSettingOption { Value = "jpg", Label = "JPEG" }
            ]
        },
        new()
        {
            Key = "jpegQuality",
            Label = "JPEG quality",
            Kind = PluginSettingKind.Integer,
            DefaultValue = "90",
            Min = 1,
            Max = 100
        },
        new()
        {
            Key = "defaultColor",
            Label = "Annotation color",
            Kind = PluginSettingKind.Select,
            DefaultValue = "Red",
            Options =
            [
                new PluginSettingOption { Value = "Red", Label = "Red" },
                new PluginSettingOption { Value = "Yellow", Label = "Yellow" },
                new PluginSettingOption { Value = "Green", Label = "Green" },
                new PluginSettingOption { Value = "Blue", Label = "Blue" },
                new PluginSettingOption { Value = "White", Label = "White" }
            ]
        },
        new()
        {
            Key = "defaultLineWidth",
            Label = "Line width",
            Kind = PluginSettingKind.Integer,
            DefaultValue = "4",
            Min = 2,
            Max = 18
        }
    ];

    public ValueTask<IReadOnlyList<WeedResult>> QueryAsync(QueryContext context, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult<IReadOnlyList<WeedResult>>(
        [
            new WeedResult
            {
                Id = "screenshot-region",
                PluginId = PluginId,
                Title = "Capture region",
                Subtitle = "Select an area, save PNG, and copy it to the clipboard",
                    Icon = WeedIcon.FromPath(PluginIconPath()),
                MatchScore = 28,
                DefaultCommand = "screenshot.region",
                Actions =
                [
                    new WeedAction { Command = "screenshot.region", Title = "Capture region", Shortcut = "Enter" },
                    new WeedAction { Command = "screenshot.fullscreen", Title = "Capture primary screen" }
                ]
            },
            new WeedResult
            {
                Id = "screenshot-fullscreen",
                PluginId = PluginId,
                Title = "Capture primary screen",
                Subtitle = "Save PNG and copy it to the clipboard",
                    Icon = WeedIcon.FromPath(PluginIconPath()),
                MatchScore = 20,
                DefaultCommand = "screenshot.fullscreen",
                Actions =
                [
                    new WeedAction { Command = "screenshot.fullscreen", Title = "Capture primary screen", Shortcut = "Enter" },
                    new WeedAction { Command = "screenshot.scrolling", Title = "Capture scrolling area" }
                ]
            },
            new WeedResult
            {
                Id = "screenshot-scrolling",
                PluginId = PluginId,
                Title = "Capture scrolling area",
                Subtitle = "Select a scrollable area, capture frames, stitch, and edit",
                Icon = WeedIcon.FromPath(PluginIconPath()),
                MatchScore = 18,
                DefaultCommand = "screenshot.scrolling",
                Actions =
                [
                    new WeedAction { Command = "screenshot.scrolling", Title = "Capture scrolling area", Shortcut = "Enter" }
                ]
            }
        ]);
    }

    public async ValueTask<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (_host is null)
        {
            return CommandResult.Failed("Screenshot is not initialized.");
        }

        ScreenCaptureResult? capture = context.Command switch
        {
            "screenshot.open" or "screenshot.region" => await _host.ScreenCapture.CaptureRegionInteractiveAsync(cancellationToken),
            "screenshot.fullscreen" => await _host.ScreenCapture.CapturePrimaryScreenAsync(cancellationToken),
            "screenshot.scrolling" => await _host.ScreenCapture.CaptureScrollingInteractiveAsync(cancellationToken),
            _ => null
        };

        if (capture is null)
        {
            return CommandResult.Ok(CommandBehavior.KeepLauncherOpen, "Screenshot cancelled.");
        }

        return CommandResult.Ok(
            CommandBehavior.CloseLauncher,
            capture.CopiedToClipboard ? "Screenshot copied." : "Screenshot saved.");
    }

    private static string PluginIconPath() =>
        Path.Combine(AppContext.BaseDirectory, "assets", "plugins", "screenshot.png");
}
