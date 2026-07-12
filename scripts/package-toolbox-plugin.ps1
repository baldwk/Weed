param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = ""
)

$arguments = @(
    "-ExecutionPolicy", "Bypass",
    "-File", (Join-Path $PSScriptRoot "package-plugin.ps1"),
    "-PluginId", "weed.toolbox",
    "-Configuration", $Configuration,
    "-Runtime", $Runtime
)
if (-not [string]::IsNullOrWhiteSpace($OutputRoot)) {
    $arguments += @("-OutputRoot", $OutputRoot)
}

& powershell @arguments
exit $LASTEXITCODE
