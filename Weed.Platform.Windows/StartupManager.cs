using Microsoft.Win32;
using System.IO;

namespace Weed.Platform.Windows;

public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "Weed";

    public static string BuildCommand(string executablePath) => $"\"{executablePath}\" --startup";

    public static string? CurrentExecutablePath()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) &&
            Path.GetFileName(processPath).Equals("Weed.exe", StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        var assemblyPath = System.Reflection.Assembly.GetEntryAssembly()?.Location;
        if (string.IsNullOrWhiteSpace(assemblyPath)) return null;
        var appHostPath = Path.ChangeExtension(assemblyPath, ".exe");
        return File.Exists(appHostPath) ? appHostPath : null;
    }

    public static bool IsEnabled(string executablePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return string.Equals(key?.GetValue(AppName)?.ToString(), BuildCommand(executablePath), StringComparison.OrdinalIgnoreCase);
    }

    public static void SetEnabled(bool enabled, string executablePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true) ??
                        Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (enabled)
        {
            key.SetValue(AppName, BuildCommand(executablePath));
        }
        else
        {
            key.DeleteValue(AppName, throwOnMissingValue: false);
        }
    }
}
