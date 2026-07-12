# External Plugin Development

> [Back to Developer Documentation](README.md)

## Runtime Model

An external plugin is a managed .NET DLL with a public type that implements `IWeedPlugin`. At startup, Weed scans `manifest.json` files under `%LOCALAPPDATA%\Weed\plugins`, validates the assembly and entry type, and creates a separate `AssemblyLoadContext` for each external plugin.

The Host supplies `Weed.Abstractions` from its default context. Do not package another copy. Put all other managed dependencies, native libraries, and runtime assets in the plugin publish directory.

External plugins still run in the Host process. A load context creates dependency and unload boundaries, not a security sandbox. A plugin can crash, block, or access resources available to the Weed process.

## Manifest

Every package must contain `manifest.json` at its root:

```json
{
  "id": "com.example.weather",
  "name": "Weather",
  "version": "0.1.0",
  "sdkVersion": "0.1",
  "assembly": "Example.Weather.dll",
  "entryType": "Example.Weather.WeatherPlugin",
  "runtime": {
    "resident": false
  },
  "activations": [
    {
      "type": "keyword",
      "keyword": "weather",
      "command": "weather.search"
    }
  ],
  "permissions": [
    "network",
    "clipboard.write"
  ]
}
```

Important fields:

- `id`: Stable, globally unique plugin identity and settings namespace.
- `version`: Plugin version, independent of the Weed version.
- `sdkVersion`: Target plugin SDK version; currently `0.1`.
- `assembly`: Plugin DLL path relative to the package root. It cannot escape the plugin directory.
- `entryType`: Fully qualified public type that implements `IWeedPlugin`.
- `runtime.resident`: Whether the plugin has an active resident lifecycle.
- `activations`: Keyword, Hotkey, or ImplicitQuery entry points.
- `permissions`: Capabilities shown to users for review. They do not enforce a sandbox.
- `externalDependencies`: Optional external programs. The Host can check process or built-in probes and attempt to start an installed dependency.

Validate the file with [`schemas/manifest.schema.json`](../../schemas/manifest.schema.json). See [Plugin System](02-plugin-system.md) for full fields and lifecycle contracts.

## Create a Project

Start from [`templates/plugin`](../../templates/plugin/README.md), or create a class library that references `Weed.Abstractions`. Inside this repository, use a project reference:

```xml
<ProjectReference Include="..\..\Weed.Abstractions\Weed.Abstractions.csproj">
  <Private>false</Private>
</ProjectReference>
```

In an independent repository, reference `Weed.Abstractions.dll` from a Weed distribution:

```xml
<Reference Include="Weed.Abstractions">
  <HintPath>..\Weed\Weed.Abstractions.dll</HintPath>
  <Private>false</Private>
</Reference>
```

The current Host targets .NET 9. Use `net9.0` for platform-neutral plugins or `net9.0-windows` when Windows APIs or WPF types are required.

## Publish a Plugin

Use `dotnet publish`; do not distribute only the main DLL from `dotnet build`:

```powershell
dotnet publish .\Example.Plugin.csproj `
  -c Release `
  -r win-x64 `
  --self-contained false `
  -o .\dist\example.plugin
```

Expected layout:

```text
example.plugin\
  manifest.json
  Example.Plugin.dll
  Example.Plugin.deps.json
  Dependency.dll
  runtimes\
  assets\
```

Zip the directory contents so `manifest.json` is at the archive root:

```powershell
Compress-Archive `
  -Path .\dist\example.plugin\* `
  -DestinationPath .\com.example.weather-0.1.0-win-x64.zip
```

## Import Behavior

**Settings > External Plugins** accepts:

- A ZIP or directory with `manifest.json` at its root.
- A ZIP or directory with one child directory containing `manifest.json`.
- A source directory with `manifest.json` and one plugin `.csproj`; Weed runs a Release, current Windows RID, non-self-contained publish.
- A DLL with a matching neighboring manifest.
- A DLL containing a public `IWeedPlugin` type. Weed uses a static `Manifest` member when present or generates a minimal manifest otherwise.

The importer:

1. Reads and validates the manifest.
2. Validates `id`, `assembly`, `entryType`, and package path boundaries.
3. Copies files to `%LOCALAPPDATA%\Weed\plugins\<manifest.id>`.
4. Replaces an existing directory with the same ID only after user confirmation.
5. Leaves the new plugin unloaded until the next Weed restart.

