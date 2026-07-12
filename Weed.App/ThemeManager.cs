using Microsoft.Win32;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using WpfApplication = System.Windows.Application;

namespace Weed.App;

internal static class ThemeManager
{
    private static readonly IReadOnlyDictionary<string, string> Dark = new Dictionary<string, string>
    {
        ["BackgroundBrush"] = "#101318", ["SurfaceBrush"] = "#191D24", ["SurfaceElevatedBrush"] = "#242A34",
        ["SidebarBrush"] = "#151920", ["TextPrimaryBrush"] = "#F5F7FA", ["TextSecondaryBrush"] = "#A8B1BF",
        ["AccentBrush"] = "#5ED394", ["BorderBrush"] = "#343C49", ["SelectionBrush"] = "#286347",
        ["ControlBrush"] = "#202630", ["ControlBorderBrush"] = "#3A4554", ["HoverBrush"] = "#2B333F",
        ["SelectedNavBrush"] = "#203C31", ["SelectedNavBorderBrush"] = "#4A9A73", ["ScrollThumbBrush"] = "#536174",
        ["DangerBrush"] = "#FF9696", ["DangerSurfaceBrush"] = "#3B2428"
    };

    private static readonly IReadOnlyDictionary<string, string> Light = new Dictionary<string, string>
    {
        ["BackgroundBrush"] = "#F5F7F9", ["SurfaceBrush"] = "#FFFFFF", ["SurfaceElevatedBrush"] = "#EEF2F4",
        ["SidebarBrush"] = "#E8EDEF", ["TextPrimaryBrush"] = "#18211D", ["TextSecondaryBrush"] = "#58645F",
        ["AccentBrush"] = "#087A48", ["BorderBrush"] = "#C9D2CE", ["SelectionBrush"] = "#BDE8D0",
        ["ControlBrush"] = "#FFFFFF", ["ControlBorderBrush"] = "#AEBBB5", ["HoverBrush"] = "#DDE6E2",
        ["SelectedNavBrush"] = "#CDEDDD", ["SelectedNavBorderBrush"] = "#25885B", ["ScrollThumbBrush"] = "#87958F",
        ["DangerBrush"] = "#B4232F", ["DangerSurfaceBrush"] = "#FCE8EA"
    };

    public static string CurrentTheme { get; private set; } = "system";

    public static event Action? Changed;

    private static string? _resolvedTheme;

    public static void Apply(string? theme)
    {
        var requestedTheme = theme?.ToLowerInvariant() is "dark" or "light" ? theme.ToLowerInvariant() : "system";
        var preferenceChanged = !string.Equals(CurrentTheme, requestedTheme, StringComparison.Ordinal);
        CurrentTheme = requestedTheme;
        var resolvedTheme = CurrentTheme == "light" || CurrentTheme == "system" && IsWindowsLightTheme() ? "light" : "dark";
        if (string.Equals(_resolvedTheme, resolvedTheme, StringComparison.Ordinal))
        {
            if (preferenceChanged) Changed?.Invoke();
            return;
        }

        _resolvedTheme = resolvedTheme;
        var useLightTheme = resolvedTheme == "light";
        ApplicationThemeManager.Apply(useLightTheme ? ApplicationTheme.Light : ApplicationTheme.Dark);
        var palette = useLightTheme ? Light : Dark;
        foreach (var (key, colorText) in palette)
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorText);
            // Brushes loaded from compiled XAML may be frozen even when freezing is disabled in markup.
            // Replace the resource instead of mutating it; DynamicResource consumers update automatically.
            WpfApplication.Current.Resources[key] = new SolidColorBrush(color);
        }

        Changed?.Invoke();
    }

    public static System.Windows.Media.Brush Resource(string key) =>
        (System.Windows.Media.Brush)WpfApplication.Current.Resources[key];

    public static void RefreshSystemTheme()
    {
        if (CurrentTheme == "system") Apply(CurrentTheme);
    }

    private static bool IsWindowsLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
        }
        catch { return false; }
    }
}
