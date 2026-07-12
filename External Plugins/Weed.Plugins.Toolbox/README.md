# Weed Toolbox External Plugin

Toolbox provides small, local text and developer utilities directly in the Weed launcher. It does not use a common prefix: enter the configured tool name itself.

## Usage

| Input | Purpose |
| --- | --- |
| `uuid` | Generate one UUID v4 |
| `timestamp` | Show the current millisecond and second Unix timestamps |
| `timestamp 1783843200000` | Convert a Unix timestamp to local and UTC time |
| `timestamp 2026-07-12 16:00:00` | Convert a local date to millisecond and second timestamps |
| `base64` | Choose Encode or Decode |
| `base64 encode hello` | Encode UTF-8 text as Base64 |
| `base64 decode aGVsbG8=` | Decode Base64 as UTF-8 text |
| `url` | Choose URL Encode or Decode |
| `hash` | Choose SHA-256, SHA-512, SHA-1, or MD5 |
| `json` | Choose Format or Minify |

Press `Enter` on a value to copy it. Result actions can also paste the value or copy the original input. Base64 is an encoding, not encryption. SHA-1 and MD5 are included only for compatibility and are not suitable for security-sensitive uses.

## Settings

Each tool name is configurable. For example, change **Timestamp command** from `timestamp` to `ts` to use `ts`; the old name stops matching immediately. Every configured name must be one unique word. Duplicate names produce a configuration error result instead of choosing a tool silently.

Toolbox is an implicit query provider. Exact tool names use the maximum plugin match score, but Weed still applies normal usage history and plugin priority ranking. If another plugin competes for the same input, change the tool name or raise Toolbox priority in Settings.

## Privacy

All transformations run locally. Toolbox does not read files, access the network, persist query content, or log input values. External plugins run inside the Weed process, so install packages only from sources you trust.

## Build and Package

Build the project:

```powershell
dotnet build .\Weed.Plugins.Toolbox.csproj -c Release
```

Create an importable package from the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\package-toolbox-plugin.ps1
```

Import the resulting ZIP under **Settings > External Plugins**, then restart Weed.
