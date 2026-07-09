using System.IO.Compression;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
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
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<PluginImportResult> ImportAsync(
        string sourcePath,
        string pluginsRoot,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return new PluginImportResult(false, "Choose a plugin ZIP, DLL, or folder first.");
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
            return await NormalizePackageRootAsync(source, cleanup, cancellationToken);
        }

        if (!File.Exists(source))
        {
            throw new FileNotFoundException("Plugin package was not found.", source);
        }

        var extension = Path.GetExtension(source);
        if (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return await PrepareDllPackageRootAsync(source, cleanup, cancellationToken);
        }

        if (!extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only plugin folders, source folders, .zip packages, and .dll files can be imported.");
        }

        var temp = Path.Combine(Path.GetTempPath(), $"weed-plugin-import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        cleanup.Add(temp);
        await Task.Run(() => ZipFile.ExtractToDirectory(source, temp), cancellationToken);
        return await NormalizePackageRootAsync(temp, cleanup, cancellationToken);
    }

    private static async Task<string?> NormalizePackageRootAsync(
        string sourceRoot,
        List<string> cleanup,
        CancellationToken cancellationToken)
    {
        var packageRoot = FindManifestRoot(sourceRoot);
        if (packageRoot is null)
        {
            return null;
        }

        var manifestPath = Path.Combine(packageRoot, "manifest.json");
        var manifest = await ReadManifestAsync(manifestPath, cancellationToken);
        if (!IsAssemblyMissing(packageRoot, manifest))
        {
            return packageRoot;
        }

        var projectPath = FindProjectFile(packageRoot, manifest);
        return projectPath is null
            ? packageRoot
            : await PublishSourcePackageAsync(packageRoot, projectPath, cleanup, cancellationToken);
    }

    private static async Task<string?> PrepareDllPackageRootAsync(
        string dllPath,
        List<string> cleanup,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(dllPath) ?? ".";
        var manifestRoot = FindManifestRoot(directory);
        if (manifestRoot is not null)
        {
            var manifest = await ReadManifestAsync(Path.Combine(manifestRoot, "manifest.json"), cancellationToken);
            var assemblyPath = ResolvePackagePath(manifestRoot, manifest.Assembly ?? string.Empty);
            if (assemblyPath is not null && PathsEqual(assemblyPath, dllPath))
            {
                return await NormalizePackageRootAsync(manifestRoot, cleanup, cancellationToken);
            }
        }

        return await CreatePackageFromDllAsync(dllPath, cleanup, cancellationToken);
    }

    private static string? FindManifestRoot(string root)
    {
        if (File.Exists(Path.Combine(root, "manifest.json")))
        {
            return root;
        }

        var manifestRoots = Directory.EnumerateFiles(root, "manifest.json", SearchOption.AllDirectories)
            .Select(Path.GetDirectoryName)
            .Where(path => !string.IsNullOrWhiteSpace(path) && !IsIgnoredDiscoveryPath(root, path!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();
        return manifestRoots.Length == 1 ? manifestRoots[0] : null;
    }

    private static async Task<WeedPluginManifest> ReadManifestAsync(string manifestPath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(manifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<WeedPluginManifest>(
            stream,
            JsonOptions,
            cancellationToken);
        return manifest ?? throw new InvalidOperationException("manifest.json is empty or invalid.");
    }

    private static bool IsAssemblyMissing(string packageRoot, WeedPluginManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Assembly))
        {
            return false;
        }

        var assemblyPath = ResolvePackagePath(packageRoot, manifest.Assembly);
        return assemblyPath is not null && !File.Exists(assemblyPath);
    }

    private static string? FindProjectFile(string packageRoot, WeedPluginManifest manifest)
    {
        var projects = Directory.EnumerateFiles(packageRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsIgnoredDiscoveryPath(packageRoot, path))
            .ToArray();
        if (projects.Length == 0)
        {
            return null;
        }

        var assemblyName = string.IsNullOrWhiteSpace(manifest.Assembly)
            ? null
            : Path.GetFileNameWithoutExtension(manifest.Assembly);
        if (!string.IsNullOrWhiteSpace(assemblyName))
        {
            var matchingProject = projects.FirstOrDefault(path =>
                Path.GetFileNameWithoutExtension(path).Equals(assemblyName, StringComparison.OrdinalIgnoreCase));
            if (matchingProject is not null)
            {
                return matchingProject;
            }
        }

        return projects.Length == 1 ? projects[0] : null;
    }

    private static async Task<string> PublishSourcePackageAsync(
        string packageRoot,
        string projectPath,
        List<string> cleanup,
        CancellationToken cancellationToken)
    {
        var publishRoot = Path.Combine(Path.GetTempPath(), $"weed-plugin-publish-{Guid.NewGuid():N}");
        Directory.CreateDirectory(publishRoot);
        cleanup.Add(publishRoot);

        var result = await RunDotnetPublishAsync(projectPath, publishRoot, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Source plugin build failed with exit code {result.ExitCode}.{Environment.NewLine}{Tail(result.Output)}");
        }

        var publishedManifestPath = Path.Combine(publishRoot, "manifest.json");
        if (!File.Exists(publishedManifestPath))
        {
            File.Copy(Path.Combine(packageRoot, "manifest.json"), publishedManifestPath, overwrite: true);
        }

        var publishedManifest = await ReadManifestAsync(publishedManifestPath, cancellationToken);
        if (IsAssemblyMissing(publishRoot, publishedManifest))
        {
            throw new InvalidOperationException(
                $"Source plugin build completed, but the published assembly was not found: {publishedManifest.Assembly}");
        }

        return publishRoot;
    }

    private sealed record ProcessRunResult(int ExitCode, string Output);

    private static async Task<ProcessRunResult> RunDotnetPublishAsync(
        string projectPath,
        string publishRoot,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(projectPath) ?? "."
        };
        start.ArgumentList.Add("publish");
        start.ArgumentList.Add(projectPath);
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("Release");
        start.ArgumentList.Add("-r");
        start.ArgumentList.Add(CurrentRuntimeIdentifier());
        start.ArgumentList.Add("--self-contained");
        start.ArgumentList.Add("false");
        start.ArgumentList.Add("-o");
        start.ArgumentList.Add(publishRoot);

        using var process = new Process { StartInfo = start };
        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start dotnet publish.");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch
        {
            TryKill(process);
            throw;
        }

        var output = $"{await outputTask}{await errorTask}";
        return new ProcessRunResult(process.ExitCode, output);
    }

    private static string CurrentRuntimeIdentifier() =>
        RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "win-arm64",
            Architecture.X86 => "win-x86",
            _ => "win-x64"
        };

    private static async Task<string> CreatePackageFromDllAsync(
        string dllPath,
        List<string> cleanup,
        CancellationToken cancellationToken)
    {
        var manifest = TryCreateManifestFromDll(dllPath);
        if (manifest is null)
        {
            throw new InvalidOperationException(
                "DLL import requires either a manifest.json next to the DLL or a public IWeedPlugin implementation in the assembly.");
        }

        var sourceDirectory = Path.GetDirectoryName(dllPath) ?? ".";
        var packageRoot = Path.Combine(Path.GetTempPath(), $"weed-plugin-dll-{Guid.NewGuid():N}");
        cleanup.Add(packageRoot);
        CopyDirectory(sourceDirectory, packageRoot, cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(packageRoot, "manifest.json"),
            JsonSerializer.Serialize(manifest, JsonOptions),
            cancellationToken);

        return packageRoot;
    }

    private static WeedPluginManifest? TryCreateManifestFromDll(string dllPath)
    {
        PluginLoadContext? context = null;
        try
        {
            context = new PluginLoadContext(dllPath);
            var assembly = context.LoadFromAssemblyPath(dllPath);
            Type? pluginType = null;
            foreach (var type in GetLoadableTypes(assembly))
            {
                if (type.IsAbstract || !typeof(IWeedPlugin).IsAssignableFrom(type))
                {
                    continue;
                }

                pluginType ??= type;
                var declaredManifest = TryReadStaticManifest(type);
                if (declaredManifest is not null)
                {
                    return NormalizeDllManifest(declaredManifest, dllPath, type);
                }
            }

            if (pluginType is null)
            {
                return null;
            }

            var name = assembly.GetName().Name ?? Path.GetFileNameWithoutExtension(dllPath);
            return new WeedPluginManifest
            {
                Id = SanitizeGeneratedPluginId(name),
                Name = name,
                Version = assembly.GetName().Version?.ToString(3) ?? "0.1.0",
                Assembly = Path.GetFileName(dllPath),
                EntryType = pluginType.FullName,
                Runtime = new PluginRuntimeManifest
                {
                    Resident = typeof(IResidentPlugin).IsAssignableFrom(pluginType)
                },
                Activations = [],
                Permissions = []
            };
        }
        catch
        {
            return null;
        }
        finally
        {
            context?.Unload();
        }
    }

    private static WeedPluginManifest NormalizeDllManifest(
        WeedPluginManifest manifest,
        string dllPath,
        Type pluginType)
    {
        var entryType = string.IsNullOrWhiteSpace(manifest.EntryType)
            ? pluginType.FullName
            : manifest.EntryType;
        return manifest with
        {
            Assembly = Path.GetFileName(dllPath),
            EntryType = entryType
        };
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }

    private static WeedPluginManifest? TryReadStaticManifest(Type type)
    {
        try
        {
            var property = type.GetProperty("Manifest", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (property is not null && typeof(WeedPluginManifest).IsAssignableFrom(property.PropertyType))
            {
                return property.GetValue(null) as WeedPluginManifest;
            }

            var field = type.GetField("Manifest", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (field is not null && typeof(WeedPluginManifest).IsAssignableFrom(field.FieldType))
            {
                return field.GetValue(null) as WeedPluginManifest;
            }
        }
        catch
        {
        }

        return null;
    }

    private static string SanitizeGeneratedPluginId(string value)
    {
        var id = Regex.Replace(value.Trim(), "[^a-zA-Z0-9_.-]+", ".");
        id = id.Trim('.');
        return string.IsNullOrWhiteSpace(id) ? "external.plugin" : id;
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

    private static bool IsIgnoredDiscoveryPath(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        var parts = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        return parts.Any(part =>
            part.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
            part.StartsWith(".", StringComparison.Ordinal));
    }

    private static string Tail(string text)
    {
        const int maxLength = 4000;
        if (string.IsNullOrWhiteSpace(text) || text.Length <= maxLength)
        {
            return text.Trim();
        }

        return text[^maxLength..].Trim();
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
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
