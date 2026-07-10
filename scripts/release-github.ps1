<#
.SYNOPSIS
Build, test, commit, push, package, tag, and upload a Weed GitHub release.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File scripts\release-github.ps1 -Version 0.1.3 -CommitMessage "Fix OCR release flow"

.EXAMPLE
powershell -ExecutionPolicy Bypass -File scripts\release-github.ps1 -Version 0.1.3 -SkipTests -Draft
#>
param(
    [string]$Version = "",
    [string]$CommitMessage = "",
    [string]$Remote = "origin",
    [string]$Branch = "",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$ReleaseTitle = "",
    [string]$ReleaseNotes = "",
    [switch]$SkipTests,
    [switch]$NoCommit,
    [switch]$Draft,
    [switch]$Prerelease,
    [switch]$Help
)

$ErrorActionPreference = "Stop"

function Write-Usage {
    Write-Host "Usage:"
    Write-Host "  powershell -ExecutionPolicy Bypass -File scripts\release-github.ps1 -Version 0.1.3 -CommitMessage `"Release v0.1.3`""
    Write-Host ""
    Write-Host "Common options:"
    Write-Host "  -SkipTests       Build and publish, but skip Weed.SmokeTests."
    Write-Host "  -NoCommit        Require a clean tree and release the current HEAD."
    Write-Host "  -Draft           Create the GitHub release as a draft."
    Write-Host "  -Prerelease      Mark the GitHub release as a prerelease."
}

if ($Help) {
    Write-Usage
    exit 0
}

function Require-Command {
    param([string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command is not available on PATH: $Name"
    }
}

function Invoke-Native {
    param(
        [string]$Label,
        [string]$FilePath,
        [string[]]$Arguments
    )

    Write-Host ""
    Write-Host "==> $Label"
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Label failed with exit code $LASTEXITCODE."
    }
}

function Get-AppVersion {
    param([string]$ProjectPath)

    $text = Get-Content -Raw -LiteralPath $ProjectPath
    $match = [regex]::Match($text, "<Version>([^<]+)</Version>")
    if (-not $match.Success) {
        throw "Could not find <Version> in $ProjectPath."
    }

    return $match.Groups[1].Value.Trim()
}

function Set-TagValue {
    param(
        [string]$Text,
        [string]$Tag,
        [string]$Value
    )

    $pattern = "(<$Tag>)[^<]*(</$Tag>)"
    if ($Text -notmatch $pattern) {
        throw "Could not find <$Tag> in project file."
    }

    return [regex]::Replace($Text, $pattern, ('${1}' + $Value + '${2}'), 1)
}

function Set-AppVersion {
    param(
        [string]$ProjectPath,
        [string]$NewVersion
    )

    if ($NewVersion -notmatch "^(\d+)\.(\d+)\.(\d+)(?:[-+][0-9A-Za-z.-]+)?$") {
        throw "Version must look like 0.1.3, with an optional prerelease/build suffix."
    }

    $assemblyVersion = "$($Matches[1]).$($Matches[2]).$($Matches[3]).0"
    $text = Get-Content -Raw -LiteralPath $ProjectPath
    $text = Set-TagValue $text "Version" $NewVersion
    $text = Set-TagValue $text "AssemblyVersion" $assemblyVersion
    $text = Set-TagValue $text "FileVersion" $assemblyVersion
    [System.IO.File]::WriteAllText($ProjectPath, $text, [System.Text.UTF8Encoding]::new($false))
}

function Get-CurrentBranch {
    $value = (& git branch --show-current).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "Could not read current Git branch."
    }

    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "Current checkout is detached. Pass -Branch explicitly or switch to a branch."
    }

    return $value
}

function Get-GitHubRepo {
    param([string]$RemoteName)

    $repo = (& gh repo view --json nameWithOwner -q ".nameWithOwner" 2>$null)
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($repo)) {
        return $repo.Trim()
    }

    $remoteUrl = (& git remote get-url $RemoteName).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "Could not read Git remote URL for $RemoteName."
    }

    if ($remoteUrl -match "github\.com[:/](?<owner>[^/]+)/(?<repo>[^/.]+)(?:\.git)?$") {
        return "$($Matches.owner)/$($Matches.repo)"
    }

    throw "Could not infer GitHub repository from remote URL: $remoteUrl"
}

function Test-LocalTag {
    param([string]$Tag)

    $value = (& git tag --list $Tag)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not list local Git tags."
    }

    return -not [string]::IsNullOrWhiteSpace(($value -join ""))
}

function Test-RemoteTag {
    param(
        [string]$RemoteName,
        [string]$Tag
    )

    $value = (& git ls-remote --tags $RemoteName "refs/tags/$Tag")
    if ($LASTEXITCODE -ne 0) {
        throw "Could not list remote Git tags."
    }

    return -not [string]::IsNullOrWhiteSpace(($value -join ""))
}

function Test-ReleaseExists {
    param([string]$Tag)

    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        & gh release view $Tag --json tagName 1>$null 2>$null
        return $LASTEXITCODE -eq 0
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
}

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Push-Location $root
try {
    Require-Command "git"
    Require-Command "dotnet"
    Require-Command "gh"

    $projectPath = Join-Path $root "Weed.App\Weed.App.csproj"
    $currentVersion = Get-AppVersion $projectPath
    if ([string]::IsNullOrWhiteSpace($Version)) {
        $Version = $currentVersion
    }

    if ($Version -ne $currentVersion) {
        if ($NoCommit) {
            throw "Project version is $currentVersion, but -Version is $Version. Remove -NoCommit so the script can commit the version bump, or edit the project first."
        }

        Write-Host "Updating Weed.App version: $currentVersion -> $Version"
        Set-AppVersion $projectPath $Version
    }

    $tag = "v$Version"
    if ([string]::IsNullOrWhiteSpace($Branch)) {
        $Branch = Get-CurrentBranch
    }

    $repo = Get-GitHubRepo $Remote
    $packageName = "Weed-$Runtime.zip"
    $manifestName = "Weed-$Runtime.update.json"
    $packageUrl = "https://github.com/$repo/releases/download/$tag/$packageName"

    if ([string]::IsNullOrWhiteSpace($CommitMessage)) {
        $CommitMessage = "Release $tag"
    }

    if ([string]::IsNullOrWhiteSpace($ReleaseTitle)) {
        $ReleaseTitle = "Weed $tag"
    }

    if ([string]::IsNullOrWhiteSpace($ReleaseNotes)) {
        $ReleaseNotes = "Release $tag for $Runtime."
    }

    if (Test-LocalTag $tag) {
        throw "Local tag already exists: $tag. Bump -Version or delete the tag manually."
    }

    if (Test-RemoteTag $Remote $tag) {
        throw "Remote tag already exists: $tag. Bump -Version or delete the tag manually."
    }

    if (Test-ReleaseExists $tag) {
        throw "GitHub release already exists: $tag. Delete it or choose another -Version."
    }

    Invoke-Native "Build solution" "dotnet" @("build", "Weed.sln", "--configuration", $Configuration)
    if (-not $SkipTests) {
        Invoke-Native "Run smoke tests" "dotnet" @("run", "--configuration", $Configuration, "--project", "Weed.SmokeTests\Weed.SmokeTests.csproj")
    }

    if ($NoCommit) {
        $dirty = (& git status --porcelain)
        if ($LASTEXITCODE -ne 0) {
            throw "Could not inspect Git status."
        }

        if (-not [string]::IsNullOrWhiteSpace(($dirty -join ""))) {
            throw "Working tree has uncommitted changes. Run without -NoCommit to commit all changes, or commit/clean them first."
        }
    }
    else {
        Invoke-Native "Stage all changes" "git" @("add", "-A")
        & git diff --cached --quiet
        if ($LASTEXITCODE -eq 1) {
            Invoke-Native "Commit changes" "git" @("commit", "-m", $CommitMessage)
        }
        elseif ($LASTEXITCODE -ne 0) {
            throw "Could not inspect staged Git diff."
        }
        else {
            Write-Host "No staged changes to commit."
        }
    }

    Invoke-Native "Push branch $Branch" "git" @("push", $Remote, $Branch)

    if (Test-LocalTag $tag) {
        throw "Local tag was created while this script was running: $tag"
    }

    if (Test-RemoteTag $Remote $tag) {
        throw "Remote tag was created while this script was running: $tag"
    }

    Invoke-Native "Create tag $tag" "git" @("tag", "-a", $tag, "-m", "Weed $tag")
    Invoke-Native "Push tag $tag" "git" @("push", $Remote, $tag)

    Invoke-Native "Publish package" "powershell" @(
        "-ExecutionPolicy", "Bypass",
        "-File", "scripts\publish-release.ps1",
        "-Configuration", $Configuration,
        "-Runtime", $Runtime,
        "-Version", $Version,
        "-PackageUrl", $packageUrl
    )

    $zipPath = Join-Path $root "artifacts\$packageName"
    $manifestPath = Join-Path $root "artifacts\$manifestName"
    if (-not (Test-Path $zipPath)) {
        throw "Package was not created: $zipPath"
    }

    if (-not (Test-Path $manifestPath)) {
        throw "Update manifest was not created: $manifestPath"
    }

    if (Test-ReleaseExists $tag) {
        throw "GitHub release already exists: $tag. Delete it or choose another -Version."
    }

    $releaseArgs = @(
        "release", "create", $tag,
        $zipPath,
        $manifestPath,
        "--title", $ReleaseTitle,
        "--notes", $ReleaseNotes
    )

    if ($Draft) {
        $releaseArgs += "--draft"
    }

    if ($Prerelease) {
        $releaseArgs += "--prerelease"
    }

    if (-not $Draft -and -not $Prerelease) {
        $releaseArgs += "--latest"
    }

    Invoke-Native "Create GitHub release" "gh" $releaseArgs

    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash.ToLowerInvariant()
    Write-Host ""
    Write-Host "Release complete: https://github.com/$repo/releases/tag/$tag"
    Write-Host "Package: $zipPath"
    Write-Host "Update manifest: $manifestPath"
    Write-Host "SHA256: $hash"
}
finally {
    Pop-Location
}
