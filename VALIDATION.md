# Packbench 3.0.0 validation

## Automated checks

- `tests/EngineTests.cs`: checksum equality against actual ZIP contents, complete manifest coverage, repeat generation, Unicode filenames, a 32 MiB payload, preservation of an existing ZIP, locked-file failures and temporary-archive cleanup.
- `tests/PackageUi.ps1`: operate the published executable, choose a fixture project through its folder field, build a source ZIP, verify all archived hashes, and recover from an invalid folder and a .NET compiler error.
- Windows CI builds, tests and publishes a self-contained Windows x64 application and runs the packaged-app workflow.

The packaged-app tests create unique temporary project files and retain their numbered output folders as evidence. They do not install the application, replace desktop shortcuts, or modify real source projects.

## Limits

This update's checks do not validate installers, uninstallers, MSIX, signing certificates, Python/Node standalone adapters, or ARM64/x86 runtime behavior. Release-folder link rejection does not guarantee that a project's build scripts or source-bundle inputs are safe. Review projects and source bundles before building or distributing them. The executable is unsigned.
