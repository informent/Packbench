param([Parameter(Mandatory=$true)][string]$Tag)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$product = 'Packbench'
[xml]$project = Get-Content -LiteralPath (Join-Path $root ($product + '.csproj')) -Raw
$version = [string]$project.Project.PropertyGroup.Version
if ($Tag -cne ('v' + $version) -or $Tag -notmatch '^v\d+\.\d+\.\d+$') { throw 'Tag must exactly match the application version' }
$output = Join-Path $root 'release-output'
if (Test-Path -LiteralPath $output) { throw 'Release output already exists; use a clean checkout' }
$tests = @()
foreach ($test in Get-ChildItem -LiteralPath (Join-Path $root 'tests') -Filter '*Tests.csproj' | Sort-Object Name) {
    & dotnet run --project $test.FullName -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw ('Regression failed: ' + $test.Name) }
    $tests += $test.Name
}
if ($tests.Count -eq 0) { throw 'No regression projects found' }
& (Join-Path $PSScriptRoot 'Build-Setup.ps1') -Verify
$payload = Join-Path $root 'installer-payload'
$exe = Join-Path $payload ($product + '.exe')
$info = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
if ($info.ProductName -cne $product -or ($info.ProductVersion -split '\+')[0] -cne $version) { throw 'Published executable branding/version mismatch' }
& (Join-Path $root 'tests/Test-Headless.ps1') -Exe $exe
New-Item -ItemType Directory -Path $output | Out-Null
$setupName = $product + '-' + $Tag + '-setup.exe'
Copy-Item -LiteralPath (Join-Path $root ('installer-output/' + $setupName)) -Destination $output
$zipName = $product + '-' + $Tag + '-win-x64.zip'
Compress-Archive -Path (Join-Path $payload '*') -DestinationPath (Join-Path $output $zipName) -CompressionLevel Optimal
$commit = (& git -C $root rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[a-f0-9]{40}$') { throw 'Cannot determine source commit' }
$evidence = [ordered]@{
    product = $product
    version = $version
    sourceCommit = $commit
    tag = $Tag
    os = [Environment]::OSVersion.VersionString
    testedProjects = $tests
    checks = @('regression-projects', 'self-contained-publish', 'executable-version', 'install', 'reinstall', 'installed-launch', 'uninstall', 'user-file-preservation', 'packaged-headless')
    signed = $false
    runtimeBundled = $true
    runUrl = if ($env:GITHUB_RUN_ID) { $env:GITHUB_SERVER_URL + '/' + $env:GITHUB_REPOSITORY + '/actions/runs/' + $env:GITHUB_RUN_ID } else { $null }
}
[IO.File]::WriteAllText((Join-Path $output 'RELEASE-VALIDATION.json'), ($evidence | ConvertTo-Json -Depth 6) + [Environment]::NewLine)
Copy-Item -LiteralPath (Join-Path $root 'RELEASE-NOTES.md') -Destination $output
$names = @($setupName, $zipName, 'RELEASE-VALIDATION.json')
$lines = foreach ($name in $names) { (Get-FileHash -LiteralPath (Join-Path $output $name) -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $name }
[IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS'), ($lines -join "`n") + "`n")
Write-Output ('PASS: verified release bundle ' + $product + ' ' + $Tag)

