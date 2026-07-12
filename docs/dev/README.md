# Weed Developer Documentation

This directory contains architecture, implementation, plugin SDK, build, test, and release documentation. User-facing installation and feature guidance lives in the [root README](../../README.md) and [User Guide](../user-guide.md).

## Development Environment

- Windows 10 or later
- .NET 9 SDK
- PowerShell
- Optional: GitHub CLI for the release script

Run from the repository root:

```powershell
dotnet build Weed.sln
dotnet run --project Weed.SmokeTests\Weed.SmokeTests.csproj
dotnet run --project Weed.App\Weed.App.csproj
```

## Documents

- [Product Boundaries and Terminology](00-overview.md)
- [System Architecture](01-system-architecture.md)
- [Plugin System](02-plugin-system.md)
- [Query Routing and Hotkeys](03-query-routing-hotkeys.md)
- [UI and Interaction](04-ui-ux.md)
- [First-Party Plugin Specification](05-first-party-plugins.md)
- [Data and Storage](06-data-storage.md)
- [Roadmap and Release Acceptance](07-roadmap.md)
- [External Plugin Development](08-external-plugins.md)

## Plugin Development

1. Read [Plugin System](02-plugin-system.md) for manifests, lifecycle, and Host APIs.
2. Start from [`templates/plugin`](../../templates/plugin/README.md).
3. Follow [External Plugin Development](08-external-plugins.md) to package and import a test build.
4. Validate manifests with [`schemas/manifest.schema.json`](../../schemas/manifest.schema.json).

External plugins share the Weed process. Permission fields communicate required capabilities but do not form a security sandbox.

## Release

The root release script handles the normal release flow:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\release-github.ps1 `
  -Version 0.1.5 `
  -CommitMessage "Release v0.1.5"
```

It builds the solution, runs SmokeTests, commits all changes, pushes the current branch, creates and pushes a version tag, builds the `win-x64` package and update manifest, and publishes a GitHub Release. Update `CHANGELOG.md` first and confirm every working-tree change belongs in the release.

To create only a local package:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\publish-release.ps1
```

Output is written to `artifacts/`. The current package requires the .NET 9 Desktop Runtime x64 on the target machine.

## Documentation Maintenance

- Update the root README, User Guide, and relevant plugin README when user-visible features, entry points, or settings change.
- Document implementation, data structures, interfaces, builds, and releases only in this directory or template documentation.
- Every change needs a `CHANGELOG.md` entry. Put unreleased changes under `Pre-release`, then move them into the matching version section at release time.
- Use `0.1.0` only as an illustrative version. References to the current release must match the project version.
