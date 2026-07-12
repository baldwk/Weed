using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Weed.Abstractions;
using Weed.Core;

namespace Weed.PluginHost;

public sealed record ExternalPluginRegistry
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; init; }

    public string SchemaVersion { get; init; } = "1";

    public List<ExternalPluginRegistryEntry> Plugins { get; init; } = [];
}

public sealed record ExternalPluginRegistryEntry
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string SdkVersion { get; init; } = string.Empty;

    public string MinWeedVersion { get; init; } = string.Empty;

    public string PackageUrl { get; init; } = string.Empty;

    public string Sha256 { get; init; } = string.Empty;

    public string RepositoryUrl { get; init; } = string.Empty;

    public string ReleaseNotesUrl { get; init; } = string.Empty;

    public bool Trusted { get; init; } = true;

    public List<string> Tags { get; init; } = [];
}

public enum ExternalPluginInstallState
{
    NotInstalled,
    Installed,
    UpdateAvailable,
    Incompatible,
    Invalid
}

public sealed record ExternalPluginInstallPlan(
    ExternalPluginRegistryEntry Entry,
    WeedPluginManifest? InstalledManifest,
    ExternalPluginInstallState State,
    string Message)
{
    public bool CanInstall =>
        State is ExternalPluginInstallState.NotInstalled or ExternalPluginInstallState.UpdateAvailable;
}

