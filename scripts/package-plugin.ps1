param(
    [Parameter(Mandatory = $true)]
    [string]$PluginId,
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = ""
)

$ErrorActionPreference = "Stop"

function Require-Command {
    param([string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command is not available on PATH: $Name"
    }
}

function Write-Utf8Json {
    param([string]$Path, [object]$Value)

    $json = $Value | ConvertTo-Json -Depth 10
    [System.IO.File]::WriteAllText($Path, $json + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$releaseConfigPath = Join-Path $PSScriptRoot "plugin-release.config.json"
$releaseConfig = Get-Content -Raw -LiteralPath $releaseConfigPath | ConvertFrom-Json
$descriptor = $releaseConfig.plugins | Where-Object { $_.id -eq $PluginId } | Select-Object -First 1
if ($null -eq $descriptor) {
    throw "Unknown plugin id: $PluginId"
}

Require-Command "dotnet"

$project = Join-Path $repoRoot ($descriptor.project -replace '/', [System.IO.Path]::DirectorySeparatorChar)
if (-not (Test-Path -LiteralPath $project)) {
    throw "Plugin project was not found: $project"
}

$projectDirectory = Split-Path -Parent $project
$sourceManifestPath = Join-Path $projectDirectory "manifest.json"
$manifest = Get-Content -Raw -LiteralPath $sourceManifestPath | ConvertFrom-Json
if ($manifest.id -ne $PluginId) {
    throw "Manifest id '$($manifest.id)' does not match release id '$PluginId'."
}

if ($manifest.version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
    throw "Plugin version must be semantic: $($manifest.version)"
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts\plugins"
}

$publishDir = Join-Path $OutputRoot $PluginId
$unversionedZipPath = Join-Path $OutputRoot "$PluginId.zip"
$packageName = "$PluginId-$($manifest.version)-$Runtime.zip"
$versionedZipPath = Join-Path $OutputRoot $packageName
$checksumPath = Join-Path $OutputRoot "$PluginId-$($manifest.version)-$Runtime.sha256"
$metadataPath = Join-Path $OutputRoot "$PluginId-$($manifest.version).plugin-release.json"

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

& dotnet publish $project -c $Configuration -r $Runtime --self-contained false -p:Version=$($manifest.version) -o $publishDir
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

if ($descriptor.fetchModels) {
    & (Join-Path $PSScriptRoot "fetch-ocr-models.ps1") -Destination (Join-Path $publishDir "models")
}

$publishedManifestPath = Join-Path $publishDir "manifest.json"
$publishedAssemblyPath = Join-Path $publishDir $manifest.assembly
if (-not (Test-Path -LiteralPath $publishedManifestPath)) {
    throw "Published plugin is missing manifest.json."
}

if (-not (Test-Path -LiteralPath $publishedAssemblyPath)) {
    throw "Published plugin is missing its entry assembly: $($manifest.assembly)"
}

if (Test-Path -LiteralPath (Join-Path $publishDir "Weed.Abstractions.dll")) {
    throw "Plugin packages must not include a private Weed.Abstractions.dll."
}

foreach ($requiredFile in $descriptor.requiredFiles) {
    $requiredPath = Join-Path $publishDir ($requiredFile -replace '/', [System.IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path -LiteralPath $requiredPath) -or (Get-Item -LiteralPath $requiredPath).Length -eq 0) {
        throw "Published plugin is missing required file: $requiredFile"
    }
}

foreach ($path in @($unversionedZipPath, $versionedZipPath, $checksumPath, $metadataPath)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
    }
}

Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $unversionedZipPath
Copy-Item -LiteralPath $unversionedZipPath -Destination $versionedZipPath

$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $versionedZipPath).Hash.ToLowerInvariant()
[System.IO.File]::WriteAllText(
    $checksumPath,
    "$hash  $packageName" + [Environment]::NewLine,
    [System.Text.UTF8Encoding]::new($false))

$tag = "$($descriptor.slug)-v$($manifest.version)"
$packageUrl = "https://github.com/$($releaseConfig.repository)/releases/download/$tag/$packageName"
$metadata = [ordered]@{
    id = $manifest.id
    name = $manifest.name
    description = $descriptor.description
    version = $manifest.version
    sdkVersion = $manifest.sdkVersion
    minWeedVersion = $descriptor.minWeedVersion
    packageUrl = $packageUrl
    sha256 = $hash
    repositoryUrl = "https://github.com/$($releaseConfig.repository)"
    releaseNotesUrl = "https://github.com/$($releaseConfig.repository)/releases/tag/$tag"
    trusted = [bool]$descriptor.trusted
    tags = @($descriptor.tags)
}
Write-Utf8Json -Path $metadataPath -Value $metadata

Write-Host "Plugin folder: $publishDir"
Write-Host "Plugin package: $versionedZipPath"
Write-Host "Checksum: $checksumPath"
Write-Host "Release metadata: $metadataPath"
Write-Host "SHA256: $hash"
