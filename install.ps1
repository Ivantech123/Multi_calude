# Kept ASCII-only so it runs the same through 'irm | iex' and from a file in Windows PowerShell 5.
# Multi_calude installer for Windows.
#
#   irm https://raw.githubusercontent.com/Ivantech123/Multi_calude/main/install.ps1 | iex
#
# Or with options:
#   & ([scriptblock]::Create((irm https://raw.githubusercontent.com/Ivantech123/Multi_calude/main/install.ps1))) -Uninstall
#
#   -Version v0.1.0   a specific release instead of the latest
#   -Archive <zip>    install from a local multi_calude-win-*.zip
#   -NoTray           do not start the tray icon with Windows
#   -Uninstall        remove everything this script installed

param(
    [string]$Version = 'latest',
    [string]$Archive,
    [switch]$NoTray,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$repo = 'Ivantech123/Multi_calude'
$dir = Join-Path $env:LOCALAPPDATA 'Programs\multi_calude'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$startMenu = Join-Path ([Environment]::GetFolderPath('Programs')) 'Multi_calude.lnk'
$desktop = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Multi_calude.lnk'

function Set-UserPath([string]$Add, [string]$Remove) {
    $parts = @(([Environment]::GetEnvironmentVariable('Path', 'User') -split ';') | Where-Object { $_ -and $_ -ne $Remove -and $_ -ne $Add })
    if ($Add) { $parts += $Add }
    [Environment]::SetEnvironmentVariable('Path', ($parts -join ';'), 'User')
}

function Stop-Tray {
    Get-Process cli-tray -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$dir\*" } | Stop-Process -Force
    Start-Sleep -Milliseconds 300
}

function New-Shortcut([string]$Path, [string]$Target) {
    $shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($Path)
    $shortcut.TargetPath = $Target
    $shortcut.WorkingDirectory = $HOME
    $shortcut.IconLocation = "$Target,0"
    $shortcut.Description = 'Claude Code: pick an account and a session'
    $shortcut.Save()
}

if ($Uninstall) {
    if (Test-Path "$dir\cli.exe") { & "$dir\cli.exe" --uninstall }
    Stop-Tray
    Remove-ItemProperty $runKey -Name ClaudeAccountsTray -ErrorAction SilentlyContinue
    Remove-Item $startMenu, $desktop -ErrorAction SilentlyContinue
    Set-UserPath -Remove $dir
    Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host 'Multi_calude removed. Accounts (~/.claude-acc*) and sessions were left as they are.'
    return
}

$arch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }
if (-not $Archive) {
    $name = "multi_calude-win-$arch.zip"
    $url = if ($Version -eq 'latest') { "https://github.com/$repo/releases/latest/download/$name" }
           else { "https://github.com/$repo/releases/download/$Version/$name" }
    $Archive = Join-Path ([IO.Path]::GetTempPath()) $name
    Write-Host "Downloading $url"
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest $url -OutFile $Archive -UseBasicParsing
}

Stop-Tray
New-Item -ItemType Directory -Force $dir | Out-Null
Expand-Archive $Archive -DestinationPath $dir -Force

Set-UserPath -Add $dir
$env:Path = "$env:Path;$dir"
New-Shortcut $startMenu "$dir\cli.exe"
New-Shortcut $desktop "$dir\cli.exe"

if (-not $NoTray) {
    Set-ItemProperty $runKey -Name ClaudeAccountsTray -Value "`"$dir\cli-tray.exe`""
    Start-Process "$dir\cli-tray.exe"
}

& "$dir\cli.exe" --install
Write-Host ''
Write-Host "Installed to $dir. Open a new terminal window and type: cli"
