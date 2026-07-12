# Data and Storage

> [Back to Developer Documentation](README.md)

## Storage Roots

Weed separates roaming configuration from machine-local data:

```text
%APPDATA%\Weed\
  settings.json
  hotkeys.json
  plugins.json
  plugin-settings\

%LOCALAPPDATA%\Weed\
  weed.db
  logs\
  plugins\
  plugins-data\
  cache\
  clipboard-objects\
  updates\
```

Screenshots default to `%USERPROFILE%\Pictures\Weed`. `clipboard-objects` is a Host-reserved path; the current Clipboard plugin stores large objects under `plugins-data\weed.clipboard\objects`.

## Global Configuration

### settings.json

Stores application settings:

```json
{
  "theme": "system",
  "showTrayIcon": true,
  "launchAtStartup": false,
  "autoCheckUpdates": false,
  "updateManifestUrl": "",
  "externalPluginRegistryUrl": "",
  "mainHotkey": "Alt+Space",
  "closeOnLostFocus": true
}
```

### hotkeys.json

Stores plugin hotkey overrides keyed by `<pluginId>:<command>`:

```json
{
  "weed.clipboard:clipboard.show": {
    "keys": "Shift+Ctrl+C",
    "enabled": true
  },
  "weed.screenshot:screenshot.region": {
    "keys": "Shift+Alt+A",
    "enabled": true
  }
}
```

### plugins.json

Stores plugin enablement and ImplicitQuery priority:

```json
{
  "weed.appLauncher": {
    "enabled": true,
    "priority": 0
  }
}
```

`priority` is clamped from `0` to `100` and affects only implicit-query ranking.

### plugin-settings\<pluginId>.json

Each plugin stores its settings in a separate JSON file. The Host renders and persists fields defined by `IPluginSettingsProvider` without interpreting their business meaning.

Secrets are currently stored as plain JSON in the user configuration directory. Logs must never include credentials. A future credential-store migration should preserve setting-key compatibility.

## Core Database

`%LOCALAPPDATA%\Weed\weed.db` stores usage history and schema migrations. The main history table is:

```sql
CREATE TABLE usage_history (
  plugin_id TEXT NOT NULL,
  result_id TEXT NOT NULL,
  command_id TEXT NOT NULL,
  selected_count INTEGER NOT NULL DEFAULT 0,
  last_selected_at TEXT,
  PRIMARY KEY (plugin_id, result_id, command_id)
);
```

Successful result execution updates selection count and time, which are mapped into UsageScore during ranking.

## Plugin Data

Plugins receive separate data and cache directories from the Host:

```text
%LOCALAPPDATA%\Weed\plugins-data\<pluginId>
%LOCALAPPDATA%\Weed\cache\<pluginId>
```

- App Launcher stores its rebuildable index in `plugins-data\weed.appLauncher\app-launcher.db` and icons in `cache\weed.appLauncher\icons`.
- Clipboard stores metadata and its FTS index in `plugins-data\weed.clipboard\clipboard.db`; image, HTML, and RTF objects live under `objects` in the same plugin directory.
- OCR stores captures and text results in `plugins-data\weed.ocr`.

Invalid Windows filename characters in plugin IDs are replaced when directory names are generated.

## Clipboard Retention

Clipboard defaults to 180 days, 100,000 records, and a 2,048 MB object quota. Pinned items have retention priority. Deleting or pruning a record must also remove unreferenced object files.

Supported content types are `text`, `image`, `files`, `rtf`, and `html`. The database stores searchable text, hashes, paths, timestamps, pinned state, and size metadata; large raw objects stay outside the primary table.

## Screenshot Output

Screenshots default to `%USERPROFILE%\Pictures\Weed`, overridden by `defaultSaveDirectory`. Filenames use `Screenshot-{yyyyMMdd-HHmmss}` with an extension selected from PNG or JPEG settings.

The plugin also persists JPEG quality, maximum saved file size, default annotation color, and line width. Editor state is session-only.

## External Plugins and Updates

- External plugin directory: `%LOCALAPPDATA%\Weed\plugins\<manifest.id>`.
- Update download directory: `%LOCALAPPDATA%\Weed\updates`.
- Replacing or deleting the application directory does not remove this user data.

The importer must keep manifest and assembly paths inside the plugin package to prevent path traversal outside the installation root.

## Migration and Cleanup

- SQLite databases record applied versions in `schema_migrations`; migrations must run in order and remain diagnosable.
- Configuration writes use a `.tmp` file followed by atomic replacement.
- Plugins own cleanup of their expired data, objects, and caches. Core must not guess plugin data formats.
- Logs, downloaded updates, and rebuildable caches need independent cleanup policies that never remove user settings or installed plugins.
- Breaking schema or directory changes require a migration and rollback path from the previous stable release.
