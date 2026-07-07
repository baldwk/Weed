$ErrorActionPreference = "Stop"

$shortcutPath = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Weed.lnk"
if (Test-Path $shortcutPath) {
    Remove-Item -LiteralPath $shortcutPath -Force
}

$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
if (Test-Path $runKey) {
    Remove-ItemProperty -Path $runKey -Name "Weed" -ErrorAction SilentlyContinue
}

$installDir = Join-Path $env:LOCALAPPDATA "Weed\App"
if (Test-Path $installDir) {
    Remove-Item -LiteralPath $installDir -Recurse -Force
}

Write-Host "Removed Weed app files and Start Menu shortcut."
Write-Host "User data under $env:APPDATA\Weed and $env:LOCALAPPDATA\Weed is preserved."