Installed packages can be removed from **Settings > External Plugins**. Weed validates that the selected manifest and directory belong to the external plugin root, removes the package atomically when possible, and uses a pending-removal marker when loaded files cannot be moved immediately. A restart unloads the current plugin instance and completes pending cleanup. Plugin settings and data are intentionally retained.

If a source directory contains multiple `.csproj` files, select the plugin project directory directly or make the plugin project name correspond to the manifest assembly.

Manual installation is also supported:

```powershell
$target = "$env:LOCALAPPDATA\Weed\plugins\com.example.weather"
New-Item -ItemType Directory -Force -Path $target
Copy-Item .\dist\example.plugin\* $target -Recurse -Force
```

Restart Weed after installation or replacement.

## Independent Distribution

Third-party plugins should maintain versions, documentation, and Releases in their own repositories. Recommended layout:

```text
weed-plugin-example\
  src\
  manifest.json
  README.md
  CHANGELOG.md
  .github\workflows\release.yml
```

Each release should include:

- `<plugin-id>-<version>-win-x64.zip`
- Installation, entry point, settings, network, and data-handling documentation
- Release notes
- Preferably, a SHA256 checksum

Generate a checksum with:

```powershell
(Get-FileHash -Algorithm SHA256 `
  .\com.example.weather-0.1.0-win-x64.zip).Hash.ToLowerInvariant()
```

Never ship developer-machine paths, secrets, caches, temporary model downloads, or unnecessary SDK assemblies.

## OCR External Plugin

`External Plugins\Weed.Plugins.Ocr` is the repository's external plugin example. `Weed.App` does not reference it. The plugin uses RapidOCRLib and PP-OCRv5 Chinese models.

Build the source:

```powershell
dotnet build "External Plugins\Weed.Plugins.Ocr\Weed.Plugins.Ocr.csproj"
```

Create an importable package with models and runtime dependencies:

```powershell
powershell -ExecutionPolicy Bypass `
  -File scripts\package-ocr-plugin.ps1 `
  -FetchModels
```

Output:

```text
artifacts\plugins\weed.ocr\
artifacts\plugins\weed.ocr.zip
artifacts\plugins\weed.ocr-0.1.0-win-x64.zip
artifacts\plugins\weed.ocr-0.1.0.plugin-release.json
```

The model set is about 21 MiB. A ZIP with RapidOCR, ONNX, OpenCV runtimes, and models is about 60 MiB, depending on dependency versions. `-FetchModels` downloads models into the package staging directory, not source control.

After import and restart:

- `ocr`: Show capture and image recognition entries.
- `ocr "C:\path\image.png"`: Recognize a local image.
- `Shift+Alt+O`: Select a screen region and recognize it.

The default result action copies recognized text. Saving and opening a text file is a secondary action. See the [OCR Plugin Guide](../../External%20Plugins/Weed.Plugins.Ocr/README.md) for user documentation.

## Toolbox External Plugin

`External Plugins\Weed.Plugins.Toolbox` is a local utility plugin and another external plugin example. It uses one implicit-query provider, recognizes configurable exact tool names, and returns no results for unrelated input.

Create an importable package:

```powershell
powershell -ExecutionPolicy Bypass `
  -File scripts\package-toolbox-plugin.ps1
```

The package supports UUID generation, timestamp conversion, Base64 and URL encoding, hashes, and JSON formatting without network or file access. See the [Toolbox Plugin Guide](../../External%20Plugins/Weed.Plugins.Toolbox/README.md) for commands and settings.

## Release Checklist

- Extract the ZIP to a clean directory and verify its manifest, DLL, `.deps.json`, and dependencies.
- Validate the manifest schema and confirm its version matches the filename.
- Test first import, replacement, restart loading, disablement, settings, and removal.
- Test missing dependencies, network failures, invalid input, and cancellation.
- Ensure logs do not contain secrets, clipboard content, translation text, or other unnecessary sensitive data.
- Document file, network, clipboard, and screen access.

## Troubleshooting

- **Plugin does not appear:** Restart Weed and inspect enabled state and logs.
- **Dependencies fail to load:** Distribute `dotnet publish` output rather than only `dotnet build` output.
- **ZIP imports but does not load:** Make sure `manifest.json` is at the root and `assembly` points to a real DLL inside the package.
- **Source import fails:** Install the .NET 9 SDK and ensure the plugin project can be identified unambiguously.
- **Native library fails to load:** Check `runtimes\win-x64\native` content and process architecture.
- **Replacement still behaves like the old version:** Enable replacement during import and restart Weed.
