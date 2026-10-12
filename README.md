# Packbench

Packbench is a privacy-first, open-source Windows release builder for developers who want a professional packaging workflow without a subscription.

## Current workflow

- Detects .NET, Python, and Node projects.
- Detects supported project markers before packaging.
- Publishes .NET applications with configurable self-contained and single-file options.
- Assembles a clean release directory.
- Creates SHA-256 checksums and a JSON build report.
- Never uploads source code or uses telemetry.
- Automatically places each desktop build in the next numbered `Downloads\\GITHUB` update folder.
- Writes a release manifest and SHA-256 checksum beside every executable.
- Creates a compressed ZIP package automatically after a successful build.

## Usage

Start `Packbench.exe`, choose your project folder, select the runtime and packaging options, then select **Build release**. Completed packages are placed in numbered folders under `Downloads\GITHUB`, with a ZIP beside the folder.

.NET projects are published using your installed .NET SDK. Python and Node projects produce source bundles; building standalone Python or Node executables is a separate workflow using the scripts under `tools`. Review source bundles before distributing them because project files can contain private configuration.

Each completed package contains `openpackager-manifest.json` and `SHA256SUMS.txt`. Checksums cover the manifest and every payload file, excluding the checksum file itself. Repeating checksum generation without changing the payload produces the same checksum list. ZIP files contain timestamps and are not claimed to be byte-for-byte reproducible.

Version 3.0.0 adds independent ZIP verification from the desktop app. It validates the manifest, complete checksum coverage, every SHA-256 digest, duplicate entries, and unsafe traversal paths before a package is distributed. Existing streaming checksums, failure cleanup, link rejection, and publisher recovery remain in place. This is a local packaging tool, not a sandbox for untrusted project build scripts.

Version 3.1.0 makes that verification strict: release manifests must parse as JSON objects, checksum paths must be unique, SHA-256 text must be valid hexadecimal, and the normalized checksum-name set must exactly match every payload entry. A duplicated checksum can no longer hide an omitted file.

Version 3.2.0 rejects linked source roots and linked descendants before creating a source bundle, preventing recursive enumeration from escaping the selected project through directory junctions. Windows release builds run the engine regressions and real packaged-app UI workflow, then publish the self-contained executable ZIP with a SHA-256 checksum.

## Verification

```powershell
dotnet run --project tests/Packbench.EngineTests.csproj -c Release
dotnet publish Packbench.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o test-output
powershell -NoProfile -File tests/PackageUi.ps1 -Exe "$PWD/test-output/Packbench.exe"
```

The packaged-app test needs an interactive Windows desktop. It creates a fixture project, builds a source ZIP through the actual window, verifies archived hashes, and checks invalid-folder and compiler-error recovery. GitHub Actions runs the engine and packaged-app tests on Windows. See [VALIDATION.md](VALIDATION.md) for the verification scope.

Portable packages include installer scripts under `installer`. Code signing is supported through `tools\Sign-Release.ps1` when a valid certificate thumbprint is supplied; unsigned builds are never presented as signed.

## License

MIT. See [LICENSE](LICENSE).

## Name change

Packbench replaces OpenPackager. New builds use Packbench.exe and Packbench.csproj. Existing releases remain available under their original names.

## Unattended package verification

Version 3.4.0 verifies release ZIPs in scripts without opening a window:

```powershell
$p = Start-Process .\Packbench.exe -WindowStyle Hidden -ArgumentList '--verify "C:\Releases\app.zip" --output "C:\Reports\verification.json"' -Wait -PassThru
$p.ExitCode
```

Use an existing report directory and a new JSON filename. Exit 0 means the package passed, 3 means it failed verification and the JSON contains findings, 2 means invalid arguments or an existing output, and 1 means an unreadable archive or operation failure. The command has a two-minute cancellation budget and never extracts package contents.

Verification rejects Windows device names, alternate data streams, ambiguous path segments, duplicate paths, file/directory collisions and archive links. Default limits are 100,000 entries, 2 GiB per entry, 8 GiB total uncompressed data, 4 MiB manifest JSON and 16 MiB checksum metadata. Streaming hash reads enforce the byte budget. Checksums establish internal consistency, not the identity or trustworthiness of a publisher.

## Automatic setup

Windows setup packages install Packbench for your current account, bundle the .NET runtime, create a Start menu shortcut and register an uninstaller. A desktop shortcut is optional. No administrator password or manual file copying is required. Existing settings and saved data stay in their current locations. The portable ZIP remains available.

Download `Packbench-v3.4.0-setup.exe` from Releases and run it.

For unattended deployment:

```powershell
Start-Process .\Packbench-v3.4.0-setup.exe -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-' -Wait -PassThru
```

Build from source with the [open-source Inno Setup compiler](https://github.com/jrsoftware/issrc) installed:

```powershell
pwsh -File scripts/Build-Setup.ps1 -Verify
```

The verification option installs into a temporary test folder, checks reinstall and launch, then uninstalls. It runs on the hosted Windows runner during CI. It does not remove existing user settings or backups.
