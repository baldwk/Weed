# Product Boundaries and Technical Overview

> [Back to Developer Documentation](README.md)

## Product Positioning

Weed is a launcher and productivity tool for Windows 10 and later. A single search window brings together application launch, calculation, clipboard history, screenshots, translation, file search, and external plugin workflows.

Product priorities are low interaction cost, responsive input, predictable ranking, explicit user control, and a consistent experience across first-party and external plugins.

## Current Scope

- `Alt+Space` opens the launcher; the app remains available in the tray and uses single-instance activation.
- Keyword, Hotkey, and ImplicitQuery activation paths.
- Editable global hotkeys, plugin enablement, implicit-query priority, and plugin-owned settings.
- Built-in App Launcher, Calculator, Clipboard, Screenshot, Emoji Search, Translator, File Search, and Run Command plugins.
- External plugin import from ZIP, DLL, published directory, or source directory.
- Update manifest checks, package download, and SHA256 validation.
- Manifest schemas, a plugin template, release scripts, and SmokeTests.

## Non-Goals

- External plugins are not sandboxed. Permission fields currently describe capabilities only.
- File Search does not maintain its own full-disk index; it depends on Everything.
- Translator does not ship a local translation model; it calls the selected online provider.
- The current release is a portable Windows x64 archive without an installer, automatic file replacement, or code signing.
- macOS, Linux, and mobile platforms are not supported.

## Design Principles

- The Host owns windows, themes, settings, hotkeys, query routing, result ranking, and plugin lifecycle.
- Product capabilities should be implemented as plugins where practical. First-party and external plugins share public abstractions.
- The Host renders structured results and actions so the launcher stays visually consistent.
- Persisted user settings take precedence and should survive plugin or Host upgrades.
- Queries, indexing, and expensive plugin calls should be asynchronous and cancellable.
- Data stays local by default. Networked plugins must document what leaves the machine.

## Technical Baseline

- Language and runtime: C# and .NET 9.
- Desktop UI: WPF.
- Local data: SQLite, FTS5, and JSON configuration.
- Plugin format: managed .NET DLL.
- Platform integration: Win32, Windows Shell APIs, and WPF interop.
- Current target: `win-x64`; packages require the .NET 9 Desktop Runtime x64.

## Terminology

- **Host:** The Weed application and shared runtime services.
- **Plugin:** A managed DLL that provides queries, commands, settings, or resident services through the public SDK.
- **First-party plugin:** A built-in plugin maintained and released with Weed.
- **External plugin:** A separately imported plugin that runs in the Host process.
- **Keyword:** A prefixed query such as `clip hello`.
- **Hotkey:** A configurable global shortcut such as `Shift+Ctrl+C`.
- **ImplicitQuery:** A query without a prefix, such as `1+2` or `vscode`.
- **Resident plugin:** A plugin with an active lifecycle while enabled, such as Clipboard.
- **Result:** A structured search result returned to the Host.
- **Action:** An operation such as open, copy, paste, delete, or save.
