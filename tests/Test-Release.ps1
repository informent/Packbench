$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$required = @(
    (Join-Path $root 'PackagingEngine.cs'),
    (Join-Path $root 'installer\Install-Packbench.ps1'),
    (Join-Path $root 'installer\Uninstall-Packbench.ps1'),
    (Join-Path $root 'installer\Build-MSIX.ps1'),
    (Join-Path $root 'tools\Sign-Release.ps1')
    (Join-Path $root 'tools\Build-Python.ps1')
    (Join-Path $root 'tools\Build-Node.ps1')
    (Join-Path $root 'tools\Generate-MSIXAssets.ps1')
    (Join-Path $root 'tools\Install-BundlerDependencies.ps1')
    (Join-Path $root 'Resources\Strings.en-US.json')
    (Join-Path $root 'Resources\Strings.fr-FR.json')
    (Join-Path $root 'Resources\Strings.es-ES.json')
)
foreach ($file in $required) { if (-not (Test-Path $file)) { throw "Required release component is missing: $file" } }
$null = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $root 'installer\Install-Packbench.ps1'), [ref]$null, [ref]$null)
$null = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $root 'installer\Uninstall-Packbench.ps1'), [ref]$null, [ref]$null)
$null = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $root 'installer\Build-MSIX.ps1'), [ref]$null, [ref]$null)
$null = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $root 'tools\Sign-Release.ps1'), [ref]$null, [ref]$null)
$null = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $root 'tools\Build-Python.ps1'), [ref]$null, [ref]$null)
$null = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $root 'tools\Build-Node.ps1'), [ref]$null, [ref]$null)
$null = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $root 'tools\Generate-MSIXAssets.ps1'), [ref]$null, [ref]$null)
$null = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $root 'tools\Install-BundlerDependencies.ps1'), [ref]$null, [ref]$null)
Write-Host 'PASS: packaging, installer, and signing sources are present'
& dotnet run --project (Join-Path $PSScriptRoot 'Packbench.EngineTests.csproj') -c Release | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Packaging engine tests failed with exit code $LASTEXITCODE." }
$out = Join-Path $root ('test-output-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $out | Out-Null
dotnet publish (Join-Path $root 'Packbench.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -o $out | Out-Host
$exe = Join-Path $out 'Packbench.exe'
if (-not (Test-Path $exe)) { throw 'Published executable is missing.' }
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant()
$p = Start-Process $exe -PassThru
Start-Sleep -Seconds 3
if ($p.HasExited) { throw "Published GUI exited during smoke test with code $($p.ExitCode)." }
Stop-Process -Id $p.Id -Force
Write-Host "PASS: published GUI stayed alive"
Write-Host "PASS: executable SHA256 $hash"
& dotnet run --project (Join-Path $PSScriptRoot 'UiSmoke.csproj') -c Release -- $exe | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Interactive UI smoke test failed with exit code $LASTEXITCODE." }
$install = Join-Path $env:TEMP ('Packbench-install-test-' + [guid]::NewGuid().ToString('N'))
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'installer\Install-Packbench.ps1') -Source $out -InstallRoot $install | Out-Host
if (-not (Test-Path (Join-Path $install 'Packbench.exe'))) { throw 'Installer did not copy the executable.' }
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'installer\Uninstall-Packbench.ps1') -InstallRoot $install | Out-Host
if (Test-Path $install) { throw 'Uninstaller did not remove the temporary install.' }
Write-Host 'PASS: installer and uninstaller round trip'
for ($attempt = 1; $attempt -le 5; $attempt++) {
    try { Remove-Item $out -Recurse -Force -ErrorAction Stop; break }
    catch { if ($attempt -eq 5) { Write-Warning "Could not remove test output; it can be cleaned later." } else { Start-Sleep -Milliseconds 500 } }
}
