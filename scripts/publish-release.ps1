param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Output = "",
    [string]$Version = "",
    [string]$PackageUrl = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path $root "artifacts\Weed-$Runtime"
}

function Get-ProjectVersion {
    param([string]$ProjectPath)

    [xml]$project = Get-Content -Raw -Path $ProjectPath
    foreach ($group in @($project.Project.PropertyGroup)) {
        if (-not [string]::IsNullOrWhiteSpace($group.Version)) {
            return [string]$group.Version
        }
    }

    return "0.1.0"
}

function Copy-DirectoryFresh {
    param(
        [string]$Source,
        [string]$Destination
    )

    if (Test-Path $Destination) {
        Remove-Item -LiteralPath $Destination -Recurse -Force
    }

    Copy-Item $Source $Destination -Recurse -Force
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-ProjectVersion (Join-Path $root "Weed.App\Weed.App.csproj")
}

New-Item -ItemType Directory -Force -Path $Output | Out-Null
dotnet publish (Join-Path $root "Weed.App\Weed.App.csproj") `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained false `
    -p:PublishSingleFile=false `
    -o $Output

Copy-Item (Join-Path $root "README.md") $Output -Force
Copy-Item (Join-Path $root "plugins.registry.json") $Output -Force
Copy-DirectoryFresh (Join-Path $root "docs") (Join-Path $Output "docs")
Copy-DirectoryFresh (Join-Path $root "schemas") (Join-Path $Output "schemas")
Copy-DirectoryFresh (Join-Path $root "templates") (Join-Path $Output "templates")

$folderManifestPath = Join-Path $Output "update-manifest.json"
if (Test-Path $folderManifestPath) {
    Remove-Item -LiteralPath $folderManifestPath -Force
}

$zipPath = "$Output.zip"
if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive -Path (Join-Path $Output "*") -DestinationPath $zipPath -Force
$hash = (Get-FileHash -Algorithm SHA256 -Path $zipPath).Hash.ToLowerInvariant()
if ([string]::IsNullOrWhiteSpace($PackageUrl)) {
    $PackageUrl = Split-Path -Leaf $zipPath
}

$manifest = [ordered]@{
    version = $Version
    publishedAt = (Get-Date).ToUniversalTime().ToString("o")
    packageUrl = $PackageUrl
    sha256 = $hash
    notes = "Weed MVP $Version for $Runtime"
}
$manifestJson = $manifest | ConvertTo-Json -Depth 4
$manifestPath = [System.IO.Path]::ChangeExtension($zipPath, ".update.json")
Set-Content -Path $manifestPath -Value $manifestJson -Encoding UTF8
Set-Content -Path $folderManifestPath -Value $manifestJson -Encoding UTF8

Write-Host "Published Weed to $Output"
Write-Host "Package: $zipPath"
Write-Host "Update manifest: $manifestPath"
