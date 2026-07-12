# Weed User Guide

## Install and Start

1. Download `Weed-win-x64.zip` from [GitHub Releases](https://github.com/baldwk/Weed/releases/latest).
2. Make sure the [.NET 9 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/9.0) is installed.
3. Extract the whole archive to a permanent folder and run `Weed.App.exe`.

The search window appears on first launch and Weed creates an icon in the notification area. Closing the search window does not exit the app; use the tray menu to exit. Running `Weed.App.exe` again activates the existing window rather than creating another process.

## Search and Actions

Press `Alt+Space` to open Weed. After entering a query:

- Use `Up` and `Down` to select a result.
- Press `Enter` to run the selected result's default action.
- Use the action hints to run other available actions.
- Press `Esc` to hide the search window.

Apps, calculator expressions, and system commands do not need a prefix. Clipboard, screenshot, translation, emoji, and file search use keywords.

| Scenario | Example |
| --- | --- |
| Launch an app | `visual studio code`, `vscode`, or pinyin initials |
| Calculate | `(3+5)/2`, `sqrt(9)`, `log10(100)` |
| Search clipboard | `clip invoice`, `clip type:files` |
| Take a screenshot | `shot` |
| Translate | `tr hello`, `tr en zh-CN hello` |
| Find emoji | `emoji smile`, `emoji :heart:` |
| Find a file | `file *.pdf invoice` |
| Open a Windows tool | `taskmgr`, `services.msc` |

See the [Built-In Plugin Guide](../Built-In%20Plugins/README.md) for complete query syntax and actions.

## Settings

Open Settings from the button in the upper-right corner of the search window.

- **General:** Tray icon, close-on-focus-loss behavior, launch at startup, and appearance.
- **Hotkeys:** Main activation hotkey and plugin hotkeys. Choose another combination if a hotkey conflicts with another app.
- **Plugin pages:** Enable or disable a plugin, adjust implicit-query priority, edit plugin options, and inspect permissions, dependencies, manifests, and logs.
- **External Plugins:** Install or update verified catalog packages, uninstall installed packages, or import a ZIP, DLL, published folder, or source folder.
- **Updates:** Configure the update manifest and check for new releases.

Settings are saved automatically. Restart Weed after replacing an external plugin or when a plugin explicitly requires a restart.

## Prepare File Search

File Search depends on [Everything](https://www.voidtools.com/) for the local file index. Install Everything first. When File Search is enabled, Weed can attempt to start an existing Everything installation, but it does not install Everything or change its startup configuration.

## External Plugins

Open **Settings > External Plugins** to browse the stable catalog. Select a compatible package and choose **Install** or **Update**; Weed downloads it, verifies its SHA256 checksum and manifest identity, and imports it into the user plugin directory. Restart Weed after installation or update.

Manual import remains available for a plugin ZIP, DLL, published folder, or source folder. To uninstall a package, select it in the installed list, choose **Uninstall**, confirm the removal, and restart Weed. Plugin settings and data are preserved so reinstalling the same plugin can reuse them. The official registry URL is configured by default and can be changed on the same page for development or private catalogs.

External plugins are not sandboxed. They run inside the Weed process and may access the screen, clipboard, files, or network according to their declared capabilities. Install only trusted plugins with a clear source and version.

The OCR plugin needs model files and native runtime dependencies. The Toolbox plugin provides local UUID, timestamp, encoding, hashing, and JSON utilities. See the [OCR Plugin Guide](../External%20Plugins/Weed.Plugins.Ocr/README.md) and [Toolbox Plugin Guide](../External%20Plugins/Weed.Plugins.Toolbox/README.md).

## Updates

New versions are published on [GitHub Releases](https://github.com/baldwk/Weed/releases). You can download a new archive and replace the application files. Replacing or deleting the application directory does not remove user settings or history.

You can also enter the release's `Weed-win-x64.update.json` URL under **Settings > Updates**. Weed validates the package SHA256 before download. Follow the UI instructions to finish installing the downloaded version.

## Data Locations

| Data | Location |
| --- | --- |
| User settings and hotkeys | `%APPDATA%\Weed` |
| History, caches, plugin data, and updates | `%LOCALAPPDATA%\Weed` |
| External plugins | `%LOCALAPPDATA%\Weed\plugins` |
| Logs | `%LOCALAPPDATA%\Weed\logs` |
| Default screenshots | `%USERPROFILE%\Pictures\Weed` |

Search history and configuration stay on the local machine by default. Translator sends the input text to the selected translation provider. Imported plugins may have their own data-handling behavior.

## Troubleshooting

### `Alt+Space` does not respond

Check whether another app owns the hotkey. Open Settings from the tray and choose another main hotkey. Restart Weed and inspect the logs if the new shortcut also fails.

### File Search says Everything is unavailable

Make sure Everything is installed, running, and exposing IPC. Try the query again or restart Weed.

### An imported plugin does not appear

Restart Weed, then check the plugin's enabled state and log. Preserve the original ZIP structure; do not extract and import only the primary DLL.

### Remove all personal data

Exit Weed, then delete `%APPDATA%\Weed` and `%LOCALAPPDATA%\Weed`. This permanently removes settings, history, caches, plugin data, and imported plugins. Back up anything you need first.
