# Weed OCR External Plugin

This plugin uses RapidOCRLib with PP-OCRv5 mobile Chinese models. It is intentionally packaged as an external plugin,
not referenced by `Weed.App`.

## Build

```powershell
dotnet build "External Plugins\Weed.Plugins.Ocr\Weed.Plugins.Ocr.csproj"
```

## Download models

```powershell
powershell -ExecutionPolicy Bypass -File scripts\fetch-ocr-models.ps1
```

The four model files are placed under `External Plugins\Weed.Plugins.Ocr\models`. The model set is about 21 MiB.

## Package

```powershell
powershell -ExecutionPolicy Bypass -File scripts\package-ocr-plugin.ps1 -FetchModels
```

The script writes both `artifacts\plugins\weed.ocr.zip` and the registry-friendly
`artifacts\plugins\weed.ocr-0.1.0-win-x64.zip`, plus `weed.ocr-0.1.0.plugin-release.json`.
Import the ZIP from Weed Settings > External Plugins, or extract it to
`%LOCALAPPDATA%\Weed\plugins\weed.ocr` and restart Weed.

`-FetchModels` downloads the models into the packaged plugin folder, not into source control.

The default OCR result action copies recognized text to the clipboard. Opening a text file remains available as a
secondary action.
