# Changelog

## 0.1.4

- Prevented multiple Weed processes from running and made repeated launches activate the existing launcher instead.
- Fixed the appearance setting so system, dark, and light themes switch immediately without visual-tree or frozen-resource errors, follow Windows changes, and use one consistent palette across launcher and settings surfaces.
- Added verified per-user launch-at-login with silent tray startup, plus plugin external-dependency declarations and automatic Everything startup for File Search.
- Added AppLauncher indexing for classic Shell AppsFolder entries such as Paint, app package logo resolution, and AppUserModelId search fallback.
- Adjusted launcher result layout so short result lists do not show unnecessary vertical scrolling, and reduced usage-history weight so exact matches stay ahead of looser frequent matches.
- Added calculator support for `ln`, `log`, and `logN` functions.
- Added configurable Everything SDK sort order to File Search.
- Added repository agent instructions requiring changelog entries for future updates.
