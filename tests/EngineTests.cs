using System.IO.Compression;
using System.Text.Json;
using System.Security.Cryptography;
using System.Diagnostics;
using Packbench;

var root = Path.Combine(Path.GetTempPath(), "openpackager-engine-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(root, "app"));
await File.WriteAllTextAsync(Path.Combine(root, "app", "hello.txt"), "Packbench engine test");
await File.WriteAllTextAsync(Path.Combine(root, "app", "résumé with spaces.txt"), "Unicode content");
var large = Path.Combine(root, "app", "large.bin");
using (var stream = File.Create(large)) stream.SetLength(32 * 1024 * 1024);
var zip = await PackagingEngine.CompleteReleaseAsync(root, new { test = true, createdUtc = DateTime.UtcNow });
if (!File.Exists(Path.Combine(root, "SHA256SUMS.txt"))) throw new Exception("Checksum file was not created.");
if (!File.Exists(Path.Combine(root, "openpackager-manifest.json"))) throw new Exception("Manifest was not created.");
if (!File.Exists(zip)) throw new Exception("ZIP was not created.");
var verifiedPackage = await PackagingEngine.VerifyZipAsync(zip);
if (!verifiedPackage.Valid || verifiedPackage.VerifiedFiles == 0) throw new Exception("Valid package verification failed.");
using (var archive = ZipFile.OpenRead(zip))
{
    if (archive.GetEntry("openpackager-manifest.json") is null) throw new Exception("Manifest missing from ZIP.");
    if (archive.GetEntry("SHA256SUMS.txt") is null) throw new Exception("Checksums missing from ZIP.");
    using var reader = new StreamReader(archive.GetEntry("SHA256SUMS.txt")!.Open());
    var lines = (await reader.ReadToEndAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
    if (lines.Length != archive.Entries.Count - 1) throw new Exception("Checksum coverage is incomplete.");
    foreach (var raw in lines)
    {
        var line = raw.TrimEnd('\r');
        var name = line[66..];
        if (name == "SHA256SUMS.txt") throw new Exception("Checksum file includes itself.");
        using var content = archive.GetEntry(name)?.Open() ?? throw new Exception("Missing archived file: " + name);
        if (!Convert.ToHexString(SHA256.HashData(content)).Equals(line[..64], StringComparison.OrdinalIgnoreCase))
            throw new Exception("Archived checksum mismatch: " + name);
    }
}
var before = await File.ReadAllTextAsync(Path.Combine(root, "SHA256SUMS.txt"));
await PackagingEngine.WriteChecksumsAsync(root);
if (before != await File.ReadAllTextAsync(Path.Combine(root, "SHA256SUMS.txt"))) throw new Exception("Repeated checksums changed.");
var zipHash = SHA256.HashData(await File.ReadAllBytesAsync(zip));
try { PackagingEngine.CreateZip(root + Path.DirectorySeparatorChar); throw new Exception("Existing ZIP was overwritten."); }
catch (IOException) { }
var preservedHash = SHA256.HashData(await File.ReadAllBytesAsync(zip));
if (!zipHash.SequenceEqual(preservedHash)) throw new Exception("Existing ZIP changed.");
var corrupt = zip + ".corrupt.zip"; File.Copy(zip, corrupt);
using (var archive = ZipFile.Open(corrupt, ZipArchiveMode.Update))
{
    var entry = archive.GetEntry("app/hello.txt")!; entry.Delete();
    using var writer = new StreamWriter(archive.CreateEntry("app/hello.txt").Open()); writer.Write("tampered");
}
var corruptResult = await PackagingEngine.VerifyZipAsync(corrupt);
if (corruptResult.Valid || !corruptResult.Errors.Any(x => x.Contains("Checksum mismatch"))) throw new Exception("Corrupt package was accepted.");
File.Delete(corrupt);

var duplicateCoverage = zip + ".duplicate-coverage.zip"; File.Copy(zip, duplicateCoverage);
using (var archive = ZipFile.Open(duplicateCoverage, ZipArchiveMode.Update))
{
    var checksumEntry = archive.GetEntry("SHA256SUMS.txt")!; string[] lines; using (var reader = new StreamReader(checksumEntry.Open())) lines = (await reader.ReadToEndAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries); checksumEntry.Delete();
    using var writer = new StreamWriter(archive.CreateEntry("SHA256SUMS.txt").Open()); for (var i = 0; i < lines.Length; i++) await writer.WriteLineAsync(i == lines.Length - 1 ? lines[0] : lines[i]);
}
var duplicateCoverageResult = await PackagingEngine.VerifyZipAsync(duplicateCoverage);
if (duplicateCoverageResult.Valid || !duplicateCoverageResult.Errors.Any(x => x.Contains("Duplicate checksum path")) || !duplicateCoverageResult.Errors.Any(x => x.Contains("coverage is incomplete"))) throw new Exception("Duplicate checksum coverage was accepted.");
File.Delete(duplicateCoverage);

var malformedManifest = zip + ".bad-manifest.zip"; File.Copy(zip, malformedManifest);
using (var archive = ZipFile.Open(malformedManifest, ZipArchiveMode.Update)) { var entry = archive.GetEntry("openpackager-manifest.json")!; entry.Delete(); using var writer = new StreamWriter(archive.CreateEntry("openpackager-manifest.json").Open()); await writer.WriteAsync("{ invalid json"); }
var malformedManifestResult = await PackagingEngine.VerifyZipAsync(malformedManifest);
if (malformedManifestResult.Valid || !malformedManifestResult.Errors.Any(x => x.Contains("Manifest JSON is invalid"))) throw new Exception("Malformed manifest was accepted.");
File.Delete(malformedManifest);
File.Delete(zip);
var lockedFile = Path.Combine(root, "app", "hello.txt");
using (var locked = new FileStream(lockedFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
{
    try { PackagingEngine.CreateZip(root); throw new Exception("Locked file should fail packaging."); }
    catch (IOException) { }
}
if (File.Exists(zip) || Directory.EnumerateFiles(Path.GetDirectoryName(root)!, Path.GetFileName(root) + ".zip.*.tmp").Any())
    throw new Exception("Failed ZIP left output behind.");

var sourceFixture = Path.Combine(root, "source-fixture");
var externalFixture = root + "-external";
var junctionFixture = Path.Combine(sourceFixture, "linked-outside");
Directory.CreateDirectory(sourceFixture);
Directory.CreateDirectory(externalFixture);
await File.WriteAllTextAsync(Path.Combine(sourceFixture, "included.txt"), "inside source");
await File.WriteAllTextAsync(Path.Combine(externalFixture, "outside-secret.txt"), "outside source");
try
{
    var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var argument in new[] { "/c", "mklink", "/J", junctionFixture, externalFixture }) start.ArgumentList.Add(argument);
    using var junction = Process.Start(start) ?? throw new Exception("Could not start source junction fixture command.");
    junction.WaitForExit();
    if (junction.ExitCode != 0) throw new Exception("Could not create source junction fixture.");
    try { PackagingEngine.EnumerateSourceFiles(sourceFixture, new HashSet<string>(StringComparer.OrdinalIgnoreCase)); throw new Exception("Source enumeration accepted a junction to an outside folder."); }
    catch (IOException ex) when (ex.Message.Contains("symbolic link or junction", StringComparison.OrdinalIgnoreCase)) { }
    if (!File.Exists(Path.Combine(externalFixture, "outside-secret.txt"))) throw new Exception("Source enumeration modified external content.");
}
finally
{
    if (Directory.Exists(junctionFixture)) Directory.Delete(junctionFixture);
    if (Directory.Exists(externalFixture)) Directory.Delete(externalFixture, true);
}
Directory.Delete(root, true);
Console.WriteLine("PASS: archive creation, strict manifest and checksum coverage verification, corruption rejection, Unicode, large file, repeated checksums, existing ZIP protection, failure cleanup, and source-junction rejection");

var guardRoot = Path.Combine(Path.GetTempPath(), "packbench-guards-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(guardRoot);
try
{
    foreach (var unsafeName in new[] { "../escape", "C:/escape", "file:stream", "CON.txt", "folder/trailing.", "folder//file", "folder/./file", "folder/trailing ", "COM1.bin" })
    {
        var path = Path.Combine(guardRoot, "unsafe.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create)) { using var writer = new StreamWriter(archive.CreateEntry(unsafeName).Open()); writer.Write("payload"); }
        var rejected = await PackagingEngine.VerifyZipAsync(path);
        if (rejected.Valid || !rejected.Errors.Any(e => e.Contains("Unsafe archive path"))) throw new Exception("Unsafe archive accepted: " + unsafeName);
        File.Delete(path);
    }
    var linkPath = Path.Combine(guardRoot, "linked.zip");
    using (var archive = ZipFile.Open(linkPath, ZipArchiveMode.Create)) { var entry = archive.CreateEntry("link"); entry.ExternalAttributes = unchecked((int)0xA1FF0000); using var writer = new StreamWriter(entry.Open()); writer.Write("target"); }
    if (!(await PackagingEngine.VerifyZipAsync(linkPath)).Errors.Any(e => e.Contains("Linked archive"))) throw new Exception("Archive link accepted.");
    var budgetPath = Path.Combine(guardRoot, "budget.zip");
    using (var archive = ZipFile.Open(budgetPath, ZipArchiveMode.Create)) { using var writer = new StreamWriter(archive.CreateEntry("large.txt").Open()); writer.Write(new string('x', 1000)); }
    if (!(await PackagingEngine.VerifyZipAsync(budgetPath, limits: new(MaxEntryBytes: 100))).Errors.Any(e => e.Contains("budget"))) throw new Exception("Decompression budget ignored.");
    var conflictPath = Path.Combine(guardRoot, "conflict.zip");
    using (var archive = ZipFile.Open(conflictPath, ZipArchiveMode.Create)) { archive.CreateEntry("file"); archive.CreateEntry("file/child.txt"); }
    if (!(await PackagingEngine.VerifyZipAsync(conflictPath)).Errors.Any(e => e.Contains("conflicts"))) throw new Exception("File/directory collision accepted.");
    if (!(await PackagingEngine.VerifyZipAsync(conflictPath, limits: new(MaxEntries: 1))).Errors.Any(e => e.Contains("entry budget"))) throw new Exception("Entry budget ignored.");
    var duplicatePath = Path.Combine(guardRoot, "duplicate.zip");
    using (var archive = ZipFile.Open(duplicatePath, ZipArchiveMode.Create)) { archive.CreateEntry("File.txt"); archive.CreateEntry("file.txt"); }
    if (!(await PackagingEngine.VerifyZipAsync(duplicatePath)).Errors.Any(e => e.Contains("Duplicate archive"))) throw new Exception("Windows case collision accepted.");
    var metadataPath = Path.Combine(guardRoot, "metadata.zip");
    using (var archive = ZipFile.Open(metadataPath, ZipArchiveMode.Create)) { using var writer = new StreamWriter(archive.CreateEntry("openpackager-manifest.json").Open()); writer.Write("{ \"value\": \"oversized\" }"); }
    if (!(await PackagingEngine.VerifyZipAsync(metadataPath, limits: new(MaxManifestBytes: 2))).Errors.Any(e => e.Contains("Manifest size budget"))) throw new Exception("Manifest budget ignored.");
    if (!(await PackagingEngine.VerifyZipAsync(budgetPath, limits: new(MaxTotalBytes: 100))).Errors.Any(e => e.Contains("decompression budget"))) throw new Exception("Total decompression budget ignored.");
    using var canceled = new CancellationTokenSource(); canceled.Cancel();
    try { await PackagingEngine.VerifyZipAsync(budgetPath, canceled.Token); throw new Exception("Canceled verification ran."); } catch (OperationCanceledException) { }
    var report = Path.Combine(guardRoot, "report.json");
    if (CommandLine.Run(new[] { "--verify", linkPath, "--output", report }) != 3 || !File.Exists(report)) throw new Exception("Rejected-package CLI evidence missing.");
    var preserved = File.ReadAllText(report);
    if (CommandLine.Run(new[] { "--verify", linkPath, "--output", report }) != 2 || File.ReadAllText(report) != preserved) throw new Exception("CLI report was overwritten.");
    if (CommandLine.Run(new[] { "--bad" }) != 2) throw new Exception("Invalid CLI arguments accepted.");
    var payload = Path.Combine(guardRoot, "payload"); Directory.CreateDirectory(payload); File.WriteAllText(Path.Combine(payload, "ok.txt"), "okay");
    var validZip = await PackagingEngine.CompleteReleaseAsync(payload, new { schema = 1 });
    var validReport = Path.Combine(guardRoot, "valid.json");
    if (CommandLine.Run(new[] { "--verify", validZip, "--output", validReport }) != 0) throw new Exception("Valid unattended verification failed.");
}
finally { Directory.Delete(guardRoot, true); }
Console.WriteLine("PASS: hostile paths, archive links, resource budgets, cancellation and unattended verification");
