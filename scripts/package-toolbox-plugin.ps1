param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $repoRoot "External Plugins\Weed.Plugins.Toolbox\Weed.Plugins.Toolbox.csproj"

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts\plugins"
}

$publishDir = Join-Path $OutputRoot "weed.toolbox"
$zipPath = Join-Path $OutputRoot "weed.toolbox.zip"

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
if (Test-Path $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

dotnet publish $project -c $Configuration -r $Runtime --self-contained false -o $publishDir

if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath

$manifest = Get-Content -Raw -Path (Join-Path $publishDir "manifest.json") | ConvertFrom-Json
$versionedZipPath = Join-Path $OutputRoot "$($manifest.id)-$($manifest.version)-$Runtime.zip"
if (Test-Path $versionedZipPath) {
    Remove-Item -LiteralPath $versionedZipPath -Force
}

Copy-Item $zipPath $versionedZipPath -Force
$hash = (Get-FileHash -Algorithm SHA256 -Path $versionedZipPath).Hash.ToLowerInvariant()

Write-Host "Plugin folder: $publishDir"
Write-Host "Plugin package: $zipPath"
Write-Host "Versioned package: $versionedZipPath"
Write-Host "SHA256: $hash"
