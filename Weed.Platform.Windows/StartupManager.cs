using Microsoft.Win32;

namespace Weed.Platform.Windows;

public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "Weed";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return !string.IsNullOrWhiteSpace(key?.GetValue(AppName)?.ToString());
    }

    public static void SetEnabled(bool enabled, string executablePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true) ??
                        Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (enabled)
        {
            key.SetValue(AppName, $"\"{executablePath}\"");
        }
        else
        {
            key.DeleteValue(AppName, throwOnMissingValue: false);
        }
    }
}
