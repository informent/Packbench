param([string]$InstallRoot = "$env:LOCALAPPDATA\Packbench")
$ErrorActionPreference = 'Stop'
$shortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Packbench.lnk'
if (Test-Path $shortcut) { Remove-Item $shortcut -Force }
if (Test-Path $InstallRoot) { Remove-Item $InstallRoot -Recurse -Force }
Write-Host 'Packbench removed.'
