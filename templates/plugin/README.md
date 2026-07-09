# Weed Plugin Template

1. Copy this folder next to a published Weed app folder, or update the `Weed.Abstractions` hint path in `Example.Plugin.csproj`.
2. Rename the project, namespace, manifest `id`, `assembly`, and `entryType`.
3. Implement `IWeedPlugin` and any entry interfaces you need.
4. Publish the plugin:

```powershell
dotnet publish .\Example.Plugin.csproj -c Release -r win-x64 --self-contained false -o .\dist\example.plugin
```

5. Put `manifest.json` in the published folder root.
6. Zip the contents of the published folder, not the parent folder.
7. Publish the ZIP to the plugin repository's GitHub Releases.
8. Add a registry entry with the ZIP URL and SHA256, or import the ZIP from Weed Settings > External Plugins.
9. Restart Weed.

The host scans plugin manifests on startup and loads each managed DLL in its own `AssemblyLoadContext`.
See `docs\08-external-plugins.md` for the full independent repository and registry workflow.
