# Local Windows build: dist\multi_calude-win-x64.zip (cli.exe + cli-tray.exe).
# The installer and the other platforms are built by .github/workflows/release.yml.
#
#   scripts\build.ps1                 win-x64
#   scripts\build.ps1 -Arch arm64     win-arm64

param([string]$Arch = 'x64')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
Set-Location $root

$package = "dist\win-$Arch-package"
Remove-Item $package -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish src/cli/cli.csproj -c Release -r "win-$Arch" -o $package --nologo
if ($LASTEXITCODE) { throw 'cli build failed' }
dotnet build src/tray/tray.csproj -c Release -o dist\tray --nologo
if ($LASTEXITCODE) { throw 'tray build failed' }

Copy-Item dist\tray\cli-tray.exe, dist\tray\cli-tray.exe.config, README.md, LICENSE $package
$zip = "dist\multi_calude-win-$Arch.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive "$package\*" $zip
Write-Host "-> $zip"
