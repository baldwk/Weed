# Weed Plugin Template

1. Copy this folder next to a published Weed app folder, or update the `Weed.Abstractions` hint path in `Example.Plugin.csproj`.
2. Rename the project, namespace, manifest `id`, `assembly`, and `entryType`.
3. Implement `IWeedPlugin` and any entry interfaces you need.
4. Build the DLL.
5. Copy the DLL and `manifest.json` into `%LOCALAPPDATA%\Weed\plugins\<plugin-id>\`.

The host scans plugin manifests on startup and loads each managed DLL in its own `AssemblyLoadContext`.
