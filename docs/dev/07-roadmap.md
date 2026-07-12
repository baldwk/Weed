# Roadmap and Release Acceptance

> [Back to Developer Documentation](README.md)

This page tracks maintenance directions and the acceptance requirements for future releases. Completed implementation phases belong in [`CHANGELOG.md`](../../CHANGELOG.md) and Git history rather than an open task list.

## Near-Term Directions

### Distribution

- Provide clearer installation, upgrade, and uninstall workflows.
- Evaluate self-contained packages, an installer, and code signing.
- Complete in-app package replacement with failure recovery and explicit status.

### Plugin Ecosystem

- Stabilize compatibility rules for `Weed.Abstractions`.
- Publish a standalone SDK package and example plugin repository.
- Improve plugin source, version, checksum, and upgrade information.
- Evaluate process isolation and stronger capability enforcement for untrusted plugins.

### Search and Interaction

- Continue tuning cross-plugin ranking, usage history weight, and exact-match priority.
- Expand accessibility, keyboard, multi-monitor, and DPI testing.
- Standardize loading, cancellation, error, and retry feedback for expensive queries.

### First-Party Features

- Improve application discovery and App Launcher index refresh.
- Improve Clipboard object retention, search quality, and privacy controls.
- Improve scrolling capture across applications and display scales.
- Improve dependency diagnostics for Translator, File Search, and OCR.

## Release Acceptance

### Build and Test

- `dotnet build Weed.sln --configuration Release` succeeds.
- `dotnet run --configuration Release --project Weed.SmokeTests\Weed.SmokeTests.csproj` passes.
- The `win-x64` package launches after extraction into a clean directory.
- Update manifest version, package URL, and SHA256 match the release assets.

### Core Workflows

- A repeated launch activates the existing Weed process.
- `Alt+Space`, tray access, theme switching, and launch-at-startup settings work.
- App search, calculator, clipboard, screenshots, translation, file search, emoji, and system commands each pass one primary workflow test.
- External plugin import, restart loading, disablement, and log diagnostics work.
- Missing Everything, offline translation, missing OCR models, and similar dependency failures show actionable diagnostics.

### Data and Compatibility

- Upgrading over the previous stable release preserves settings, hotkeys, plugin state, and history.
- Cleanup policies do not remove pinned clipboard entries or user-selected files accidentally.
- Logs do not contain unnecessary clipboard content, translation text, secrets, or other sensitive data.

### Documentation and Release

- The root README and User Guide contain only user-visible features, installation, and usage.
- Implementation, build, plugin development, and release guidance stays under `docs/dev/`.
- Built-in plugin, OCR plugin, and template documentation matches current behavior.
- `CHANGELOG.md`, project version, Git tag, and Release title agree.
- GitHub Release contains `Weed-win-x64.zip` and `Weed-win-x64.update.json` and is marked as the latest stable version.

## Definition of Done

A feature is releasable only when implementation, error handling, necessary tests, user documentation, developer documentation, and changelog entries are complete. Changes to persisted data or plugin contracts also require compatibility or migration notes.
