using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Weed.Core;

public sealed record UpdateManifest
{
    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("publishedAt")]
    public string? PublishedAt { get; init; }

    [JsonPropertyName("packageUrl")]
    public required string PackageUrl { get; init; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; init; }

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }
}

public sealed record UpdateCheckResult
{
    public required Version CurrentVersion { get; init; }

    public UpdateManifest? Manifest { get; init; }

    public bool IsUpdateAvailable { get; init; }

    public string Message { get; init; } = string.Empty;
}

public sealed record UpdatePackageResult
{
    public required string PackagePath { get; init; }

    public string? Sha256 { get; init; }

    public bool Verified { get; init; }

    public string Message { get; init; } = string.Empty;
}

public sealed class UpdateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly HttpClient _httpClient;

    public UpdateService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(12)
        };
    }

    public static Version CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version ??
        Assembly.GetExecutingAssembly().GetName().Version ??
        new Version(0, 1, 0);

    public async ValueTask<UpdateCheckResult> CheckAsync(string manifestLocation, CancellationToken cancellationToken)
    {
        var current = CurrentVersion;
        if (string.IsNullOrWhiteSpace(manifestLocation))
        {
            return new UpdateCheckResult
            {
                CurrentVersion = current,
                Message = "No update manifest URL is configured."
            };
        }

        try
        {
            var manifest = await ReadManifestAsync(manifestLocation, cancellationToken);
            if (!Version.TryParse(manifest.Version, out var available))
            {
                return new UpdateCheckResult
                {
                    CurrentVersion = current,
                    Manifest = manifest,
                    Message = $"Update manifest has an invalid version: {manifest.Version}"
                };
            }

            var isUpdateAvailable = available > current;
            return new UpdateCheckResult
            {
                CurrentVersion = current,
                Manifest = manifest,
                IsUpdateAvailable = isUpdateAvailable,
                Message = isUpdateAvailable
                    ? $"Version {available} is available."
                    : $"Weed is up to date ({current})."
            };
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult
            {
                CurrentVersion = current,
                Message = $"Update check failed: {ex.Message}"
            };
        }
    }

    public async ValueTask<UpdateManifest> ReadManifestAsync(string manifestLocation, CancellationToken cancellationToken)
    {
        string json;
        if (Uri.TryCreate(manifestLocation, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            json = await _httpClient.GetStringAsync(uri, cancellationToken);
        }
        else
        {
            var path = manifestLocation.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
                ? new Uri(manifestLocation).LocalPath
                : manifestLocation;
            json = await File.ReadAllTextAsync(path, cancellationToken);
        }

        return JsonSerializer.Deserialize<UpdateManifest>(json, JsonOptions) ??
               throw new InvalidDataException("Update manifest is empty.");
    }

    public async ValueTask<UpdatePackageResult> DownloadPackageAsync(
        UpdateManifest manifest,
        string destinationDirectory,
        CancellationToken cancellationToken,
        string? manifestLocation = null)
    {
        if (string.IsNullOrWhiteSpace(manifest.PackageUrl))
        {
            throw new InvalidDataException("Update manifest does not contain a package URL.");
        }

        Directory.CreateDirectory(destinationDirectory);
        var packageLocation = ResolvePackageLocation(manifest.PackageUrl, manifestLocation);
        var packageName = Path.GetFileName(Uri.TryCreate(packageLocation, UriKind.Absolute, out var packageUri)
            ? packageUri.LocalPath
            : packageLocation);

        if (string.IsNullOrWhiteSpace(packageName))
        {
            packageName = $"Weed-{manifest.Version}.zip";
        }

        var packagePath = Path.Combine(destinationDirectory, packageName);
        if (Uri.TryCreate(packageLocation, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            await using var source = await _httpClient.GetStreamAsync(uri, cancellationToken);
            await using var target = File.Create(packagePath);
            await source.CopyToAsync(target, cancellationToken);
        }
        else
        {
            var sourcePath = packageLocation.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
                ? new Uri(packageLocation).LocalPath
                : packageLocation;
            if (!Path.GetFullPath(sourcePath).Equals(Path.GetFullPath(packagePath), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(sourcePath, packagePath, true);
            }
        }

        var sha256 = Sha256File(packagePath);
        var verified = string.IsNullOrWhiteSpace(manifest.Sha256) ||
                       string.Equals(sha256, manifest.Sha256, StringComparison.OrdinalIgnoreCase);
        if (!verified)
        {
            File.Delete(packagePath);
            return new UpdatePackageResult
            {
                PackagePath = packagePath,
                Sha256 = sha256,
                Verified = false,
                Message = "Downloaded package hash did not match the update manifest."
            };
        }

        return new UpdatePackageResult
        {
            PackagePath = packagePath,
            Sha256 = sha256,
            Verified = true,
            Message = $"Downloaded update package to {packagePath}"
        };
    }

    public static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string ResolvePackageLocation(string packageUrl, string? manifestLocation)
    {
        if (Uri.TryCreate(packageUrl, UriKind.Absolute, out _))
        {
            return packageUrl;
        }

        if (string.IsNullOrWhiteSpace(manifestLocation))
        {
            return packageUrl;
        }

        if (Uri.TryCreate(manifestLocation, UriKind.Absolute, out var manifestUri) &&
            (manifestUri.Scheme == Uri.UriSchemeHttp || manifestUri.Scheme == Uri.UriSchemeHttps))
        {
            return new Uri(manifestUri, packageUrl).ToString();
        }

        var manifestPath = manifestLocation.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
            ? new Uri(manifestLocation).LocalPath
            : manifestLocation;
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifestPath) ?? ".", packageUrl));
    }
}
