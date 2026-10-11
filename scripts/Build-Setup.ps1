param([switch]$Verify)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'Packbench.csproj'
[xml]$xml = Get-Content -LiteralPath $project -Raw
$version = [string]$xml.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid application version' }
$payload = Join-Path $root 'installer-payload'
$output = Join-Path $root 'installer-output'
New-Item -ItemType Directory -Path $payload,$output -Force | Out-Null
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o $payload --nologo
if ($LASTEXITCODE -ne 0) { throw 'Self-contained publish failed' }
Copy-Item -LiteralPath (Join-Path $root 'LICENSE'),(Join-Path $root 'README.md') -Destination $payload
$compiler = @((Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),(Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),(Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe')) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (!$compiler) { throw 'Install the open-source Inno Setup compiler from https://jrsoftware.org/isdl.php, then retry.' }
& $compiler ('/DAppVersion=' + $version) ('/DPayloadDir=' + $payload) ('/DOutputDir=' + $output) (Join-Path $root 'installer\Setup.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed' }
$setup = Join-Path $output ('Packbench-v' + $version + '-setup.exe')
if (!(Test-Path -LiteralPath $setup)) { throw 'Installer missing' }
$hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS'), $hash + '  ' + [IO.Path]::GetFileName($setup) + [Environment]::NewLine)
if ($Verify) { & (Join-Path $root 'tests\Test-Installer.ps1') -Setup $setup }
Write-Output ('PASS: self-contained setup created: ' + [IO.Path]::GetFileName($setup))
