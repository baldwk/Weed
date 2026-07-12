param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = "",
    [switch]$FetchModels
)

# Complete OCR release packages always include the pinned model set. FetchModels is retained for CLI compatibility.
$arguments = @(
    "-ExecutionPolicy", "Bypass",
    "-File", (Join-Path $PSScriptRoot "package-plugin.ps1"),
    "-PluginId", "weed.ocr",
    "-Configuration", $Configuration,
    "-Runtime", $Runtime
)
if (-not [string]::IsNullOrWhiteSpace($OutputRoot)) {
    $arguments += @("-OutputRoot", $OutputRoot)
}

& powershell @arguments
exit $LASTEXITCODE
