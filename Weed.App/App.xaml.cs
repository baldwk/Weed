using System.IO;
using System.Windows;
using Weed.Abstractions;
using Weed.Core;
using Weed.Platform.Windows;
using Weed.PluginHost;
using Weed.Plugins.AppLauncher;
using Weed.Plugins.Calculator;
using Weed.Plugins.Clipboard;
using Weed.Plugins.RunCommand;
using Weed.Plugins.Screenshot;
using Forms = System.Windows.Forms;

namespace Weed.App;

public partial class App : System.Windows.Application
{
    private SettingsRepository? _settings;
    private FileWeedLogger? _logger;
    private UsageHistoryStore? _usage;
    private QueryRouter? _router;
    private PluginRuntime? _pluginRuntime;
    private HotkeyManager? _hotkeys;
    private Forms.NotifyIcon? _trayIcon;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var paths = new AppPaths();
        _settings = new SettingsRepository(paths);
        _settings.Load();
        _logger = new FileWeedLogger(paths.Logs);
        HookCrashLogging(_logger);
        _logger.Info("Weed starting.");

        var clipboard = new WindowsClipboardService();
        var shell = new WindowsShellService(clipboard);
        var screenCapture = new WindowsScreenCaptureService(paths.Screenshots, _settings);
        var host = new WeedHost(_logger, _settings, _settings, clipboard, shell, screenCapture);

        _usage = new UsageHistoryStore(paths.DatabaseFile);
        _usage.Load();
        _router = new QueryRouter(_settings, _usage, _logger);
        _pluginRuntime = new PluginRuntime(host, _logger);

        using var startupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await _pluginRuntime.AddBuiltInAsync(AppLauncherPlugin.Manifest, new AppLauncherPlugin(), startupCts.Token);
        await _pluginRuntime.AddBuiltInAsync(CalculatorPlugin.Manifest, new CalculatorPlugin(), startupCts.Token);
        await _pluginRuntime.AddBuiltInAsync(ClipboardPlugin.Manifest, new ClipboardPlugin(), startupCts.Token);
        await _pluginRuntime.AddBuiltInAsync(RunCommandPlugin.Manifest, new RunCommandPlugin(), startupCts.Token);
        await _pluginRuntime.AddBuiltInAsync(ScreenshotPlugin.Manifest, new ScreenshotPlugin(), startupCts.Token);
        await _pluginRuntime.ScanDirectoryAsync(paths.Plugins, startupCts.Token);

        _settings.EnsurePluginDefaults(_pluginRuntime.Plugins.Select(p => p.Manifest));
        _router.SetPlugins(_pluginRuntime.Plugins);

        MainWindow? window = null;
        window = new MainWindow(_router, _settings, _logger, () =>
        {
            if (window is not null)
            {
                RegisterHotkeys(window);
            }
        });
        host.LauncherWindow = window;
        MainWindow = window;

        _hotkeys = new HotkeyManager();
        _hotkeys.Attach(window);
        _hotkeys.HotkeyPressed += Hotkeys_HotkeyPressed;
        RegisterHotkeys(window);
        CreateTrayIcon(window);
        QueueStartupUpdateCheck();

