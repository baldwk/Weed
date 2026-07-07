namespace Weed.Abstractions;

public interface IWeedPlugin
{
    ValueTask InitializeAsync(IWeedHost host, CancellationToken cancellationToken);

    ValueTask DisposeAsync();
}

public interface IQueryProvider
{
    string ProviderId { get; }

    ValueTask<IReadOnlyList<WeedResult>> QueryAsync(QueryContext context, CancellationToken cancellationToken);
}

public interface ICommandHandler
{
    ValueTask<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken);
}

public interface IResidentPlugin
{
    ValueTask StartAsync(CancellationToken cancellationToken);

    ValueTask StopAsync(CancellationToken cancellationToken);
}

public interface IPluginSettingsProvider
{
    IReadOnlyList<PluginSettingDefinition> GetSettings();
}

public interface IWeedHost
{
    IWeedLogger Logger { get; }

    IWeedSettings Settings { get; }

    IWeedStorage Storage { get; }

    IWeedClipboard Clipboard { get; }

    IWeedShell Shell { get; }

    IWeedWindowService Windows { get; }

    IWeedScreenCapture ScreenCapture { get; }
}

public interface IWeedLogger
{
    void Info(string message);

    void Warn(string message);

    void Error(string message, Exception? exception = null);
}

public interface IWeedSettings
{
    T GetPluginSetting<T>(string pluginId, string key, T defaultValue);

    void SetPluginSetting<T>(string pluginId, string key, T value);
}

public interface IWeedStorage
{
    string GetPluginDataDirectory(string pluginId);

    string GetPluginCacheDirectory(string pluginId);
}

public interface IWeedClipboard
{
    ValueTask<ClipboardSnapshot?> TryReadAsync(CancellationToken cancellationToken);

    ValueTask<string?> TryGetTextAsync(CancellationToken cancellationToken);

    ValueTask SetTextAsync(string text, CancellationToken cancellationToken);

    ValueTask SetFilesAsync(IReadOnlyList<string> files, CancellationToken cancellationToken);

    ValueTask PasteTextAsync(string text, CancellationToken cancellationToken);

    ValueTask PasteCurrentAsync(CancellationToken cancellationToken);

    ValueTask SetImageAsync(string imagePath, CancellationToken cancellationToken);
}

public interface IWeedShell
{
    ValueTask OpenAsync(string pathOrUri, CancellationToken cancellationToken);

    ValueTask OpenAsAdministratorAsync(string path, string? arguments, string? workingDirectory, CancellationToken cancellationToken);

    ValueTask OpenContainingFolderAsync(string path, CancellationToken cancellationToken);

    ValueTask CopyPathAsync(string path, CancellationToken cancellationToken);
}

public interface IWeedWindowService
{
    ValueTask ShowLauncherAsync(string? initialQuery, CancellationToken cancellationToken);

    ValueTask ShowClipboardPanelAsync(CancellationToken cancellationToken);

    ValueTask ShowMessageAsync(string title, string message, CancellationToken cancellationToken);
}

public interface IWeedScreenCapture
{
    ValueTask<ScreenCaptureResult?> CaptureRegionInteractiveAsync(CancellationToken cancellationToken);

    ValueTask<ScreenCaptureResult?> CapturePrimaryScreenAsync(CancellationToken cancellationToken);

    ValueTask<ScreenCaptureResult?> CaptureScrollingInteractiveAsync(CancellationToken cancellationToken);
}
