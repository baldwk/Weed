# Changelog

## Pre-release

- Added the Toolbox external plugin with configurable implicit commands for UUID generation, timestamp conversion, Base64, URL encoding, hashes, and JSON formatting.
- Restyled the external plugin list and added confirmed uninstall with restart-safe cleanup for loaded packages.

## 0.1.6

- Converted the complete user, plugin, template, and developer documentation set to English.

## 0.1.5

- Reworked the main README around product capabilities, installation, everyday usage, screenshots, and user-facing plugin discovery.
- Added a dedicated user guide covering settings, updates, local data, privacy boundaries, external plugins, and troubleshooting.
- Moved architecture, implementation, plugin SDK, storage, roadmap, and release guidance into `docs/dev` with a new developer index and corrected .NET 9 requirements.
- Reorganized the built-in plugin, OCR plugin, and plugin template documentation, including current calculator logarithm support and external dependency guidance.

## 0.1.4

- Prevented multiple Weed processes from running and made repeated launches activate the existing launcher instead.
- Fixed the appearance setting so system, dark, and light themes switch immediately without visual-tree or frozen-resource errors, follow Windows changes, and use one consistent palette across launcher and settings surfaces.
- Added verified per-user launch-at-login with silent tray startup, plus plugin external-dependency declarations and automatic Everything startup for File Search.
- Added AppLauncher indexing for classic Shell AppsFolder entries such as Paint, app package logo resolution, and AppUserModelId search fallback.
- Adjusted launcher result layout so short result lists do not show unnecessary vertical scrolling, and reduced usage-history weight so exact matches stay ahead of looser frequent matches.
- Added calculator support for `ln`, `log`, and `logN` functions.
- Added configurable Everything SDK sort order to File Search.
- Added repository agent instructions requiring changelog entries for future updates.
