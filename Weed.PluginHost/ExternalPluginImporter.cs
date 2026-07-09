using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Weed.Abstractions;

namespace Weed.PluginHost;

public sealed record PluginImportResult(
    bool Succeeded,
    string Message,
    string? PluginId = null,
    string? TargetDirectory = null);

public sealed class ExternalPluginImporter
{
    private static readonly Regex PluginIdPattern = new("^[a-zA-Z0-9_.-]+$", RegexOptions.Compiled);

    public async Task<PluginImportResult> ImportAsync(
        string sourcePath,
        string pluginsRoot,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return new PluginImportResult(false, "Choose a plugin ZIP or folder first.");
        }

        var root = Path.GetFullPath(pluginsRoot);
        Directory.CreateDirectory(root);

        var cleanup = new List<string>();
        string? tempTarget = null;
        try
        {
            var packageRoot = await PreparePackageRootAsync(sourcePath, cleanup, cancellationToken);
            if (packageRoot is null)
            {
                return new PluginImportResult(false, "The package does not contain a manifest.json file.");
            }

            var manifestPath = Path.Combine(packageRoot, "manifest.json");
            var manifest = await ReadManifestAsync(manifestPath, cancellationToken);
            var validation = ValidatePackage(packageRoot, manifest);
            if (validation is not null)
            {
                return new PluginImportResult(false, validation);
            }

            var targetDirectory = Path.Combine(root, manifest.Id);
            var fullTargetDirectory = Path.GetFullPath(targetDirectory);
            if (!IsUnderDirectory(fullTargetDirectory, root))
            {
                return new PluginImportResult(false, "The plugin id resolves outside the plugin directory.");
            }

            var fullPackageRoot = Path.GetFullPath(packageRoot);
            if (PathsEqual(fullPackageRoot, fullTargetDirectory))
            {
                return new PluginImportResult(true, "Plugin is already in the external plugin directory.", manifest.Id, fullTargetDirectory);
            }

            if (Directory.Exists(fullTargetDirectory) && !overwrite)
            {
                return new PluginImportResult(
                    false,
                    $"Plugin {manifest.Id} is already installed. Import again and allow replacement to update it.",
                    manifest.Id,
                    fullTargetDirectory);
            }

            tempTarget = Path.Combine(root, $".{manifest.Id}.import-{Guid.NewGuid():N}");
            CopyDirectory(fullPackageRoot, tempTarget, cancellationToken);
            EnsurePackageStillValid(tempTarget, manifest);
            ReplaceDirectory(tempTarget, fullTargetDirectory, overwrite);
            tempTarget = null;

            return new PluginImportResult(
                true,
                $"Imported {manifest.Name}. Restart Weed to load or update it.",
                manifest.Id,
                fullTargetDirectory);
        }
        catch (Exception ex)
        {
            return new PluginImportResult(false, $"Plugin import failed: {ex.Message}");
        }
        finally
        {
            if (tempTarget is not null && Directory.Exists(tempTarget))
            {
                TryDeleteDirectory(tempTarget);
            }

            foreach (var path in cleanup)
            {
                TryDeleteDirectory(path);
            }
        }
    }

    private static async Task<string?> PreparePackageRootAsync(
        string sourcePath,
        List<string> cleanup,
        CancellationToken cancellationToken)
    {
        var source = Path.GetFullPath(Environment.ExpandEnvironmentVariables(sourcePath));
        if (Directory.Exists(source))
        {
            return FindManifestRoot(source);
        }

        if (!File.Exists(source))
        {
            throw new FileNotFoundException("Plugin package was not found.", source);
        }

        if (!source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only plugin folders and .zip packages can be imported.");
        }

        var temp = Path.Combine(Path.GetTempPath(), $"weed-plugin-import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        cleanup.Add(temp);
        await Task.Run(() => ZipFile.ExtractToDirectory(source, temp), cancellationToken);
        return FindManifestRoot(temp);
    }

    private static string? FindManifestRoot(string root)
    {
        if (File.Exists(Path.Combine(root, "manifest.json")))
        {
            return root;
        }

        var childManifestRoots = Directory.EnumerateDirectories(root)
            .Where(path => File.Exists(Path.Combine(path, "manifest.json")))
            .Take(2)
            .ToArray();
        return childManifestRoots.Length == 1 ? childManifestRoots[0] : null;
    }

    private static async Task<WeedPluginManifest> ReadManifestAsync(string manifestPath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(manifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<WeedPluginManifest>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken);
        return manifest ?? throw new InvalidOperationException("manifest.json is empty or invalid.");
    }

    private static string? ValidatePackage(string packageRoot, WeedPluginManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Id) || !PluginIdPattern.IsMatch(manifest.Id))
        {
            return "Plugin manifest has an invalid id.";
        }

        if (string.IsNullOrWhiteSpace(manifest.Assembly) || string.IsNullOrWhiteSpace(manifest.EntryType))
        {
            return "Plugin manifest must include assembly and entryType.";
        }

        var assemblyPath = ResolvePackagePath(packageRoot, manifest.Assembly);
        if (assemblyPath is null || !File.Exists(assemblyPath))
        {
            return $"Plugin assembly was not found: {manifest.Assembly}";
        }

        if (!string.IsNullOrWhiteSpace(manifest.Icon) && ResolvePackagePath(packageRoot, manifest.Icon) is null)
        {
            return "Plugin icon path must stay inside the package.";
        }

        return null;
    }

    private static void EnsurePackageStillValid(string packageRoot, WeedPluginManifest manifest)
    {
        var validation = ValidatePackage(packageRoot, manifest);
        if (validation is not null)
        {
            throw new InvalidOperationException(validation);
        }
    }

    private static string? ResolvePackagePath(string packageRoot, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            return null;
        }

        var root = Path.GetFullPath(packageRoot);
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        return IsUnderDirectory(fullPath, root) ? fullPath : null;
    }

    private static void CopyDirectory(string source, string target, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(target, relative));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(source, file);
            var targetFile = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(file, targetFile, overwrite: true);
        }
    }

    private static void ReplaceDirectory(string source, string target, bool overwrite)
    {
        if (!Directory.Exists(target))
        {
            Directory.Move(source, target);
            return;
        }

        if (!overwrite)
        {
            throw new InvalidOperationException("Target plugin directory already exists.");
        }

        var backup = $"{target}.backup-{Guid.NewGuid():N}";
        Directory.Move(target, backup);
        try
        {
            Directory.Move(source, target);
            TryDeleteDirectory(backup);
        }
        catch
        {
            if (!Directory.Exists(target) && Directory.Exists(backup))
            {
                Directory.Move(backup, target);
            }

            throw;
        }
    }

    private static bool IsUnderDirectory(string path, string directory)
    {
        var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return normalizedPath.Equals(normalizedDirectory, StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.StartsWith(normalizedDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string left, string right) =>
        Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Equals(
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }
}
