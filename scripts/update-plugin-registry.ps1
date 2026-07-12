param(
    [Parameter(Mandatory = $true)]
    [string]$MetadataPath,
    [string]$RegistryPath = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
if ([string]::IsNullOrWhiteSpace($RegistryPath)) {
    $RegistryPath = Join-Path $repoRoot "plugins.registry.json"
}

$metadata = Get-Content -Raw -LiteralPath $MetadataPath | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($metadata.id) -or
    [string]::IsNullOrWhiteSpace($metadata.version) -or
    [string]::IsNullOrWhiteSpace($metadata.packageUrl) -or
    $metadata.sha256 -notmatch '^[a-fA-F0-9]{64}$') {
    throw "Plugin release metadata is incomplete or invalid: $MetadataPath"
}

$registry = Get-Content -Raw -LiteralPath $RegistryPath | ConvertFrom-Json
$entry = [ordered]@{
    id = $metadata.id
    name = $metadata.name
    description = $metadata.description
    version = $metadata.version
    sdkVersion = $metadata.sdkVersion
    minWeedVersion = $metadata.minWeedVersion
    packageUrl = $metadata.packageUrl
    sha256 = $metadata.sha256.ToLowerInvariant()
    repositoryUrl = $metadata.repositoryUrl
    releaseNotesUrl = $metadata.releaseNotesUrl
    trusted = [bool]$metadata.trusted
    tags = @($metadata.tags)
}

$plugins = @($registry.plugins | Where-Object { $_.id -ne $metadata.id })
$plugins += [pscustomobject]$entry
$plugins = @($plugins | Sort-Object @{ Expression = { $_.name }; Ascending = $true }, @{ Expression = { $_.id }; Ascending = $true })

$updated = [ordered]@{
    '$schema' = if ([string]::IsNullOrWhiteSpace($registry.'$schema')) { "schemas/plugin-registry.schema.json" } else { $registry.'$schema' }
    schemaVersion = if ([string]::IsNullOrWhiteSpace($registry.schemaVersion)) { "1" } else { $registry.schemaVersion }
    plugins = $plugins
}

$json = $updated | ConvertTo-Json -Depth 10
[System.IO.File]::WriteAllText(
    $RegistryPath,
    $json + [Environment]::NewLine,
    [System.Text.UTF8Encoding]::new($false))

Write-Host "Updated plugin registry: $RegistryPath"
Write-Host "$($metadata.id) -> $($metadata.version)"
