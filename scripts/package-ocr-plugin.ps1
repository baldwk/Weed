param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = "",
    [switch]$FetchModels
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $repoRoot "External Plugins\Weed.Plugins.Ocr\Weed.Plugins.Ocr.csproj"

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts\plugins"
}

$publishDir = Join-Path $OutputRoot "weed.ocr"
$zipPath = Join-Path $OutputRoot "weed.ocr.zip"

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
if (Test-Path $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

dotnet publish $project -c $Configuration -r $Runtime --self-contained false -o $publishDir

if ($FetchModels) {
    & (Join-Path $PSScriptRoot "fetch-ocr-models.ps1") -Destination (Join-Path $publishDir "models")
}

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
$metadata = [ordered]@{
    id = $manifest.id
    name = $manifest.name
    version = $manifest.version
    sdkVersion = $manifest.sdkVersion
    minWeedVersion = "0.1.0"
    packageUrl = Split-Path -Leaf $versionedZipPath
    sha256 = $hash
    repositoryUrl = "https://github.com/wky/weed-plugin-ocr"
    releaseNotesUrl = "https://github.com/wky/weed-plugin-ocr/releases/tag/v$($manifest.version)"
}
$metadataPath = Join-Path $OutputRoot "$($manifest.id)-$($manifest.version).plugin-release.json"
$metadata | ConvertTo-Json -Depth 6 | Set-Content -Path $metadataPath -Encoding UTF8

Write-Host "Plugin folder: $publishDir"
Write-Host "Plugin package: $zipPath"
Write-Host "Versioned package: $versionedZipPath"
Write-Host "Release metadata: $metadataPath"
