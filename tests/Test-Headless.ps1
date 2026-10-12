param([Parameter(Mandatory=$true)][string]$Exe)
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('Packbench-headless-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
function Invoke-Headless([string]$Arguments, [int]$Expected) {
    $process = Start-Process -FilePath $Exe -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(150)
        while (!$process.HasExited) {
            $process.Refresh()
            if ($process.MainWindowHandle -ne 0) { throw 'Unattended command opened a desktop window' }
            if ([DateTime]::UtcNow -ge $deadline) { throw 'Unattended command timed out' }
            Start-Sleep -Milliseconds 100
        }
        $process.WaitForExit()
        if ($process.ExitCode -ne $Expected) { throw "Expected exit $Expected, received $($process.ExitCode)" }
    }
    finally { if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() }; $process.Dispose() }
}
try {
    $payload = Join-Path $root 'payload'
    New-Item -ItemType Directory -Path $payload | Out-Null
    [IO.File]::WriteAllText((Join-Path $payload 'openpackager-manifest.json'), '{"schema":1}')
    [IO.File]::WriteAllText((Join-Path $payload 'hello.txt'), 'headless fixture')
    $lines = @('hello.txt','openpackager-manifest.json') | ForEach-Object { (Get-FileHash -LiteralPath (Join-Path $payload $_) -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_ }
    [IO.File]::WriteAllText((Join-Path $payload 'SHA256SUMS.txt'), ($lines -join "`n") + "`n")
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = Join-Path $root 'package.zip'
    [IO.Compression.ZipFile]::CreateFromDirectory($payload, $zip)
    $report = Join-Path $root 'verification.json'
    $arguments = '--verify "' + $zip + '" --output "' + $report + '"'
    Invoke-Headless $arguments 0
    $json = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    if (!$json.Valid -or $json.VerifiedFiles -ne 2) { throw 'Package verification evidence is incorrect' }
    $before = (Get-FileHash -LiteralPath $report -Algorithm SHA256).Hash
    Invoke-Headless $arguments 2
    if ((Get-FileHash -LiteralPath $report -Algorithm SHA256).Hash -ne $before) { throw 'Existing report changed' }
    Invoke-Headless '--invalid' 2
    Write-Output 'PASS: packaged unattended workflow, report content, existing-output protection, argument exits and no desktop window'
}
finally {
    $resolved = [IO.Path]::GetFullPath($root)
    $temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$resolved.StartsWith($temp, [StringComparison]::OrdinalIgnoreCase) -or !([IO.Path]::GetFileName($resolved)).StartsWith('Packbench-headless-')) { throw 'Unsafe cleanup path' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
