using System.Text.Json;
using Weed.Abstractions;

namespace Weed.PluginHost;

public sealed record PluginUninstallResult(
    bool Succeeded,
    string Message,
    string? PluginId = null,
    string? RemovedDirectory = null,
    bool RestartRequired = false);

public sealed class ExternalPluginUninstaller
{
    public const string PendingRemovalMarker = ".uninstall";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Task<PluginUninstallResult> UninstallAsync(
        string pluginId,
        string pluginDirectory,
        string pluginsRoot,
        CancellationToken cancellationToken) => Task.Run(
        () => Uninstall(pluginId, pluginDirectory, pluginsRoot, cancellationToken),
        cancellationToken);

    public static bool IsPendingRemoval(string pluginDirectory) =>
        File.Exists(Path.Combine(pluginDirectory, PendingRemovalMarker));

    internal static void CleanupPendingRemovals(string pluginsRoot, IWeedLogger logger)
    {
        if (!Directory.Exists(pluginsRoot))
        {
            return;
        }

        string[] markers;
        try
        {
            markers = Directory.EnumerateFiles(
                    pluginsRoot,
                    PendingRemovalMarker,
                    SearchOption.AllDirectories)
                .ToArray();
        }
        catch (Exception ex)
        {
            logger.Warn($"Could not scan pending external plugin removals: {ex.Message}");
            return;
        }

        foreach (var marker in markers)
        {
            var directory = Path.GetDirectoryName(marker);
            if (directory is null || !IsUnderDirectory(directory, pluginsRoot) || PathsEqual(directory, pluginsRoot))
            {
                continue;
            }

            try
            {
                Directory.Delete(directory, recursive: true);
                logger.Info($"Completed pending external plugin removal at {directory}.");
            }
            catch (Exception ex)
            {
                logger.Warn($"External plugin removal is still pending at {directory}: {ex.Message}");
            }
        }
    }

    private static PluginUninstallResult Uninstall(
        string pluginId,
        string pluginDirectory,
        string pluginsRoot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(pluginId) || string.IsNullOrWhiteSpace(pluginDirectory))
        {
            return new PluginUninstallResult(false, "Choose an installed external plugin first.");
        }

        var root = Path.GetFullPath(pluginsRoot);
        var target = Path.GetFullPath(pluginDirectory);
        if (PathsEqual(target, root) || !IsUnderDirectory(target, root))
        {
            return new PluginUninstallResult(false, "The selected plugin directory is outside the external plugin folder.");
        }

        if (!Directory.Exists(target))
        {
            return new PluginUninstallResult(false, "The selected plugin directory no longer exists.", pluginId, target);
        }

        var manifestPath = Path.Combine(target, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return new PluginUninstallResult(false, "The selected directory does not contain a plugin manifest.", pluginId, target);
        }

        try
        {
            var manifest = JsonSerializer.Deserialize<WeedPluginManifest>(File.ReadAllText(manifestPath), JsonOptions);
            if (manifest is null || !manifest.Id.Equals(pluginId, StringComparison.OrdinalIgnoreCase))
            {
                return new PluginUninstallResult(false, "The selected plugin manifest does not match the installed plugin.", pluginId, target);
            }
        }
        catch (Exception ex)
        {
            return new PluginUninstallResult(false, $"The selected plugin manifest could not be read: {ex.Message}", pluginId, target);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var quarantine = Path.Combine(root, $".{SafeName(pluginId)}.uninstall-{Guid.NewGuid():N}");
        try
        {
            Directory.Move(target, quarantine);
        }
        catch (Exception moveException)
        {
            try
            {
                File.WriteAllText(
                    Path.Combine(target, PendingRemovalMarker),
                    DateTimeOffset.UtcNow.ToString("O"));
                return new PluginUninstallResult(
                    true,
                    $"Marked {pluginId} for removal. Restart Weed to finish uninstalling it.",
                    pluginId,
                    target,
                    RestartRequired: true);
            }
            catch (Exception markerException)
            {
                return new PluginUninstallResult(
                    false,
                    $"Plugin uninstall failed: {moveException.Message} {markerException.Message}",
                    pluginId,
                    target);
            }
        }

        try
        {
            Directory.Delete(quarantine, recursive: true);
            return new PluginUninstallResult(
                true,
                $"Uninstalled {pluginId}. Restart Weed to unload it from the current session.",
                pluginId,
                target,
                RestartRequired: true);
        }
        catch
        {
            try
            {
                File.WriteAllText(
                    Path.Combine(quarantine, PendingRemovalMarker),
                    DateTimeOffset.UtcNow.ToString("O"));
            }
            catch
            {
            }

            return new PluginUninstallResult(
                true,
                $"Uninstalled {pluginId}. Remaining files will be cleaned up after Weed restarts.",
                pluginId,
                target,
                RestartRequired: true);
        }
    }

    private static string SafeName(string value) =>
        string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_' ? ch : '_'));

    private static bool IsUnderDirectory(string path, string directory)
    {
        var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return normalizedPath.StartsWith(normalizedDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string left, string right) =>
        Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Equals(
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
}
