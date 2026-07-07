param(
    [string]$Source = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Source)) {
    $Source = Join-Path $root "artifacts\Weed-win-x64"
}

if (-not (Test-Path $Source)) {
    & (Join-Path $PSScriptRoot "publish-release.ps1") -Output $Source
}

$installDir = Join-Path $env:LOCALAPPDATA "Weed\App"
New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Copy-Item (Join-Path $Source "*") $installDir -Recurse -Force

$exe = Join-Path $installDir "Weed.App.exe"
$startMenu = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
$shortcutPath = Join-Path $startMenu "Weed.lnk"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $installDir
$shortcut.Description = "Weed launcher"
$shortcut.Save()

Write-Host "Installed Weed to $installDir"
Write-Host "Start Menu shortcut: $shortcutPath"
