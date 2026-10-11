using System.IO.Compression;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Packbench;

public static class PackagingEngine
{
    public sealed record VerificationResult(bool Valid, int VerifiedFiles, IReadOnlyList<string> Errors);

    public static async Task<VerificationResult> VerifyZipAsync(string zipPath)
    {
        var errors = new List<string>(); var verified = 0;
        using var archive = ZipFile.OpenRead(zipPath);
        var entries = archive.Entries.Where(x => !string.IsNullOrEmpty(x.Name)).ToArray();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (Path.IsPathRooted(name) || name.Split('/').Any(x => x == "..")) errors.Add($"Unsafe archive path: {name}");
            if (!names.Add(name)) errors.Add($"Duplicate archive path: {name}");
        }
        var manifest = entries.FirstOrDefault(x => x.FullName.Replace('\\', '/') == "openpackager-manifest.json");
        var sums = entries.FirstOrDefault(x => x.FullName.Replace('\\', '/') == "SHA256SUMS.txt");
        if (manifest is null) errors.Add("Manifest is missing.");
        else
        {
            try { using var manifestContent = manifest.Open(); using var document = await JsonDocument.ParseAsync(manifestContent); if (document.RootElement.ValueKind != JsonValueKind.Object) errors.Add("Manifest root must be a JSON object."); }
            catch (JsonException ex) { errors.Add($"Manifest JSON is invalid: {ex.Message}"); }
        }
        if (sums is null) errors.Add("SHA256SUMS.txt is missing.");
        if (sums is not null)
        {
            using var reader = new StreamReader(sums.Open());
            var lines = (await reader.ReadToEndAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var checksummedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in lines)
            {
                var line = raw.TrimEnd('\r');
                if (line.Length < 67 || line[64..66] != "  ") { errors.Add("Malformed checksum line."); continue; }
                var expected = line[..64]; var name = line[66..].Replace('\\', '/');
                if (expected.Length != 64 || !expected.All(Uri.IsHexDigit)) { errors.Add($"Invalid SHA-256 value: {name}"); continue; }
                if (!checksummedNames.Add(name)) { errors.Add($"Duplicate checksum path: {name}"); continue; }
                var entry = entries.FirstOrDefault(x => x.FullName.Replace('\\', '/').Equals(name, StringComparison.OrdinalIgnoreCase));
                if (entry is null) { errors.Add($"Checksummed file is missing: {name}"); continue; }
                using var content = entry.Open();
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(content)).ToLowerInvariant();
                if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) errors.Add($"Checksum mismatch: {name}"); else verified++;
            }
            var expectedCoverage = entries.Where(x => !x.FullName.Replace('\\', '/').Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)).Select(x => x.FullName.Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!checksummedNames.SetEquals(expectedCoverage)) errors.Add("Checksum coverage is incomplete.");
        }
        return new(errors.Count == 0, verified, errors);
    }

    public static async Task<string> WriteChecksumsAsync(string releaseFolder)
    {
        var root = Path.GetFullPath(releaseFolder);
        var path = Path.Combine(root, "SHA256SUMS.txt");
        var files = EnumerateRegularFiles(root).Where(file => !string.Equals(file, path, StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => Path.GetRelativePath(root, file), StringComparer.Ordinal).ToArray();
        var lines = new List<string>();
        foreach (var file in files)
        {
            await using var stream = File.OpenRead(file);
            var hash = await SHA256.HashDataAsync(stream);
            lines.Add($"{Convert.ToHexString(hash).ToLowerInvariant()}  {Path.GetRelativePath(root, file).Replace('\\', '/')}");
        }
        await File.WriteAllLinesAsync(path, lines);
        return path;
    }

    public static async Task<string> WriteManifestAsync(string releaseFolder, object manifest)
    {
        var path = Path.Combine(releaseFolder, "openpackager-manifest.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    public static IReadOnlyList<string> EnumerateSourceFiles(string sourceFolder, IReadOnlySet<string> excludedDirectoryNames)
    {
        ArgumentNullException.ThrowIfNull(excludedDirectoryNames);
        if (!Directory.Exists(sourceFolder)) throw new DirectoryNotFoundException(sourceFolder);
        var root = Path.GetFullPath(sourceFolder);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Source project root must not be a symbolic link or junction.");

        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                var isDirectory = (attributes & FileAttributes.Directory) != 0;
                if (isDirectory && excludedDirectoryNames.Contains(Path.GetFileName(entry))) continue;
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"Source project contains a symbolic link or junction: {Path.GetRelativePath(root, entry)}");
                if (isDirectory) pending.Push(entry);
                else files.Add(Path.GetFullPath(entry));
            }
        }
        return files.OrderBy(file => Path.GetRelativePath(root, file), StringComparer.Ordinal).ToArray();
    }

    // Refuse linked content instead of silently including files outside the release.
    static IEnumerable<string> EnumerateRegularFiles(string folder)
    {
        if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Release folders must not contain symbolic links or junctions.");
        foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Release folders must not contain symbolic links or junctions.");
            if ((attributes & FileAttributes.Directory) != 0)
            {
                foreach (var file in EnumerateRegularFiles(entry)) yield return file;
            }
            else yield return entry;
        }
    }

    public static async Task<string> CompleteReleaseAsync(string releaseFolder, object manifest)
    {
        await WriteManifestAsync(releaseFolder, manifest);
        await WriteChecksumsAsync(releaseFolder);
        return CreateZip(releaseFolder);
    }

    public static string CreateZip(string releaseFolder)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(releaseFolder));
        var zip = root + ".zip";
        if (File.Exists(zip)) throw new IOException("The release ZIP already exists. Choose a new output folder.");
        var temporary = zip + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var files = EnumerateRegularFiles(root).OrderBy(file => file, StringComparer.Ordinal).ToArray();
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
                foreach (var file in files)
                    archive.CreateEntryFromFile(file, Path.GetRelativePath(root, file).Replace('\\', '/'), CompressionLevel.Optimal);
            File.Move(temporary, zip);
            return zip;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
