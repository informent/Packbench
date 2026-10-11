param([Parameter(Mandatory=$true)][string]$Setup)
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('Packbench-installer-tests-' + [Guid]::NewGuid().ToString('N'))
$install = Join-Path $root 'application'
$appProcess = $null
New-Item -ItemType Directory -Path $root -Force | Out-Null
function Invoke-SetupProcess([string]$File, [string]$Arguments) {
    $process = Start-Process -FilePath $File -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(120000)) { $process.Kill(); throw 'Installer timed out' }
    if ($process.ExitCode -ne 0) { throw ('Installer exit code ' + $process.ExitCode) }
}
try {
    $arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /DIR="' + $install + '" /LOG="' + (Join-Path $root 'install.log') + '"'
    Invoke-SetupProcess $Setup $arguments
    $exe = Join-Path $install 'Packbench.exe'
    if (!(Test-Path -LiteralPath $exe)) { throw 'Installed app is missing' }
    $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
    if ($info.ProductName -ne 'Packbench') { throw 'Installed executable branding is incorrect' }
    $note = Join-Path $install 'user-note.txt'
    [IO.File]::WriteAllText($note, 'Preserve this unrecognized user file')
    Invoke-SetupProcess $Setup $arguments
    if ([IO.File]::ReadAllText($note) -ne 'Preserve this unrecognized user file') { throw 'Reinstall changed a user file' }
    $appProcess = Start-Process -FilePath $exe -WindowStyle Hidden -PassThru
    Start-Sleep -Seconds 3
    if ($appProcess.HasExited) { throw 'Installed app exited during launch verification' }
    Stop-Process -Id $appProcess.Id -Force
    $appProcess.WaitForExit()
    $appProcess = $null
    Invoke-SetupProcess (Join-Path $install 'unins000.exe') '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
    if (Test-Path -LiteralPath $exe) { throw 'Uninstall left the installed executable' }
    if ([IO.File]::ReadAllText($note) -ne 'Preserve this unrecognized user file') { throw 'Uninstall removed a user file' }
    Write-Output 'PASS: automatic install, reinstall, installed app launch, uninstall and user-file preservation'
}
finally {
    if ($null -ne $appProcess -and !$appProcess.HasExited) { Stop-Process -Id $appProcess.Id -Force; $appProcess.WaitForExit() }
    $resolved = [IO.Path]::GetFullPath($root)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or !([IO.Path]::GetFileName($resolved)).StartsWith('Packbench-installer-tests-')) { throw 'Unsafe cleanup path' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
