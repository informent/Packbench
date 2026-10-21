using System.IO.Compression;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Packbench;

public static class PackagingEngine
{
    public sealed record VerificationResult(bool Valid, int VerifiedFiles, IReadOnlyList<string> Errors);

    public sealed record VerificationLimits(int MaxEntries = 100_000, long MaxTotalBytes = 8L * 1024 * 1024 * 1024, long MaxEntryBytes = 2L * 1024 * 1024 * 1024, int MaxManifestBytes = 4 * 1024 * 1024, int MaxChecksumBytes = 16 * 1024 * 1024);

    public static async Task<VerificationResult> VerifyZipAsync(string zipPath, CancellationToken cancellationToken = default, VerificationLimits? limits = null)
    {
        limits ??= new();
        if (limits.MaxEntries < 1 || limits.MaxTotalBytes < 1 || limits.MaxEntryBytes < 1 || limits.MaxManifestBytes < 1 || limits.MaxChecksumBytes < 1) throw new ArgumentOutOfRangeException(nameof(limits));
        cancellationToken.ThrowIfCancellationRequested();
        var errors = new List<string>(); var verified = 0;
        using var archive = ZipFile.OpenRead(zipPath);
        if (archive.Entries.Count > limits.MaxEntries) return new(false, 0, new[] { "Archive entry budget exceeded." });
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase); long declared = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = entry.FullName.Replace('\\', '/');
            var directory = name.EndsWith('/'); var candidate = directory ? name[..^1] : name;
            if (!SafeArchivePath(candidate)) return new(false, 0, new[] { "Unsafe archive path." });
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0) return new(false, 0, new[] { "Linked archive entry is not allowed." });
            if (entry.Length > limits.MaxEntryBytes || entry.Length > limits.MaxTotalBytes - declared) return new(false, 0, new[] { "Archive decompression budget exceeded." });
            declared += entry.Length;
            if (!entries.TryAdd(candidate, entry)) return new(false, 0, new[] { "Duplicate archive path." });
        }
        foreach (var name in entries.Keys)
        {
            var slash = name.IndexOf('/');
            while (slash >= 0)
            {
                if (entries.TryGetValue(name[..slash], out var ancestor) && !ancestor.FullName.EndsWith('/')) return new(false, 0, new[] { "Archive file conflicts with a directory path." });
                slash = name.IndexOf('/', slash + 1);
            }
        }
        var files = entries.Where(e => !e.Value.FullName.EndsWith('/')).ToDictionary(e => e.Key, e => e.Value, StringComparer.OrdinalIgnoreCase);
        if (!files.TryGetValue("openpackager-manifest.json", out var manifest)) errors.Add("Manifest is missing.");
        else
        {
            if (manifest.Length > limits.MaxManifestBytes) return new(false, 0, new[] { "Manifest size budget exceeded." });
            try { using var document = JsonDocument.Parse(await ReadBounded(manifest, limits.MaxManifestBytes, cancellationToken)); if (document.RootElement.ValueKind != JsonValueKind.Object) errors.Add("Manifest root must be a JSON object."); }
            catch (JsonException) { errors.Add("Manifest JSON is invalid."); }
        }
        if (!files.TryGetValue("SHA256SUMS.txt", out var sums)) errors.Add("SHA256SUMS.txt is missing.");
        else
        {
            if (sums.Length > limits.MaxChecksumBytes) return new(false, 0, new[] { "Checksum metadata size budget exceeded." });
            var lines = System.Text.Encoding.UTF8.GetString(await ReadBounded(sums, limits.MaxChecksumBytes, cancellationToken)).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var checksummedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long actualTotal = 0;
            foreach (var raw in lines)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (errors.Count >= 100) { errors.Add("Additional errors omitted."); break; }
                var line = raw.TrimEnd('\r');
                if (line.Length < 67 || line[64..66] != "  ") { errors.Add("Malformed checksum line."); continue; }
                var expected = line[..64]; var name = line[66..].Replace('\\', '/');
                if (!SafeArchivePath(name)) { errors.Add("Unsafe checksum path."); continue; }
                if (!expected.All(Uri.IsHexDigit)) { errors.Add("Invalid SHA-256 value."); continue; }
                if (!checksummedNames.Add(name)) { errors.Add("Duplicate checksum path."); continue; }
                if (!files.TryGetValue(name, out var entry)) { errors.Add("Checksummed file is missing."); continue; }
                using var content = entry.Open(); using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[64 * 1024]; long entryBytes = 0; int read;
                while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    entryBytes += read; actualTotal += read;
                    if (entryBytes > limits.MaxEntryBytes || actualTotal > limits.MaxTotalBytes) return new(false, verified, new[] { "Archive decompression budget exceeded." });
                    hash.AppendData(buffer, 0, read);
                }
                var actual = Convert.ToHexString(hash.GetHashAndReset());
                if (entryBytes != entry.Length || !actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) errors.Add($"Checksum mismatch: {name}"); else verified++;
            }
            var expectedCoverage = files.Keys.Where(x => !x.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!checksummedNames.SetEquals(expectedCoverage)) errors.Add("Checksum coverage is incomplete.");
        }
        return new(errors.Count == 0, verified, errors);
    }

    private static bool SafeArchivePath(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || Path.IsPathRooted(name) || name.Length > 4096) return false;
        foreach (var segment in name.Split('/'))
        {
            if (segment.Length == 0 || segment is "." or ".." || segment.EndsWith(' ') || segment.EndsWith('.') || segment.Any(c => c < 32 || c is ':' or '<' or '>' or '"' or '|' or '?' or '*')) return false;
            var stem = segment.Split('.')[0];
            if (new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" }.Contains(stem, StringComparer.OrdinalIgnoreCase) || stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && (stem[3] is >= '1' and <= '9' or '\u00b9' or '\u00b2' or '\u00b3')) return false;
        }
        return true;
    }

    private static async Task<byte[]> ReadBounded(ZipArchiveEntry entry, int maximum, CancellationToken cancellationToken)
    {
        using var source = entry.Open(); using var output = new MemoryStream(); var buffer = new byte[8192]; int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (output.Length + read > maximum) throw new InvalidDataException("Archive metadata budget exceeded.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
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
        await WriteMetadataAsync(path, string.Join(Environment.NewLine, lines) + Environment.NewLine);
        return path;
    }

    public static async Task<string> WriteManifestAsync(string releaseFolder, object manifest)
    {
        var path = Path.Combine(releaseFolder, "openpackager-manifest.json");
        await WriteMetadataAsync(path, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    public static IReadOnlyList<string> EnumerateSourceFiles(string sourceFolder, IReadOnlySet<string> excludedDirectoryNames)
    {
        ArgumentNullException.ThrowIfNull(excludedDirectoryNames);
        if (!Directory.Exists(sourceFolder)) throw new DirectoryNotFoundException(sourceFolder);
        var root = Path.GetFullPath(sourceFolder);
        RejectLinkedPath(root);

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
        RejectLinkedPath(folder);
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

    private static void RejectLinkedPath(string path)
    {
        for (string? current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Path must not contain a symbolic link or junction.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static async Task WriteMetadataAsync(string path, string text)
    {
        path = Path.GetFullPath(path);
        RejectLinkedPath(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await using (var writer = new StreamWriter(stream))
                await writer.WriteAsync(text);
            RejectLinkedPath(path);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
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
