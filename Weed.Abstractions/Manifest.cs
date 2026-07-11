using System.Text.Json.Serialization;

namespace Weed.Abstractions;

public sealed record WeedPluginManifest
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("version")]
    public string Version { get; init; } = "0.1.0";

    [JsonPropertyName("sdkVersion")]
    public string SdkVersion { get; init; } = "0.1";

    [JsonPropertyName("assembly")]
    public string? Assembly { get; init; }

    [JsonPropertyName("entryType")]
    public string? EntryType { get; init; }

    [JsonPropertyName("icon")]
    public string? Icon { get; init; }

    [JsonPropertyName("runtime")]
    public PluginRuntimeManifest Runtime { get; init; } = new();

    [JsonPropertyName("activations")]
    public IReadOnlyList<PluginActivationManifest> Activations { get; init; } = [];

    [JsonPropertyName("permissions")]
    public IReadOnlyList<string> Permissions { get; init; } = [];

    [JsonPropertyName("externalDependencies")]
    public IReadOnlyList<PluginExternalDependencyManifest> ExternalDependencies { get; init; } = [];
}

public sealed record PluginExternalDependencyManifest
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("executables")]
    public IReadOnlyList<string> Executables { get; init; } = [];

    [JsonPropertyName("requiredRunning")]
    public bool RequiredRunning { get; init; } = true;

    [JsonPropertyName("autoStart")]
    public bool AutoStart { get; init; }

    [JsonPropertyName("readinessProbe")]
    public string ReadinessProbe { get; init; } = "process";
}

public sealed record PluginRuntimeManifest
{
    [JsonPropertyName("resident")]
    public bool Resident { get; init; }
}

public sealed record PluginActivationManifest
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("keyword")]
    public string? Keyword { get; init; }

    [JsonPropertyName("command")]
    public string? Command { get; init; }

    [JsonPropertyName("provider")]
    public string? Provider { get; init; }

    [JsonPropertyName("defaultKeys")]
    public string? DefaultKeys { get; init; }

    [JsonPropertyName("configurable")]
    public bool Configurable { get; init; } = true;

    [JsonPropertyName("behavior")]
    public string? Behavior { get; init; }
}
