# Weed Plugin Template

Use this template to create an external managed .NET plugin. Read the [Plugin System](../../docs/dev/02-plugin-system.md) and [External Plugin Development](../../docs/dev/08-external-plugins.md) guides first.

## Create a Project

1. Copy this directory into a standalone plugin repository.
2. Rename the project, namespace, and entry class.
3. Update `id`, `name`, `version`, `assembly`, and `entryType` in `manifest.json`.
4. Update the `Weed.Abstractions` reference path in `Example.Plugin.csproj`.
5. Implement `IWeedPlugin` and any query, command, settings, or resident lifecycle interfaces you need.

Keep the plugin ID stable and use a clear namespace such as `com.example.weather`. Update both the project version and manifest version for each release.

## Publish

Do not distribute only the DLL produced by `dotnet build`. Publish the complete plugin directory:

```powershell
dotnet publish .\Example.Plugin.csproj `
  -c Release `
  -r win-x64 `
  --self-contained false `
  -o .\dist\example.plugin
```

The publish directory root must contain:

- `manifest.json`
- The plugin DLL
- The `.deps.json` file
- All dependency DLLs, native libraries, and runtime assets

Zip the contents of the publish directory so `manifest.json` is at the ZIP root. Do not wrap the files in another parent directory.

## Validate Locally

1. Import the ZIP, published folder, or source folder under **Settings > External Plugins**.
2. Restart Weed.
3. Confirm the plugin is enabled and test queries, default actions, secondary actions, and settings.
4. Inspect the manifest, permission declarations, and log in the plugin details page.
5. Extract the final ZIP into a clean directory and perform one final import test.

External plugins run inside the Weed process. Manifest permissions describe capabilities to users but do not create a security sandbox. Declare only the permissions you need and document all network and data-handling behavior in the plugin README.

## Distribute

Publish versioned ZIP packages from the plugin's own GitHub repository. Include release notes and, preferably, a SHA256 checksum. See [External Plugin Development](../../docs/dev/08-external-plugins.md) for repository layout, packaging, import rules, and troubleshooting.