public sealed class ExternalPluginRegistryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly HttpClient _httpClient;

    public ExternalPluginRegistryService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
    }

    public async ValueTask<ExternalPluginRegistry> ReadRegistryAsync(
        string registryLocation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(registryLocation))
        {
            return new ExternalPluginRegistry();
        }

        var json = await ReadTextAsync(registryLocation, cancellationToken);
        return JsonSerializer.Deserialize<ExternalPluginRegistry>(json, JsonOptions) ??
               throw new InvalidDataException("Plugin registry is empty.");
    }

    public IReadOnlyList<ExternalPluginInstallPlan> BuildInstallPlans(
        ExternalPluginRegistry registry,
        string pluginsRoot)
    {
        var installed = ReadInstalledManifests(pluginsRoot);
        return registry.Plugins
            .OrderBy(plugin => plugin.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(plugin => plugin.Id, StringComparer.OrdinalIgnoreCase)
            .Select(plugin => BuildInstallPlan(plugin, installed))
            .ToArray();
    }

    public async ValueTask<PluginImportResult> DownloadAndImportAsync(
        ExternalPluginRegistryEntry entry,
        string registryLocation,
        string pluginsRoot,
        string downloadDirectory,
        CancellationToken cancellationToken)
    {
        var validation = ValidateRegistryEntry(entry);
        if (validation is not null)
        {
            return new PluginImportResult(false, validation, entry.Id);
        }

        Directory.CreateDirectory(downloadDirectory);
        var packageLocation = ResolvePackageLocation(entry.PackageUrl, registryLocation);
        var packageName = PackageFileName(packageLocation, entry);
        var downloadPath = Path.Combine(downloadDirectory, $"{Path.GetFileNameWithoutExtension(packageName)}-{Guid.NewGuid():N}.zip");

        try
        {
            await DownloadPackageAsync(packageLocation, downloadPath, cancellationToken);
            var sha256 = Sha256File(downloadPath);
            if (!string.Equals(sha256, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return new PluginImportResult(
                    false,
                    $"Downloaded plugin hash did not match the registry. Expected {entry.Sha256}, got {sha256}.",
                    entry.Id);
            }

            var packageValidation = ValidateDownloadedPackage(downloadPath, entry);
            if (packageValidation is not null)
            {
                return new PluginImportResult(false, packageValidation, entry.Id);
            }

            return await new ExternalPluginImporter().ImportAsync(
                downloadPath,
                pluginsRoot,
                overwrite: true,
                cancellationToken);
        }
        catch (Exception ex)
        {
            return new PluginImportResult(false, $"Plugin install failed: {ex.Message}", entry.Id);
        }
        finally
        {
            TryDeleteFile(downloadPath);
        }
    }

    public static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static string ResolvePackageLocation(string packageUrl, string? registryLocation)
    {
        if (Uri.TryCreate(packageUrl, UriKind.Absolute, out _))
        {
            return packageUrl;
        }

        if (string.IsNullOrWhiteSpace(registryLocation))
        {
            return packageUrl;
        }

        if (Uri.TryCreate(registryLocation, UriKind.Absolute, out var registryUri) &&
            (registryUri.Scheme == Uri.UriSchemeHttp || registryUri.Scheme == Uri.UriSchemeHttps))
        {
            return new Uri(registryUri, packageUrl).ToString();
        }

        var registryPath = registryLocation.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
            ? new Uri(registryLocation).LocalPath
            : registryLocation;
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(registryPath) ?? ".", packageUrl));
    }

    public static int CompareVersions(string left, string right)
    {
        var leftVersion = TryParseVersion(left);
        var rightVersion = TryParseVersion(right);
        if (leftVersion is not null && rightVersion is not null)
        {
            return leftVersion.CompareTo(rightVersion);
        }

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static ExternalPluginInstallPlan BuildInstallPlan(
        ExternalPluginRegistryEntry entry,
        IReadOnlyDictionary<string, WeedPluginManifest> installed)
    {
        var validation = ValidateRegistryEntry(entry);
        if (validation is not null)
        {
            return new ExternalPluginInstallPlan(entry, null, ExternalPluginInstallState.Invalid, validation);
        }

        if (!IsCompatible(entry.MinWeedVersion))
        {
            return new ExternalPluginInstallPlan(
                entry,
                null,
                ExternalPluginInstallState.Incompatible,
                $"Requires Weed {entry.MinWeedVersion} or newer.");
        }

        if (!installed.TryGetValue(entry.Id, out var installedManifest))
        {
            return new ExternalPluginInstallPlan(
                entry,
                null,
                ExternalPluginInstallState.NotInstalled,
                $"Ready to install {entry.Name} {entry.Version}.");
        }

        if (CompareVersions(entry.Version, installedManifest.Version) > 0)
        {
            return new ExternalPluginInstallPlan(
                entry,
                installedManifest,
                ExternalPluginInstallState.UpdateAvailable,
                $"Update available: {installedManifest.Version} -> {entry.Version}.");
        }

        return new ExternalPluginInstallPlan(
            entry,
            installedManifest,
            ExternalPluginInstallState.Installed,
            $"Installed: {installedManifest.Version}.");
    }

    private static Dictionary<string, WeedPluginManifest> ReadInstalledManifests(string pluginsRoot)
    {
        var manifests = new Dictionary<string, WeedPluginManifest>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(pluginsRoot))
        {
            return manifests;
        }

        foreach (var manifestPath in Directory.EnumerateFiles(pluginsRoot, "manifest.json", SearchOption.AllDirectories))
        {
            if (IsIgnoredPluginPath(pluginsRoot, manifestPath))
            {
                continue;
            }

            try
            {
                var manifest = JsonSerializer.Deserialize<WeedPluginManifest>(
                    File.ReadAllText(manifestPath),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (manifest is not null && !string.IsNullOrWhiteSpace(manifest.Id))
                {
                    manifests[manifest.Id] = manifest;
                }
            }
            catch
            {
            }
        }

        return manifests;
    }

    private static string? ValidateRegistryEntry(ExternalPluginRegistryEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Id))
        {
            return "Registry entry is missing a plugin id.";
        }

        if (string.IsNullOrWhiteSpace(entry.Name))
        {
            return $"Registry entry {entry.Id} is missing a name.";
        }

        if (string.IsNullOrWhiteSpace(entry.Version))
        {
            return $"Registry entry {entry.Id} is missing a version.";
        }

        if (string.IsNullOrWhiteSpace(entry.PackageUrl))
        {
            return $"Registry entry {entry.Id} is missing a package URL.";
        }

        if (string.IsNullOrWhiteSpace(entry.Sha256))
        {
            return $"Registry entry {entry.Id} is missing a SHA256 hash.";
        }

        if (entry.Sha256.Length != 64 || entry.Sha256.Any(character => !Uri.IsHexDigit(character)))
        {
            return $"Registry entry {entry.Id} has an invalid SHA256 hash.";
        }

        return null;
    }

    private static string? ValidateDownloadedPackage(string packagePath, ExternalPluginRegistryEntry entry)
    {
        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            var manifestEntry = archive.GetEntry("manifest.json");
            if (manifestEntry is null)
            {
                return "Registry packages must contain manifest.json at the ZIP root.";
            }

            using var stream = manifestEntry.Open();
            var manifest = JsonSerializer.Deserialize<WeedPluginManifest>(stream, JsonOptions);
            if (manifest is null)
            {
                return "Downloaded plugin manifest is empty.";
            }

            if (!manifest.Id.Equals(entry.Id, StringComparison.OrdinalIgnoreCase))
            {
                return $"Downloaded package id {manifest.Id} does not match registry id {entry.Id}.";
            }

            if (!manifest.Version.Equals(entry.Version, StringComparison.OrdinalIgnoreCase))
            {
                return $"Downloaded package version {manifest.Version} does not match registry version {entry.Version}.";
            }

            if (!string.IsNullOrWhiteSpace(entry.SdkVersion) &&
                !manifest.SdkVersion.Equals(entry.SdkVersion, StringComparison.OrdinalIgnoreCase))
            {
                return $"Downloaded package SDK {manifest.SdkVersion} does not match registry SDK {entry.SdkVersion}.";
            }

            return null;
        }
        catch (Exception ex)
        {
            return $"Downloaded plugin package could not be validated: {ex.Message}";
        }
    }

    private async Task<string> ReadTextAsync(string location, CancellationToken cancellationToken)
    {
        if (Uri.TryCreate(location, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return await _httpClient.GetStringAsync(uri, cancellationToken);
        }

        var path = location.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
            ? new Uri(location).LocalPath
            : location;
        return await File.ReadAllTextAsync(path, cancellationToken);
    }

    private async Task DownloadPackageAsync(
        string packageLocation,
        string downloadPath,
        CancellationToken cancellationToken)
    {
        if (Uri.TryCreate(packageLocation, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            await using var source = await _httpClient.GetStreamAsync(uri, cancellationToken);
            await using var target = File.Create(downloadPath);
            await source.CopyToAsync(target, cancellationToken);
            return;
        }

        var sourcePath = packageLocation.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
            ? new Uri(packageLocation).LocalPath
            : packageLocation;
        File.Copy(sourcePath, downloadPath, overwrite: true);
    }

    private static string PackageFileName(string packageLocation, ExternalPluginRegistryEntry entry)
    {
        var path = Uri.TryCreate(packageLocation, UriKind.Absolute, out var uri)
            ? uri.LocalPath
            : packageLocation;
        var name = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = $"{entry.Id}-{entry.Version}-win-x64.zip";
        }

        return name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? name : $"{name}.zip";
    }

    private static bool IsCompatible(string minWeedVersion)
    {
        if (string.IsNullOrWhiteSpace(minWeedVersion))
        {
            return true;
        }

        var minimum = TryParseVersion(minWeedVersion);
        var current = TryParseVersion(UpdateService.CurrentVersion.ToString());
        return minimum is null || current is null || current.CompareTo(minimum) >= 0;
    }

    private static Version? TryParseVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var main = value.Split(['-', '+'], 2, StringSplitOptions.TrimEntries)[0];
        return Version.TryParse(main, out var version) ? version : null;
    }

    private static bool IsIgnoredPluginPath(string pluginsDirectory, string manifestPath)
    {
        var pluginDirectory = Path.GetDirectoryName(manifestPath);
        if (pluginDirectory is not null && ExternalPluginUninstaller.IsPendingRemoval(pluginDirectory))
        {
            return true;
        }

        var relative = Path.GetRelativePath(pluginsDirectory, manifestPath);
        var parts = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        return parts.Any(part => part.StartsWith(".", StringComparison.Ordinal));
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
