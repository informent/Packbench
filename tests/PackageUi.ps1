param([Parameter(Mandatory=$true)][string]$Exe)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.IO.Compression.FileSystem
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('Packbench-ui-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $fixture
[IO.File]::WriteAllText((Join-Path $fixture 'package.json'), '{"name":"packaging-test","version":"1.0.0"}')
[IO.File]::WriteAllText((Join-Path $fixture 'hello.txt'), 'Packaged through the actual desktop application.')
$process = $null
$output = $null
function Find-Control($window, $id) {
    $window.FindFirst([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, $id))
}
try {
    $process = Start-Process -FilePath $Exe -PassThru
    $window = $null
    for ($i=0; $i -lt 80 -and $null -eq $window; $i++) {
        Start-Sleep -Milliseconds 250
        $window = [Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Children, [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id))
    }
    if ($null -eq $window) { throw 'Application window was not found.' }
    $pathControl = Find-Control $window 'ProjectPath'
    $pathValue = $pathControl.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)
    $pathValue.SetValue($fixture)
    $build = Find-Control $window 'BuildRelease'
    $build.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    $activity = Find-Control $window 'Activity'
    $text = ''
    for ($i=0; $i -lt 120; $i++) {
        Start-Sleep -Milliseconds 250
        $text = $activity.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value
        if ($text -match 'Source bundle complete\.') { break }
        if ($text -match 'Release failed:') { throw $text }
    }
    if ($text -notmatch 'Package:\s*([^\r\n]+)') { throw "Package not completed: $text" }
    $zip = $Matches[1].Trim()
    $output = [IO.Path]::GetFullPath($zip.Substring(0, $zip.Length - 4))
    $releaseRoot = [IO.Path]::GetFullPath((Join-Path $env:USERPROFILE 'Downloads\GITHUB'))
    if ([IO.Path]::GetDirectoryName($output) -ne $releaseRoot -or [IO.Path]::GetFileName($output) -notmatch '^\d+$') { throw 'Unexpected test output path.' }
    $archive = [IO.Compression.ZipFile]::OpenRead($zip)
    try {
        $checksums = $archive.GetEntry('SHA256SUMS.txt')
        $reader = [IO.StreamReader]::new($checksums.Open())
        try { $lines = $reader.ReadToEnd().Split("`n", [StringSplitOptions]::RemoveEmptyEntries) } finally { $reader.Dispose() }
        if ($lines.Count -ne $archive.Entries.Count - 1) { throw 'Incomplete checksum coverage.' }
        foreach ($line in $lines) {
            $entry = $archive.GetEntry($line.TrimEnd("`r").Substring(66))
            $stream = $entry.Open()
            $sha = [Security.Cryptography.SHA256]::Create()
            try { $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() } finally { $stream.Dispose(); $sha.Dispose() }
            if ($hash -ne $line.Substring(0,64)) { throw 'Archived file checksum mismatch.' }
        }
        if ($null -eq $archive.GetEntry('openpackager-manifest.json')) { throw 'Manifest missing.' }
    } finally { $archive.Dispose() }
    $pathValue.SetValue((Join-Path $fixture 'missing'))
    $build.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 500
    if (-not $build.Current.IsEnabled -or $process.HasExited) { throw 'Invalid-folder recovery failed.' }
    [IO.File]::WriteAllText((Join-Path $fixture 'Broken.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>')
    [IO.File]::WriteAllText((Join-Path $fixture 'Program.cs'), 'this deliberately does not compile;')
    $pathValue.SetValue($fixture)
    $build.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    for ($i=0; $i -lt 480; $i++) {
        Start-Sleep -Milliseconds 250
        $text = $activity.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value
        if ($text -match '^Build failed\.') { break }
        if ($text -match '^Release failed:') { throw "Unexpected publisher failure handling: $text" }
    }
    if ($text -notmatch '^Build failed\.' -or -not $build.Current.IsEnabled -or $process.HasExited) { throw "Publisher failure recovery failed: $text" }
    Write-Output 'PASS: packaged UI created a ZIP with matching hashes and manifest, and recovered from invalid folders and .NET compiler errors.'
} finally {
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    # Only the uniquely created fixture is removed; output is kept as test evidence.
    $resolved = [IO.Path]::GetFullPath($fixture)
    if ([IO.Path]::GetDirectoryName($resolved) -eq [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') -and [IO.Path]::GetFileName($resolved).StartsWith('Packbench-ui-')) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
