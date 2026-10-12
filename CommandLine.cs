using System.IO;
using System.Text.Json;
namespace Packbench;

public static class CommandLine
{
    public static int Run(string[] args)
    {
        if (args.Length != 4 || args[0] != "--verify" || args[2] != "--output" || !args[3].EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return 2;
        string? temporary = null;
        try
        {
            var output = Path.GetFullPath(args[3]);
            if (File.Exists(output) || !Directory.Exists(Path.GetDirectoryName(output)) || string.Equals(output, Path.GetFullPath(args[1]), StringComparison.OrdinalIgnoreCase)) return 2;
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var result = Task.Run(() => PackagingEngine.VerifyZipAsync(args[1], timeout.Token)).GetAwaiter().GetResult();
            temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) JsonSerializer.Serialize(stream, result, new JsonSerializerOptions { WriteIndented = true });
            File.Move(temporary, output);
            return result.Valid ? 0 : 3;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or OperationCanceledException or System.Security.SecurityException) { return 1; }
        finally { if (temporary is not null && File.Exists(temporary)) File.Delete(temporary); }
    }
}
