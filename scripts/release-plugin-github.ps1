param(
    [Parameter(Mandatory = $true)]
    [string]$PluginId,
    [string]$Version = "",
    [string]$Remote = "origin",
    [string]$Branch = "",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$ReleaseNotes = "",
    [switch]$SkipTests,
    [switch]$Draft,
    [switch]$Prerelease,
    [switch]$UpdateRegistry
)

$ErrorActionPreference = "Stop"

function Require-Command {
    param([string]$Name)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command is not available on PATH: $Name"
    }
}

function Invoke-Native {
    param([string]$Label, [string]$FilePath, [string[]]$Arguments)
    Write-Host ""
    Write-Host "==> $Label"
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Label failed with exit code $LASTEXITCODE."
    }
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Push-Location $repoRoot
try {
    foreach ($command in @("git", "dotnet", "gh", "powershell")) {
        Require-Command $command
    }

    $dirty = (& git status --porcelain)
    if ($LASTEXITCODE -ne 0 -or -not [string]::IsNullOrWhiteSpace(($dirty -join ""))) {
        throw "Plugin releases require a clean working tree. Commit and push source changes first."
    }

    if ([string]::IsNullOrWhiteSpace($Branch)) {
        $Branch = (& git branch --show-current).Trim()
    }

    if ([string]::IsNullOrWhiteSpace($Branch)) {
        throw "Plugin releases require a named Git branch."
    }

    $releaseConfig = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot "plugin-release.config.json") | ConvertFrom-Json
    $descriptor = $releaseConfig.plugins | Where-Object { $_.id -eq $PluginId } | Select-Object -First 1
    if ($null -eq $descriptor) {
        throw "Unknown plugin id: $PluginId"
    }

    $project = Join-Path $repoRoot ($descriptor.project -replace '/', [System.IO.Path]::DirectorySeparatorChar)
    $manifest = Get-Content -Raw -LiteralPath (Join-Path (Split-Path -Parent $project) "manifest.json") | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace($Version)) {
        $Version = $manifest.version
    }

    if ($Version -ne $manifest.version) {
        throw "Requested version $Version does not match manifest version $($manifest.version)."
    }

    if ($UpdateRegistry -and ($Draft -or $Prerelease)) {
        throw "Draft and prerelease packages cannot update the stable plugin registry."
    }

    $tag = "$($descriptor.slug)-v$Version"
    if (-not [string]::IsNullOrWhiteSpace((& git tag --list $tag))) {
        throw "Local tag already exists: $tag"
    }

    if (-not [string]::IsNullOrWhiteSpace((& git ls-remote --tags $Remote "refs/tags/$tag"))) {
        throw "Remote tag already exists: $tag"
    }

    & gh release view $tag --json tagName 1>$null 2>$null
    if ($LASTEXITCODE -eq 0) {
        throw "GitHub release already exists: $tag"
    }

    Invoke-Native "Build solution" "dotnet" @("build", "Weed.sln", "--configuration", $Configuration)
    if (-not $SkipTests) {
        Invoke-Native "Run smoke tests" "dotnet" @("run", "--configuration", $Configuration, "--project", "Weed.SmokeTests\Weed.SmokeTests.csproj")
    }

    Invoke-Native "Package $PluginId" "powershell" @(
        "-ExecutionPolicy", "Bypass",
        "-File", "scripts\package-plugin.ps1",
        "-PluginId", $PluginId,
        "-Configuration", $Configuration,
        "-Runtime", $Runtime
    )

    Invoke-Native "Push source branch" "git" @("push", $Remote, $Branch)
    Invoke-Native "Create tag $tag" "git" @("tag", "-a", $tag, "-m", "$($descriptor.name) Plugin v$Version")
    Invoke-Native "Push tag $tag" "git" @("push", $Remote, $tag)

    $outputRoot = Join-Path $repoRoot "artifacts\plugins"
    $zipPath = Join-Path $outputRoot "$PluginId-$Version-$Runtime.zip"
    $checksumPath = Join-Path $outputRoot "$PluginId-$Version-$Runtime.sha256"
    $metadataPath = Join-Path $outputRoot "$PluginId-$Version.plugin-release.json"
    if ([string]::IsNullOrWhiteSpace($ReleaseNotes)) {
        $ReleaseNotes = "$($descriptor.name) external plugin v$Version for Weed. Install the ZIP from Settings > External Plugins, then restart Weed."
    }

    $releaseArguments = @(
        "release", "create", $tag,
        $zipPath,
        $checksumPath,
        $metadataPath,
        "--title", "$($descriptor.name) Plugin v$Version",
        "--notes", $ReleaseNotes,
        "--latest=false"
    )
    if ($Draft) { $releaseArguments += "--draft" }
    if ($Prerelease) { $releaseArguments += "--prerelease" }
    Invoke-Native "Create GitHub plugin release" "gh" $releaseArguments

    if ($UpdateRegistry) {
        & (Join-Path $PSScriptRoot "update-plugin-registry.ps1") -MetadataPath $metadataPath
        Invoke-Native "Stage plugin registry" "git" @("add", "plugins.registry.json")
        & git diff --cached --quiet
        if ($LASTEXITCODE -eq 1) {
            Invoke-Native "Commit plugin registry" "git" @("commit", "-m", "chore: publish $PluginId $Version in registry")
            Invoke-Native "Push plugin registry" "git" @("push", $Remote, $Branch)
        }
        elseif ($LASTEXITCODE -ne 0) {
            throw "Could not inspect the staged plugin registry change."
        }
        else {
            Write-Host "Plugin registry already contains $PluginId $Version."
        }
    }

    Write-Host ""
    Write-Host "Plugin release complete: https://github.com/$($releaseConfig.repository)/releases/tag/$tag"
    Write-Host "Registry metadata: $metadataPath"
}
finally {
    Pop-Location
}
