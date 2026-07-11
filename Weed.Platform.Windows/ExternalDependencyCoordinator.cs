using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Weed.Abstractions;

namespace Weed.Platform.Windows;

public sealed record ExternalDependencyStatus(string PluginId, string DependencyId, string Name, bool Available, string Message);

public sealed class ExternalDependencyCoordinator
{
    private readonly IWeedLogger _logger;
    private readonly TimeSpan _timeout;
    private readonly Func<PluginExternalDependencyManifest, bool> _isReady;
    private readonly Func<IEnumerable<string>, string?> _findExecutable;
    private readonly Action<string> _start;

    public ExternalDependencyCoordinator(IWeedLogger logger, TimeSpan? timeout = null,
        Func<PluginExternalDependencyManifest, bool>? isReady = null,
        Func<IEnumerable<string>, string?>? findExecutable = null,
        Action<string>? start = null)
    {
        _logger = logger;
        _timeout = timeout ?? TimeSpan.FromSeconds(5);
        _isReady = isReady ?? IsReady;
        _findExecutable = findExecutable ?? FindExecutable;
        _start = start ?? (path => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }));
    }

    public async Task<IReadOnlyList<ExternalDependencyStatus>> EnsureAsync(
        IEnumerable<(string PluginId, PluginExternalDependencyManifest Dependency)> dependencies,
        CancellationToken cancellationToken)
    {
        var results = new List<ExternalDependencyStatus>();
        foreach (var item in dependencies)
        {
            var dependency = item.Dependency;
            if (!dependency.RequiredRunning || _isReady(dependency))
            {
                results.Add(new(item.PluginId, dependency.Id, dependency.Name, true, "Available"));
                continue;
            }

            var executable = _findExecutable(dependency.Executables);
            if (dependency.AutoStart && executable is not null)
            {
                try
                {
                    _start(executable);
                    var deadline = DateTime.UtcNow + _timeout;
                    while (DateTime.UtcNow < deadline && !_isReady(dependency))
                    {
                        await Task.Delay(200, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to start dependency {dependency.Name} for {item.PluginId}.", ex);
                }
            }

            var available = _isReady(dependency);
            var message = available ? "Available" : executable is null ? "Not installed or executable not found" : "Failed to become ready";
            if (!available)
            {
                _logger.Warn($"Dependency {dependency.Name} for {item.PluginId} is unavailable: {message}.");
            }
            results.Add(new(item.PluginId, dependency.Id, dependency.Name, available, message));
        }
        return results;
    }

    public static bool IsReady(PluginExternalDependencyManifest dependency) =>
        dependency.ReadinessProbe.Equals("everythingIpc", StringComparison.OrdinalIgnoreCase)
            ? FindWindow("EVERYTHING_TASKBAR_NOTIFICATION", null) != IntPtr.Zero
            : dependency.Executables.Any(name => Process.GetProcessesByName(Path.GetFileNameWithoutExtension(name)).Length > 0);

    public static string? FindExecutable(IEnumerable<string> candidates)
    {
        foreach (var candidate in candidates.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            if (Path.IsPathRooted(candidate) && File.Exists(candidate)) return candidate;
            using var appPath = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{candidate}")
                                ?? Registry.CurrentUser.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{candidate}");
            var registered = appPath?.GetValue(null)?.ToString();
            if (!string.IsNullOrWhiteSpace(registered) && File.Exists(registered)) return registered;
            foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
            {
                var path = Path.Combine(root, "Everything", candidate);
                if (File.Exists(path)) return path;
            }
            foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var path = Path.Combine(directory.Trim(), candidate);
                if (File.Exists(path)) return path;
            }
        }
        return null;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);
}
