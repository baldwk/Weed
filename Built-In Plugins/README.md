# Weed Built-In Plugin Guide

Weed includes eight built-in plugins. Use **Settings > Plugins** to enable or disable them, adjust implicit-query priority, and edit plugin-specific settings.

| Plugin | Entry | Purpose |
| --- | --- | --- |
| App Launcher | Enter an app name directly | Find and launch applications |
| Calculator | Enter an expression directly | Calculate, copy, or paste a result |
| Clipboard | `clip`, `Shift+Ctrl+C` | Search and reuse clipboard history |
| Screenshot | `shot`, `Shift+Alt+A` | Capture a region, screen, or scrolling area |
| Emoji Search | `emoji` | Find and copy emoji |
| Translator | `tr`, `translate` | Translate text quickly |
| File Search | `file` | Search files through Everything |
| Run Command | Enter a command name directly | Open common Windows tools |

## App Launcher

Enter an application name to search Start Menu shortcuts and packaged Windows apps. Matching supports display names, pinyin, pinyin initials, and common English acronyms.

```text
Visual Studio Code
weixin
wx
```

Actions include open, run as administrator, open location, and copy path or app ID. Enter `refresh apps` to rebuild the application list manually.

The plugin setting can hide low-value uninstall and maintenance shortcuts.

## Calculator

Enter a mathematical expression directly; no keyword is required.

```text
1+2*3
(3+5)/2
sqrt(9)
5!
50%
ln(e)
log(100)
log2(8)
```

Supported features:

- `+`, `-`, `*`, `/`, `%`, `^`, `**`, and parentheses.
- Factorials and postfix percentages.
- `sqrt`, `abs`, `sin`, `cos`, `tan`, and `round`.
- Natural log `ln`, base-10 log `log`, and arbitrary-base `logN` functions.
- Constants `pi` and `e`.
- Common full-width input forms, including full-width digits, localized parentheses, `×`, and `÷`.

The default action copies the result. Other actions paste it into the foreground window or copy the complete equation. Decimal precision is configurable.

## Clipboard

Clipboard records history while the plugin is enabled. Enter `clip` to browse recent content or append a query to search.

```text
clip meeting
clip type:text
clip type:image
clip type:files project
```

It supports text, images, file lists, HTML, and RTF. Search supports regular terms, pinyin, pinyin initials, and type filters.

Available actions:

- Copy an entry or paste it into the foreground window.
- Open a preview or location for images, files, and rich content.
- Pin frequently used content.
- Delete an entry.

Settings control image and file-list capture, retention days, maximum records, result count, and object storage quota. Clipboard data stays on the local machine and is not cleared automatically when Weed exits.

## Screenshot

Enter `shot` or press `Shift+Alt+A` to start a region capture.

Capture modes:

- **Capture region:** Drag to select an area.
- **Capture primary screen:** Capture the primary display.
- **Capture scrolling area:** Select a scrolling area and stitch it into one image.

After a region capture, use the pen, rectangle, ellipse, color, line width, undo, redo, and clear controls. Copy the result or save it as PNG or JPEG.

Settings control the save directory, format, JPEG quality, maximum file size, annotation color, and line width.

## Emoji Search

Enter `emoji`, then search by English name, alias, category, or shortcode.

```text
emoji smile
emoji rocket
emoji :heart:
```

The default action copies the emoji. Other actions copy its `:shortcode:` or English name. Settings control the maximum results and default copy format.

## Translator

Use `tr` or `translate` to translate text.

```text
tr hello
tr en zh-CN hello
translate auto en hello
translate ja zh-CN arigatou
```

- `tr text` uses the configured source and target languages.
- `tr source target text` temporarily specifies both languages.

Google Translate and Baidu Translate are supported. Google works without credentials; Baidu requires an App ID and secret key. The default action copies the translation. Other actions copy source and translated text together or swap languages and run the query again.

Settings include provider, default languages, secondary target language, query delay, service URLs, and system/no/custom proxy modes. Translation text is sent to the selected provider, so do not submit sensitive content you do not want that service to process.

## File Search

File Search uses the local Everything index. Install [Everything](https://www.voidtools.com/) before using it.

```text
file report
file *.pdf invoice
file path:projects weed
```

Queries support Everything search syntax. Actions include open, open location, and copy full path.

Settings control whether folders are included, the maximum results, and Everything sort order. Weed can attempt to start an installed Everything instance, but it does not install Everything or alter its startup settings.

## Run Command

Enter a supported Windows command or tool name directly. This plugin opens only its built-in allowlist and never executes arbitrary input.

```text
cmd
regedit
taskmgr
services.msc
devmgmt.msc
diskmgmt.msc
control
appwiz.cpl
ncpa.cpl
sysdm.cpl
mstsc
notepad
calc
explorer
```

Exact command matches rank first, and display-name search is also supported. Press `Enter` to open the selected tool.

## Related Documentation

- [Weed User Guide](../docs/user-guide.md)
- [OCR External Plugin](../External%20Plugins/Weed.Plugins.Ocr/README.md)
- [First-Party Plugin Technical Specification](../docs/dev/05-first-party-plugins.md)