        await _pluginRuntime.StartResidentsAsync(startupCts.Token);
        window.ShowLauncher(null);
        _logger.Info("Weed started.");
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            _logger?.Info("Weed shutting down.");
            _trayIcon?.Dispose();
            _hotkeys?.Dispose();
            if (_pluginRuntime is not null)
            {
                await _pluginRuntime.DisposeAsync();
            }
            _logger?.Info("Weed stopped.");
        }
        finally
        {
            base.OnExit(e);
        }
    }

    private void RegisterHotkeys(MainWindow window)
    {
        if (_settings is null || _pluginRuntime is null || _hotkeys is null)
        {
            return;
        }

        var mainHotkey = HotkeyText.Normalize(_settings.AppSettings.MainHotkey);
        _hotkeys.Clear();
        if (!_hotkeys.Register(window, mainHotkey, "weed:launcher"))
        {
            _logger?.Warn($"Failed to register main hotkey {mainHotkey}.");
        }

        foreach (var plugin in _pluginRuntime.Plugins)
        {
            foreach (var activation in plugin.Manifest.Activations.Where(a => a.Type.Equals("hotkey", StringComparison.OrdinalIgnoreCase)))
            {
                if (string.IsNullOrWhiteSpace(activation.Command))
                {
                    continue;
                }

                var key = $"{plugin.Manifest.Id}:{activation.Command}";
                if (!_settings.Hotkeys.TryGetValue(key, out var hotkey) ||
                    !hotkey.Enabled ||
                    string.IsNullOrWhiteSpace(hotkey.Keys))
                {
                    continue;
                }

                if (!_hotkeys.Register(window, hotkey.Keys, key))
                {
                    _logger?.Warn($"Failed to register hotkey {hotkey.Keys} for {key}.");
                }
            }
        }
    }

    private async void Hotkeys_HotkeyPressed(object? sender, string command)
    {
        if (_router is null || MainWindow is not MainWindow window)
        {
            return;
        }

        try
        {
            if (command == "weed:launcher")
            {
                window.ShowLauncher(null);
                return;
            }

            var separator = command.IndexOf(':');
            if (separator <= 0)
            {
                return;
            }

            var pluginId = command[..separator];
            var commandId = command[(separator + 1)..];
            IDisposable? closeOnLostFocusScope = null;
            try
            {
                if (pluginId.Equals(ScreenshotPlugin.PluginId, StringComparison.OrdinalIgnoreCase))
                {
                    closeOnLostFocusScope = window.SuspendCloseOnLostFocus();
                }

                var result = await _router.ExecutePluginCommandAsync(pluginId, commandId, null, CancellationToken.None);
                if (!result.Succeeded && !string.IsNullOrWhiteSpace(result.Message))
                {
                    System.Windows.MessageBox.Show(result.Message, "Weed", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            finally
            {
                closeOnLostFocusScope?.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger?.Error($"Hotkey command failed: {command}", ex);
        }
    }

    private void CreateTrayIcon(MainWindow window)
    {
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "Weed",
            Visible = _settings?.AppSettings.ShowTrayIcon ?? true,
            ContextMenuStrip = new Forms.ContextMenuStrip()
        };
        _trayIcon.ContextMenuStrip.Items.Add("Open", null, (_, _) => window.ShowLauncher(null));
        _trayIcon.ContextMenuStrip.Items.Add("Settings", null, (_, _) => window.ShowSettings());
        _trayIcon.ContextMenuStrip.Items.Add("Exit", null, (_, _) => Shutdown());
        _trayIcon.DoubleClick += (_, _) => window.ShowLauncher(null);
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "assets", "app", "weed.ico");
        try
        {
            return File.Exists(iconPath) ? new System.Drawing.Icon(iconPath) : System.Drawing.SystemIcons.Application;
        }
        catch
        {
            return System.Drawing.SystemIcons.Application;
        }
    }

    private void QueueStartupUpdateCheck()
    {
        if (_settings is null ||
            _logger is null ||
            !_settings.AppSettings.AutoCheckUpdates ||
            string.IsNullOrWhiteSpace(_settings.AppSettings.UpdateManifestUrl))
        {
            return;
        }

        _ = CheckUpdatesOnStartupAsync(_settings, _logger);
    }

    private async Task CheckUpdatesOnStartupAsync(SettingsRepository settings, IWeedLogger logger)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            var result = await new UpdateService().CheckAsync(settings.AppSettings.UpdateManifestUrl, CancellationToken.None);
            logger.Info($"Update check: {result.Message}");
            if (result.IsUpdateAvailable && result.Manifest is not null)
            {
                await Dispatcher.InvokeAsync(() =>
                    System.Windows.MessageBox.Show(
                        $"Weed {result.Manifest.Version} is available.\n\nOpen Settings > Updates to download it.",
                        "Weed Update",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information));
            }
        }
        catch (Exception ex)
        {
            logger.Error("Startup update check failed.", ex);
        }
    }

    private void HookCrashLogging(IWeedLogger logger)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            logger.Error("Unhandled UI exception.", args.Exception);
            args.Handled = true;
            System.Windows.MessageBox.Show(args.Exception.Message, "Weed Error", MessageBoxButton.OK, MessageBoxImage.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            logger.Error("Unhandled process exception.", args.ExceptionObject as Exception);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            logger.Error("Unobserved task exception.", args.Exception);
            args.SetObserved();
        };
    }
}
