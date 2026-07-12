# First-Party Plugin Specification

> [Back to Developer Documentation](README.md)

## Overview

Weed ships these built-in plugins:

| Plugin | ID | Activation |
| --- | --- | --- |
| App Launcher | `weed.appLauncher` | ImplicitQuery |
| Calculator | `weed.calculator` | ImplicitQuery |
| Clipboard | `weed.clipboard` | `clip`, `Shift+Ctrl+C` |
| Screenshot | `weed.screenshot` | `shot`, `Shift+Alt+A` |
| Emoji Search | `weed.emoji` | `emoji` |
| Translator | `weed.translate` | `tr`, `translate` |
| File Search | `weed.fileSearch` | `file` |
| Run Command | `weed.runCommand` | ImplicitQuery |

All built-ins use the public manifest, lifecycle, result, action, and settings models. `Weed.App` references and registers them directly rather than loading external package manifests.

## App Launcher

### Scope

App Launcher indexes:

- Current-user and all-user Start Menu shortcuts.
- Classic and packaged applications exposed by `shell:AppsFolder`.
- Relevant `.lnk` target metadata, working directory, arguments, and icons.

Maintenance and uninstall shortcuts are hidden by default. The plugin must not index arbitrary executable files from the full disk.

### Persistence

The application index is stored in the plugin data directory as `app-launcher.db`. Extracted icons are stored in the plugin cache directory. Cached data is rebuildable and should never be treated as the source of truth.

Startup loads the cache first for responsiveness, then refreshes from Shell sources. Manual refresh aliases include:

```text
refresh apps
refresh applications
app refresh
apps refresh
```

### Matching

Matching signals include:

- Exact and prefix display-name matches.
- Token and substring matches.
- English acronym matches.
- Pinyin and pinyin-initial matches.
- Subsequence fallback.
- AppUserModelId fallback for packaged apps.

Exact matches must remain above looser matches even when usage history favors another app.

### Actions

- Open application.
- Run a traditional desktop app as administrator.
- Open the target location.
- Copy target path or packaged app ID.
- Refresh the index.

Packaged Windows apps do not expose the traditional run-as-administrator action.

## Calculator

### Recognition

Calculator is an implicit provider and must reject normal words quickly. Expression-like inputs include numeric operators, parentheses, known functions, constants, and full-width equivalents.

Normalization includes:

- Full-width digits to ASCII.
- Localized parentheses to `(` and `)`.
- `×` to `*` and `÷` to `/`.
- Normalized whitespace.

### Supported Expressions

```text
1+2*3
(3+5)/2
5!
50%
2^8
sqrt(9)
ln(e)
log(100)
log2(8)
```

Supported features:

- Addition, subtraction, multiplication, division, remainder, and exponentiation.
- Parentheses and unary signs.
- Factorials and postfix percentages.
- `sqrt`, `abs`, `sin`, `cos`, `tan`, and `round`.
- `ln`, base-10 `log`, and arbitrary-base `logN`.
- Constants `pi` and `e`.

Invalid syntax, invalid log bases, division errors, or non-finite results should fail without affecting other query providers.

### Results and Actions

The result subtitle preserves enough of the expression to explain the answer. Output precision is bounded by the `decimalPrecision` plugin setting.

- Copy result.
- Paste result into the foreground window.
- Copy the complete equation and result.

## Clipboard

### Lifecycle

Clipboard is resident. It starts a native clipboard listener and uses periodic polling as a fallback. Capture and persistence must not block the UI thread.

Self-generated writes should be deduplicated so copy and paste actions do not create unnecessary duplicate history entries.

### Supported Content

```text
text
image
files
rtf
html
```

Metadata and searchable text are stored in `clipboard.db`. Large image and rich-text objects are stored as files under the plugin data directory.

### Query

```text
clip
clip project plan
clip type:text
clip type:image
clip type:files project
```

Search combines SQLite FTS, normalized text, pinyin, pinyin initials, and subsequence matching. Pinned items rank before unpinned items, followed by match quality and recency.

### Actions

- Copy entry to the clipboard.
- Copy and paste into the foreground window.
- Open an image, file, or rich-content preview/location.
- Pin or unpin.
- Delete.

### Retention

Settings include:

- `captureImages`
- `captureFileLists`
- `retentionDays`
- `maxItems`
- `resultLimit`
- `maxObjectMegabytes`

