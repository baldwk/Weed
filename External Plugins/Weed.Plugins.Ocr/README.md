# Weed OCR External Plugin

The OCR plugin recognizes text from a selected screen region or a local image. It uses PP-OCRv5 Chinese models and supports Chinese, English, digits, and common mixed-language content.

The plugin is not installed with the main Weed package. Obtain a complete plugin ZIP containing its runtime dependencies and four model files, import it under **Settings > External Plugins**, then restart Weed. Maintainers and plugin developers can build the package using the [external plugin development guide](../../docs/dev/08-external-plugins.md#ocr-external-plugin).

## Usage

| Input or Hotkey | Purpose |
| --- | --- |
| `ocr` | Show screen-capture and image recognition actions |
| `ocr "C:\path\image.png"` | Recognize a local image |
| `Shift+Alt+O` | Select a screen region and recognize its text |

PNG, JPEG, BMP, TIFF, and WebP images are supported. Wrap paths that contain spaces in double quotes.

The default result action copies recognized text to the clipboard. Other actions can open the source image, open its folder, or save and open the recognition result as a text file.

## Settings

- **Model directory:** Leave empty to use the bundled `models` directory.
- **Max side length:** Higher values can improve small-text recognition at the cost of speed and memory.
- **Padding:** Adds image padding before text detection.
- **Angle detection:** Detects and corrects rotated text; enabled by default.

The defaults should work for most images. If models are reported missing, reimport a complete package containing the `models` directory or select a valid model directory in Settings.

## Privacy

Recognition runs locally and does not upload images to an online OCR service. Region captures and generated text may be stored under `%LOCALAPPDATA%\Weed\plugins-data\weed.ocr`; remove them manually when working with sensitive content.

External plugins run inside the Weed process. Import packages only from sources you trust.

## Troubleshooting

- **OCR does not appear after import:** Restart Weed and confirm the plugin is enabled under **Settings > Plugins**.
- **Model files are missing:** Verify that the plugin or configured model directory contains the complete PP-OCRv5 model set.
- **The capture hotkey does not work:** Check whether another app uses `Shift+Alt+O`, then change it in Weed's hotkey settings.
- **Small text is incomplete:** Increase Max side length and prefer a sharp source image with minimal scaling.
- **Loading or recognition fails:** Inspect the plugin log and confirm you imported the complete published package rather than a single DLL.
