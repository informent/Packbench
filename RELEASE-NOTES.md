# Packbench 3.4.0

Packbench can now verify release archives in unattended scripts and write a structured result. Verification rejects unsafe Windows paths, links, file/directory collisions and duplicate entries, and enforces metadata and streamed decompression budgets. Existing reports are preserved.

## Verification

Engine regressions cover unsafe paths, archive links, entry and byte budgets, metadata limits, collisions, cancellation and both valid and rejected command results. The self-contained EXE passed unattended verification locally.

Automatic setup and portable builds include the .NET runtime. Installers are unsigned. ZIP checksums verify downloaded bytes; they do not authenticate an unknown publisher.

# Packbench 3.1.0

- Reject malformed JSON release manifests.
- Require unique checksum paths and strict SHA-256 formatting.
- Compare checksum coverage as an exact normalized path set so duplicate lines cannot mask missing payloads.

## 3.0.0

- Verify existing Packbench ZIPs directly from the desktop app.
- Validate manifests, complete SHA-256 coverage, every payload digest, duplicate archive paths, and traversal attempts.
- Reject corrupted packages with actionable per-file results.

- Include the release manifest in SHA-256 checksums and exclude the checksum list itself on repeated runs.
- Stream file hashes instead of loading entire payloads into memory.
- Preserve an existing ZIP and stage a new archive until creation succeeds; clean up unfinished archives on failure.
- Reject symbolic links and junctions when packaging a release folder.
- Rescan the selected project before building and reject unsupported folders.
- Prevent overlapping builds and recover from publisher failures without mixing asynchronous and synchronous stderr reads.
- Report version 2.9.0 consistently and compare update versions numerically.
- Add archive-content regression tests and automated packaged-app checks to Windows CI.

Windows x64 executable and ZIP. This update does not certify installers, code signing, ARM64/x86 execution, or standalone Python/Node adapters. Python and Node GUI output remains a source bundle.
