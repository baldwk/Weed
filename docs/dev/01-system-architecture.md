# System Architecture

> [Back to Developer Documentation](README.md)

## Solution Structure

```text
Weed.App
  WPF launcher, tray integration, settings, theme, input, and result rendering

Weed.Core
  Query routing, ranking, history, configuration, logging, and update checks

Weed.PluginHost
  External manifest scanning, load contexts, lifecycle, import, and diagnostics

Weed.Abstractions
  Public plugin SDK interfaces, DTOs, manifests, permissions, and result models

Weed.Platform.Windows
  Global hotkeys, Shell integration, clipboard, capture, startup, and Win32 interop

Built-In Plugins
  AppLauncher, Calculator, Clipboard, Screenshot, Emoji, Translator, FileSearch,
  and RunCommand
```

## Project Responsibilities

### Weed.App

- Starts the application and composes shared services.
- Creates the launcher, settings window, tray menu, and plugin detail pages.
- Handles `Alt+Space`, focus, visibility, keyboard navigation, and single-instance activation.
- Renders structured plugin results, actions, icons, and previews.
- Applies theme resources and owns UI state.

### Weed.Core

- Loads and persists application, hotkey, and plugin settings.
- Routes Keyword, Hotkey, and ImplicitQuery activations.
- Combines plugin match scores with usage history and user priority.
- Records successful result actions.
- Provides logging, storage paths, text normalization, and update checks.
- Cancels previous queries when input changes.

### Weed.PluginHost

- Registers first-party plugins supplied by the application.
- Scans the user plugin directory for external manifests.
- Validates SDK versions, assemblies, entry types, and package boundaries.
- Creates an `AssemblyLoadContext` for each external plugin.
- Starts, stops, and disposes plugin lifecycles.
- Isolates plugin exceptions at dispatch boundaries and records diagnostics.
- Imports ZIP, DLL, published folder, and source-folder plugins.

### Weed.Abstractions

- Defines the interfaces external plugins compile against.
- Defines manifests, query and command contexts, results, actions, settings, and permissions.
- Avoids dependencies on WPF, Win32 implementations, or Host-internal types.
- Acts as the primary plugin compatibility contract.

### Weed.Platform.Windows

- Registers and unregisters global hotkeys.
- Resolves Start Menu shortcuts and launches Shell targets.
- Reads and writes clipboard formats and pastes into foreground windows.
- Captures regions, displays, and scrolling areas.
- Manages launch-at-startup and external program dependencies.
- Encapsulates handles, messages, DPI, displays, and other Win32 details.

## Startup Flow

```text
Process starts
  -> Acquire the single-instance lock or activate the existing instance
  -> Create paths, logging, settings, storage, and platform services
  -> Register built-in plugins
  -> Scan the external plugin directory
  -> Load plugin defaults and user enablement
  -> Initialize enabled plugins and start resident plugins
  -> Check and start declared external dependencies
  -> Register global hotkeys
  -> Show the tray icon and optionally the launcher
  -> Check for updates when enabled
```

Shutdown reverses this order: hotkeys are unregistered, resident plugins stop, plugin instances are disposed, and logging is flushed.

## Query Flow

```text
User opens Weed and types
  -> Core normalizes the input
  -> Router selects Keyword or ImplicitQuery activations
  -> Matching enabled plugins receive QueryContext
  -> Plugins return structured results
  -> Core adds usage and priority scores
  -> Results are sorted and rendered
```

Each input change cancels the previous query token. Plugins should honor cancellation promptly and avoid blocking the UI thread.

## Command Flow

```text
User activates a result or hotkey
  -> Core resolves CommandContext
  -> Plugin runtime dispatches the command
  -> Plugin uses Host services for platform actions
  -> Successful execution updates usage history
  -> CommandBehavior decides whether to hide, keep, or reopen the launcher
```

## Configuration Precedence

```text
Code and manifest defaults
  -> Persisted user settings
  -> Current session state
```

User choices are authoritative for plugin enablement, hotkeys, priorities, and plugin settings. New plugin versions may add defaults but should not overwrite existing values.

## Plugin Locations

First-party plugins are referenced by `Weed.App` and published with the application. External plugins are installed under:

```text
%LOCALAPPDATA%\Weed\plugins\<plugin-id>
```

They are scanned during the next application startup.

## Logging and Diagnostics

- Host logs cover startup, shutdown, plugin loading, hotkey registration, dependency startup, query failures, command failures, and unhandled exceptions.
- Plugin log messages include plugin identity through a scoped logger.
- Settings exposes plugin manifest data, permissions, external dependencies, and recent log output.
- Logs must avoid raw sensitive query content, credentials, and unnecessary clipboard data.

## Responsiveness Rules

- The UI thread handles input, rendering, and lightweight state only.
- Queries, indexing, clipboard persistence, image processing, and network operations run asynchronously.
- Cancellation is propagated through `CancellationToken`.
- Plugins should return bounded result sets and avoid expensive work for inputs they cannot match.
- Preview and icon loading must not change result-row dimensions or block typing.
