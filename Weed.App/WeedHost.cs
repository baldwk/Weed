using System.Windows;
using Weed.Abstractions;

namespace Weed.App;

public sealed class WeedHost : IWeedHost, IWeedWindowService
{
    public WeedHost(
        IWeedLogger logger,
        IWeedSettings settings,
        IWeedStorage storage,
        IWeedClipboard clipboard,
        IWeedShell shell,
        IWeedScreenCapture screenCapture)
    {
        Logger = logger;
        Settings = settings;
        Storage = storage;
        Clipboard = clipboard;
        Shell = shell;
        ScreenCapture = screenCapture;
    }

    public MainWindow? LauncherWindow { get; set; }

    public IWeedLogger Logger { get; }

    public IWeedSettings Settings { get; }

    public IWeedStorage Storage { get; }

    public IWeedClipboard Clipboard { get; }

    public IWeedShell Shell { get; }

    public IWeedWindowService Windows => this;

    public IWeedScreenCapture ScreenCapture { get; }

    public async ValueTask ShowLauncherAsync(string? initialQuery, CancellationToken cancellationToken)
    {
        if (LauncherWindow is null)
        {
            return;
        }

        await LauncherWindow.Dispatcher.InvokeAsync(() => LauncherWindow.ShowLauncher(initialQuery)).Task.WaitAsync(cancellationToken);
    }

    public async ValueTask ShowClipboardPanelAsync(CancellationToken cancellationToken)
    {
        if (LauncherWindow is null)
        {
            return;
        }

        await LauncherWindow.Dispatcher.InvokeAsync(LauncherWindow.ShowClipboardPanel).Task.WaitAsync(cancellationToken);
    }

    public async ValueTask ShowMessageAsync(string title, string message, CancellationToken cancellationToken)
    {
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            System.Windows.MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }).Task.WaitAsync(cancellationToken);
    }
}
