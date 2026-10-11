param([Parameter(Mandatory)][string]$Source, [Parameter(Mandatory)][string]$Output, [string]$Publisher = 'CN=Packbench Local')
$ErrorActionPreference = 'Stop'
$makeappx = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Recurse -Filter makeappx.exe | Where-Object FullName -match '\\x64\\' | Select-Object -First 1
if (-not $makeappx) { throw 'Windows SDK makeappx.exe was not found.' }
if (-not (Test-Path (Join-Path $Source 'Packbench.exe'))) { throw 'MSIX source must contain Packbench.exe.' }
$stage = Join-Path ([IO.Path]::GetTempPath()) ('Packbench-msix-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
Get-ChildItem $Source -File | Copy-Item -Destination $stage -Force
$manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10" xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10" xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restricted">
  <Identity Name="Packbench" Publisher="$Publisher" Version="2.5.0.0" />
  <Properties><DisplayName>Packbench</DisplayName><PublisherDisplayName>informent</PublisherDisplayName><Description>Local Windows application packaging</Description><Logo>Assets\StoreLogo.png</Logo></Properties>
  <Resources><Resource Language="en-us" /></Resources>
  <Applications><Application Id="Packbench" Executable="Packbench.exe" EntryPoint="Windows.FullTrustApplication"><uap:VisualElements DisplayName="Packbench" Description="Local Windows application packaging" BackgroundColor="transparent" Square150x150Logo="Assets\150.png" Square44x44Logo="Assets\44.png" /></Application></Applications>
  <Capabilities><rescap:Capability Name="runFullTrust" /></Capabilities>
</Package>
"@
New-Item -ItemType Directory -Path (Join-Path $stage 'Assets') -Force | Out-Null
if (-not (Test-Path (Join-Path $Source 'Assets\StoreLogo.png'))) { throw 'MSIX packaging requires Assets\StoreLogo.png, Assets\150.png, and Assets\44.png.' }
Set-Content -Path (Join-Path $stage 'AppxManifest.xml') -Value $manifest -Encoding utf8
New-Item -ItemType Directory -Path $Output -Force | Out-Null
& $makeappx.FullName pack /d $stage /p (Join-Path $Output 'Packbench.msix') /o
if ($LASTEXITCODE -ne 0) { throw "makeappx failed with exit code $LASTEXITCODE." }
Remove-Item $stage -Recurse -Force
Write-Host "MSIX created in $Output"
