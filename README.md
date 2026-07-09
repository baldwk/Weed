# Weed MVP

Weed is an Alfred-style launcher and workflow tool for Windows. This repository contains a runnable MVP based on the specification in `docs/`.

## Run

```powershell
dotnet run --project Weed.App\Weed.App.csproj
```

The app opens the launcher window on startup and also creates a tray icon. Search examples:

- `1+2*3` returns a calculator result.
- `sqrt(9)` returns `3`.
- Type part of an installed Start Menu app name to launch it.
- `clip keyword` searches captured clipboard text.
- `shot` opens screenshot actions.

## Current Scope

- WPF host application with Alfred-style centered search window.
- Built-in plugin source projects live under `Built-In Plugins`; external plugin examples live under `External Plugins`.
- Built-in plugin features are documented in [Built-In Plugins/README.md](Built-In%20Plugins/README.md).
- Settings files under `%APPDATA%\Weed`.
- Logs under `%LOCALAPPDATA%\Weed\logs`.
- SQLite database at `%LOCALAPPDATA%\Weed\weed.db` for usage history and migrations.
- Built-in plugin runtime and external manifest scanning from `%LOCALAPPDATA%\Weed\plugins`.
- External plugin UI imports ZIP packages, DLLs, compiled folders, and source folders into `%LOCALAPPDATA%\Weed\plugins`.
- Query routing for Keyword, Hotkey, and ImplicitQuery.
- Ranking with plugin match score, usage score, and plugin priority.
- Built-in AppLauncher, Calculator, Clipboard, Screenshot, Run Command, Emoji Search, Translator, and File Search plugins.
- AppLauncher indexes Start Menu shortcuts, persists a SQLite cache, filters uninstall/maintenance shortcuts by default, resolves `.lnk` target metadata, supports name/acronym/pinyin/pinyin-initial matching, extracts app icons into cache, and exposes open, run as administrator, open location, copy path, and manual refresh actions.
- Clipboard uses a native clipboard listener with polling fallback, captures text, images, file lists, RTF summaries, and HTML summaries; metadata and FTS search live in SQLite, pinyin/type filters are supported, and image/rich objects are cleaned up by retention and object quota settings.
- Screenshot capture supports region, primary-screen, and scrolling-area capture with a stoppable progress window. Region capture stays in the screenshot overlay with a size hint and magnifier, then uses a bottom toolbar for pen, rectangle, ellipse, color, line width, undo, redo, clear, copy, and PNG/JPEG save controls.
- Emoji Search supports the `emoji` keyword over built-in emoji names, aliases, categories, and shortcodes.
- Translator supports `tr` and `translate` keywords, Google Translate and Baidu Translate (百度翻译), configurable default languages, query delay, and proxy modes.
- File Search supports the `file` keyword through the Everything SDK and Everything's local index; Weed does not build its own full-disk file index.
- OCR is available as a packaged external plugin using RapidOCRLib and PP-OCRv5 Chinese models; it is not referenced by `Weed.App`.
- Settings use a sidebar layout and include launch-at-startup, hotkey editing, plugin enablement, implicit-query plugin priority, plugin-owned settings, and plugin details with manifest and log diagnostics.
- The app ships a native application icon, tray icon, and first-party plugin icons.
- Host logs startup, shutdown, plugin loading, command errors, and unhandled exceptions.
- Update checks can read an HTTP or local manifest, compare versions, verify package SHA256, and download packages under `%LOCALAPPDATA%\Weed\updates`.
- Release scripts under `scripts/` can publish, create a ZIP package and update manifest, install for the current user, and uninstall while preserving user data.

## Publish And Install

```powershell
powershell -ExecutionPolicy Bypass -File scripts\publish-release.ps1
powershell -ExecutionPolicy Bypass -File scripts\install-current-user.ps1
```

`publish-release.ps1` emits:

- `artifacts\Weed-win-x64\` runnable app folder.
- `artifacts\Weed-win-x64.zip` release package.
- `artifacts\Weed-win-x64.update.json` update manifest with version, package URL, and SHA256.
- `docs\`, `schemas\`, and `templates\` inside the app folder for plugin development.

Set the manifest path or URL in Settings > Updates to check and download updates. Relative `packageUrl` values are resolved next to the manifest.

Uninstall app files and the Start Menu shortcut:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\uninstall-current-user.ps1
```

Plugin SDK contracts live in `Weed.Abstractions`. A manifest schema and C# plugin template are included in the published app folder.

## External Plugin Distribution

External plugins should live in independent repositories and publish ZIP packages through GitHub Releases, or be imported directly from a local DLL or source folder. A packaged ZIP root should contain `manifest.json`, the plugin DLL, `.deps.json`, and dependencies. Weed copies imports into `%LOCALAPPDATA%\Weed\plugins\<plugin-id>` from Settings > External Plugins.

The implementation targets `net9.0` / `net9.0-windows` because that is the installed SDK on this machine. The spec names .NET 10 LTS; upgrading is a project-file change once the SDK is installed.