Cleanup must preserve pinned entries where possible, remove unreferenced objects, and enforce age, record count, and object quota independently.

## Screenshot

### Activation

```text
shot
Shift+Alt+A
```

The keyword returns region, primary-screen, and scrolling-area actions. The hotkey executes region capture directly.

### Region Capture

The platform layer displays a multi-monitor overlay, tracks drag bounds in virtual-screen coordinates, and shows size and magnifier feedback. Cancellation returns cleanly without creating output.

After selection, the editor provides:

- Pen, rectangle, and ellipse annotations.
- Color and line-width selection.
- Undo, redo, and clear.
- Copy to clipboard.
- Save as PNG or JPEG.

### Primary and Scrolling Capture

Primary capture records the primary display and opens the shared output workflow. Scrolling capture selects an area, captures multiple frames, reports progress, supports cancellation, and stitches content into a single image before editing.

Scrolling capture must bound runtime, output size, and duplicate-frame handling. Failure should preserve any recoverable image and show a diagnostic rather than silently closing.

### Settings

- `defaultSaveDirectory`
- `defaultFormat`
- `jpegQuality`
- `maxSavedFileMegabytes`
- `defaultColor`
- `defaultLineWidth`

## Emoji Search

### Data

Emoji Search loads the embedded `emoji-test.txt` dataset and falls back to a small built-in set if the resource is unavailable.

### Query

```text
emoji
emoji smile
emoji rocket
emoji :heart:
```

Matching covers names, shortcodes, aliases, categories, and subcategories. An empty query returns a bounded default list. Common user terms may map to aliases such as `love` for heart-related emoji.

### Actions and Settings

- Copy emoji character.
- Copy `:shortcode:`.
- Copy English name.

`maxResults` bounds output. `copyFormat` selects the default action format from Emoji, Shortcode, or Name.

## Translator

### Query Syntax

```text
tr hello
tr en zh-CN hello
translate auto en hello
```

- `tr text` uses configured defaults.
- `tr source target text` specifies both languages for the current query.
- `auto` asks the provider to detect the source language.

If detected input already uses the default target language, the plugin can switch to `secondaryTargetLanguage` for unlabeled queries.

### Providers

- Google Translate through the configured Google base URL.
- Baidu Translate with App ID and secret key.

Network calls honor query cancellation and `queryDelayMilliseconds` to avoid a request for every keystroke. Proxy mode can be `system`, `none`, or `custom`.

Failures return a diagnostic result and scoped log entry. Logs must not include secrets or full sensitive translation text.

### Actions and Settings

- Copy translation.
- Copy source and translation.
- Swap explicit source and target languages and reopen the query.

Settings include provider, source and target languages, secondary target, query delay, service URLs, Baidu credentials, proxy mode, and proxy URL.

## File Search

### Dependency

File Search uses the Everything SDK and an existing Everything index. Weed does not recursively index the filesystem.

The plugin declares Everything as an external dependency. On startup, the Host checks IPC readiness and may start an installed Everything executable. Weed does not install Everything or change its startup configuration.

Both `Everything64.dll` and `Everything32.dll` are distributed with the plugin, and the runtime selects the correct architecture.

### Query

```text
file report
file *.pdf invoice
file path:projects weed
```

The query is forwarded to Everything. When `includeFolders` is false, a file-only filter is added. Results preserve full paths and map Everything order into descending Weed match scores.

### Actions and Settings

- Open file or folder.
- Open containing location.
- Copy full path.

Settings include `includeFolders`, `maxResults`, and Everything SDK sort order. Missing installation, unavailable IPC, invalid syntax, and SDK errors return diagnostic results.

## Run Command

Run Command is an implicit provider that opens only a fixed allowlist of Windows tools. It never passes arbitrary user input to a command shell.

Current aliases include:

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

Matching uses exact, prefix, and substring checks against aliases and display names, returning at most eight results. The only action opens the selected allowlisted target through Shell execution.

## Cross-Plugin Requirements

- Plugin IDs, result IDs, and command IDs remain stable across compatible releases.
- Query results are bounded and cancellation-aware.
- User-visible failures return useful diagnostics and write scoped logs.
- Settings definitions include clear labels, defaults, valid ranges, and choices.
- Plugins use Host storage, clipboard, Shell, screen capture, and logging services rather than Host implementation classes.
- Sensitive content and credentials are not written to logs.
- Every user-visible behavior change updates the built-in plugin guide and changelog.
