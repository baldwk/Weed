# Plugin System

> [Back to Developer Documentation](README.md)

## Plugin Format

Weed plugins are managed .NET DLLs that communicate with the Host through `Weed.Abstractions`.

Recommended targets:

- `Weed.Abstractions`: `net9.0`
- Windows plugins and the Host: `net9.0-windows`
- Platform-neutral plugins: `net9.0`

The entry type must be public and implement `IWeedPlugin`. A plugin may also implement query, command, resident lifecycle, or settings interfaces.

## Package Layout

```text
com.example.plugin\
  manifest.json
  Example.Plugin.dll
  Example.Plugin.deps.json
  assets\
    icon.png
  runtimes\
    win-x64\
      native\
```

`manifest.json` is the discovery entry point for external plugins. Assembly and asset paths are relative to the package root and must remain inside it.

## Manifest Example

```json
{
  "id": "com.example.clipboard",
  "name": "Clipboard History",
  "version": "0.1.0",
  "sdkVersion": "0.1",
  "assembly": "Example.Clipboard.dll",
  "entryType": "Example.Clipboard.ClipboardPlugin",
  "icon": "assets/icon.png",
  "runtime": {
    "resident": true
  },
  "activations": [
    {
      "type": "keyword",
      "keyword": "clip",
      "command": "clipboard.search"
    },
    {
      "type": "hotkey",
      "command": "clipboard.show",
      "defaultKeys": "Shift+Ctrl+C",
      "configurable": true,
      "behavior": "showLauncher"
    }
  ],
  "permissions": [
    "clipboard.read",
    "clipboard.write",
    "storage.local"
  ]
}
```

## Manifest Fields

| Field | Meaning |
| --- | --- |
| `id` | Stable, unique plugin identity and settings namespace |
| `name` | User-visible plugin name |
| `version` | Plugin version |
| `sdkVersion` | Compatible Weed SDK version |
| `assembly` | Entry assembly path relative to package root |
| `entryType` | Fully qualified public plugin type |
| `icon` | Optional icon asset path |
| `runtime.resident` | Whether resident lifecycle is required |
| `activations` | Keyword, Hotkey, and ImplicitQuery entries |
| `permissions` | Declared Host capabilities |
| `externalDependencies` | Optional external programs and readiness probes |

Validate external manifests with [`schemas/manifest.schema.json`](../../schemas/manifest.schema.json).

## Activation Types

### Keyword

```json
{
  "type": "keyword",
  "keyword": "weather",
  "command": "weather.search"
}
```

The router removes the matched keyword and passes the remaining input to the plugin. Keywords should be short, stable, and unique enough to avoid ambiguity.

### Hotkey

```json
{
  "type": "hotkey",
  "command": "clipboard.show",
  "defaultKeys": "Shift+Ctrl+C",
  "configurable": true,
  "behavior": "showLauncher"
}
```

The Host owns global registration and user overrides. A hotkey may open the launcher with an initial query or execute a command directly, depending on its declared behavior.

### ImplicitQuery

```json
{
  "type": "implicitQuery",
  "provider": "calculator"
}
```

Implicit providers receive unprefixed text. They must reject irrelevant input cheaply and return meaningful match scores because their results compete across plugins.

## Lifecycle Interfaces

### IWeedPlugin

```csharp
public interface IWeedPlugin : IAsyncDisposable
{
    ValueTask InitializeAsync(IWeedHost host, CancellationToken cancellationToken);
}
```

Initialization receives a restricted Host API. Plugins must not assume the WPF application is visible or that another plugin has initialized first.

### IQueryProvider

```csharp
public interface IQueryProvider
{
    string ProviderId { get; }

    ValueTask<IReadOnlyList<WeedResult>> QueryAsync(
        QueryContext context,
        CancellationToken cancellationToken);
}
```

Queries should be deterministic for the same state, return bounded collections, and honor cancellation.

### ICommandHandler

```csharp
public interface ICommandHandler
{
    ValueTask<CommandResult> ExecuteAsync(
        CommandContext context,
        CancellationToken cancellationToken);
}
```

Commands are stable IDs declared by results or activations. `CommandResult` controls success state, status text, and launcher behavior.

### IResidentPlugin

Resident plugins implement start and stop methods for background observation such as clipboard monitoring. Start must be idempotent, stop must release resources, and neither method may block the UI thread.

### IPluginSettingsProvider

Settings providers return typed field definitions for booleans, integers, strings, paths, and selections. The Host renders and persists them under the plugin ID.

## Host API

`IWeedHost` exposes controlled services such as:

- `Settings`: Read and write plugin-scoped settings.
- `Storage`: Obtain plugin data and cache directories.
- `Logger`: Write scoped diagnostic messages.
- `Clipboard`: Read, write, and paste supported formats.
- `Shell`: Open files, folders, URLs, and processes.
- `ScreenCapture`: Capture screen content through the platform layer.

Plugins should use these services instead of reaching into Host implementation types.

## Results and Actions

`WeedResult` includes a stable ID, plugin ID, title, subtitle, icon, match score, default command, optional preview data, and actions.

```csharp
new WeedResult
{
    Id = "weather-shanghai",
    PluginId = "com.example.weather",
    Title = "24 C, Clear",
    Subtitle = "Shanghai",
    MatchScore = 25,
    DefaultCommand = "weather.copy",
    Actions =
    [
        new WeedAction
        {
            Command = "weather.copy",
            Title = "Copy forecast",
            Shortcut = "Enter"
        }
    ]
};
```

Result and command IDs should remain stable so usage history continues to influence the same logical actions across releases.

## Permissions

Common declarations include:

```text
clipboard.read
clipboard.write
window.paste
screen.capture
file.read
file.write
shell.launch
network
storage.local
```

Permissions are descriptive in the current runtime. They help users review plugins but do not prevent undeclared access. External plugin documentation must explain sensitive capabilities and network behavior.

## Load Context and Dependencies

- `Weed.Abstractions` comes from the Host default context and should use `<Private>false</Private>`.
- Other managed dependencies are resolved from the plugin directory.
- Native libraries belong under normal runtime-specific publish paths.
- Static event handlers, background threads, timers, and unmanaged handles must be released during disposal so unload can succeed.
- Type identity must never cross contexts through a private copy of `Weed.Abstractions`.

## First-Party Plugins

First-party plugins use the same manifests, result models, settings, and lifecycle contracts, but `Weed.App` references and registers them directly. External scanning and import are not used for built-ins.

## Developer Resources

- [Plugin Template](../../templates/plugin/README.md)
- [External Plugin Development](08-external-plugins.md)
- [Manifest Schema](../../schemas/manifest.schema.json)
- [First-Party Plugin Specification](05-first-party-plugins.md)
