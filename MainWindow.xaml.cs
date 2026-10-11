using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.IO.Compression;
using System.Net.Http;
using Microsoft.Win32;
using System.Windows;
using Forms = System.Windows.Forms;

namespace Packbench;
public partial class MainWindow : Window
{
    static string AppVersion => typeof(MainWindow).Assembly.GetName().Version!.ToString(3);
    string? root; string? csproj; string? lastOutput; bool loadingSettings; Process? activeProcess;
    static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "GITHUB", "openpackager-settings.json");
    public MainWindow() { InitializeComponent(); LoadSettings(); }
    void Theme_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) { if (loadingSettings || Theme is null || AccentSlider is null) return; if (Theme.SelectedItem is System.Windows.Controls.ComboBoxItem item) { var theme = item.Content?.ToString() ?? "Light"; ApplyTheme(theme); SaveSettings(theme, AccentSlider.Value, root); } }
    void AccentSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (AccentValue is null || loadingSettings || Theme is null) return; AccentValue.Text = $"{(int)e.NewValue}Â°"; ApplyAccent(e.NewValue); if (Theme.SelectedItem is System.Windows.Controls.ComboBoxItem item) SaveSettings(item.Content?.ToString() ?? "Light", e.NewValue, root); }
    void LoadSettings() { loadingSettings = true; var theme = "Light"; var accent = 220d; string? lastProject = null; try { if (File.Exists(SettingsPath)) { var saved = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(SettingsPath)); theme = saved?.Theme ?? theme; accent = saved?.AccentHue ?? accent; lastProject = saved?.LastProject; } } catch { Log("Settings could not be read; defaults used"); } Theme.SelectedIndex = theme switch { "Dark" => 1, "System" => 2, _ => 0 }; AccentSlider.Value = Math.Clamp(accent, 0, 360); loadingSettings = false; ApplyTheme(theme); ApplyAccent(accent); if (!string.IsNullOrWhiteSpace(lastProject) && Directory.Exists(lastProject)) { root = lastProject; ProjectPath.Text = lastProject; Scan(); } }
    void SaveSettings(string theme, double accent, string? lastProject) { try { Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!); File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new UserSettings(theme, accent, lastProject), new JsonSerializerOptions { WriteIndented = true })); } catch { Log("Settings could not be saved"); } }
    void ApplyTheme(string theme) { var dark = theme == "Dark" || (theme == "System" && IsWindowsDarkMode()); var bg = dark ? "#171A20" : "#F3F5F8"; var panel = dark ? "#222832" : "#FFFFFF"; var alt = dark ? "#2B3440" : "#E9EEF5"; var border = dark ? "#3A4655" : "#D4DCE7"; var text = dark ? "#F1F4F8" : "#172131"; var muted = dark ? "#AAB5C4" : "#65748A"; SetBrush("Background", bg); SetBrush("Panel", panel); SetBrush("PanelAlt", alt); SetBrush("Border", border); SetBrush("Text", text); SetBrush("Muted", muted); }
    static bool IsWindowsDarkMode() { try { return Convert.ToInt32(Registry.GetValue("HKEY_CURRENT_USER\\Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize", "AppsUseLightTheme", 1)) == 0; } catch { return false; } }
    void ApplyAccent(double hue) { var brush = new System.Windows.Media.SolidColorBrush(Hsv(hue, .72, .82)); System.Windows.Application.Current.Resources["Accent"] = brush; if (BuildRelease is not null) BuildRelease.Background = brush; }
    static void SetBrush(string key, string value) { var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value); System.Windows.Application.Current.Resources[key] = new System.Windows.Media.SolidColorBrush(color); }
    static System.Windows.Media.SolidColorBrush Brush(string value) => new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value));
    static System.Windows.Media.Color Hsv(double h, double s, double v) { var c=v*s; var x=c*(1-Math.Abs((h/60%2)-1)); var m=v-c; double r=0,g=0,b=0; if(h<60){r=c;g=x;} else if(h<120){r=x;g=c;} else if(h<180){g=c;b=x;} else if(h<240){g=x;b=c;} else if(h<300){r=x;b=c;} else {r=c;b=x;} return System.Windows.Media.Color.FromRgb((byte)((r+m)*255),(byte)((g+m)*255),(byte)((b+m)*255)); }
    static string LogPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "GITHUB", "Packbench.log");
    static void Log(string message) { try { Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!); File.AppendAllText(LogPath, $"{DateTime.UtcNow:O}  {message}{Environment.NewLine}"); } catch { } }
    void ViewLog_Click(object sender, RoutedEventArgs e) { Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!); if (!File.Exists(LogPath)) File.WriteAllText(LogPath, $"{DateTime.UtcNow:O}  No diagnostic events recorded yet.{Environment.NewLine}"); Process.Start(new ProcessStartInfo("notepad.exe", $"\"{LogPath}\"") { UseShellExecute = true }); }
    async void CheckUpdates_Click(object sender, RoutedEventArgs e) { try { using var client = new HttpClient(); client.DefaultRequestHeaders.UserAgent.ParseAdd("Packbench/" + AppVersion); var json = await client.GetStringAsync("https://api.github.com/repos/informent/Packbench/releases/latest"); using var doc = JsonDocument.Parse(json); var latest = doc.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v') ?? AppVersion; Log("Update check completed: latest=" + latest); System.Windows.MessageBox.Show((Version.TryParse(latest, out var latestVersion) && latestVersion > Version.Parse(AppVersion)) ? $"Update {latest} is available on GitHub." : $"You are running the latest release ({AppVersion}).", "Packbench", MessageBoxButton.OK, MessageBoxImage.Information); } catch (Exception ex) { Log("Update check failed: " + ex.GetType().Name); System.Windows.MessageBox.Show("Could not check GitHub right now. You can check the Releases page manually.", "Packbench", MessageBoxButton.OK, MessageBoxImage.Warning); } }
    void ChooseFolder_Click(object sender, RoutedEventArgs e) { using var d = new Forms.FolderBrowserDialog { Description = "Choose the project folder to package" }; if (d.ShowDialog() == Forms.DialogResult.OK) { root = d.SelectedPath; ProjectPath.Text = root; Log("Folder selected: " + root); Scan(); if (Theme?.SelectedItem is System.Windows.Controls.ComboBoxItem item) SaveSettings(item.Content?.ToString() ?? "Light", AccentSlider.Value, root); } }
    void Scan_Click(object sender, RoutedEventArgs e) => Scan();
    void Scan() { if (string.IsNullOrWhiteSpace(ProjectPath.Text) || !Directory.Exists(ProjectPath.Text)) { HealthText.Text = "Choose a valid project folder first."; Log("Scan rejected: invalid folder"); return; } root = ProjectPath.Text; csproj = Directory.EnumerateFiles(root, "*.csproj").FirstOrDefault(); var py = File.Exists(Path.Combine(root, "pyproject.toml")); var node = File.Exists(Path.Combine(root, "package.json")); var kind = csproj is not null ? ".NET project" : py ? "Python project" : node ? "Node project" : "Unknown project"; ProjectType.Text = kind; HealthText.Text = kind == "Unknown project" ? "No supported project marker was found." : "Ready to package. The appropriate local adapter will be used."; Activity.Text = $"Scanned {DateTime.Now:T}\nType: {kind}\nSource remains local."; Log($"Scan completed: {kind}"); }
    void OpenOutput_Click(object sender, RoutedEventArgs e) { if (lastOutput is not null && Directory.Exists(lastOutput)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{lastOutput}\"") { UseShellExecute = true }); }
    async void VerifyPackage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Verify an Packbench release", Filter = "ZIP packages (*.zip)|*.zip" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            Activity.Text = "Verifying manifest, paths, coverage, and SHA-256 hashes...";
            var result = await PackagingEngine.VerifyZipAsync(dialog.FileName);
            HealthText.Text = result.Valid ? "Package verified." : "Package verification failed.";
            Activity.Text = result.Valid ? $"Verified {result.VerifiedFiles:N0} packaged files.\nNo unsafe paths, duplicates, missing coverage, or hash mismatches were found." : string.Join("\n", result.Errors.Take(30));
            Log($"Package verification: valid={result.Valid}, files={result.VerifiedFiles}, errors={result.Errors.Count}");
        }
        catch (Exception ex) { HealthText.Text = "Package verification failed."; Activity.Text = ex.Message; Log("Package verification failed: " + ex.GetType().Name); }
    }
    void CancelBuild_Click(object sender, RoutedEventArgs e) { if (activeProcess is { HasExited: false }) { Log("Build cancellation requested"); activeProcess.Kill(true); } }
    async void Build_Click(object sender, RoutedEventArgs e)
    {
        if (!BuildRelease.IsEnabled) return;
        BuildRelease.IsEnabled = false;
        try
        {
        root = null; csproj = null;
        Scan();
        if (root is null) return;
        if (csproj is null && !File.Exists(Path.Combine(root, "package.json")) && !File.Exists(Path.Combine(root, "pyproject.toml")))
        { Activity.Text = "Choose a supported .NET, Python, or Node project."; return; }
        if (csproj is null) { await PackageSourceProject(root); return; }
        var github = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "GITHUB");
        Directory.CreateDirectory(github);
        var next = Directory.EnumerateDirectories(github).Select(Path.GetFileName).Where(n => n is not null && int.TryParse(n, out _)).Select(n => int.Parse(n!)).DefaultIfEmpty(0).Max() + 1;
        var folder = Path.Combine(github, next.ToString("D3"));
        Directory.CreateDirectory(folder);
        lastOutput = folder; OpenOutput.IsEnabled = false; BuildProgress.Visibility = Visibility.Visible; BuildProgress.IsIndeterminate = true; Activity.Text = $"Building update {next:D3}...\nThis can take a moment.";
        var runtime = ((System.Windows.Controls.ComboBoxItem)Runtime.SelectedItem)?.Tag?.ToString() ?? "win-x64";
        Log($"Build started: runtime={runtime}, selfContained={SelfContained.IsChecked == true}, singleFile={SingleFile.IsChecked == true}");
        var psi = new ProcessStartInfo("dotnet", $"publish \"{csproj}\" -c Release -r {runtime} --self-contained {SelfContained.IsChecked == true} -o \"{folder}\" -p:PublishSingleFile={SingleFile.IsChecked == true} -p:DebugType=None") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        activeProcess = Process.Start(psi); CancelBuild.IsEnabled = activeProcess is not null;
        if (activeProcess is null) { BuildProgress.IsIndeterminate = false; BuildProgress.Visibility = Visibility.Collapsed; Activity.Text = "Could not start the .NET publisher."; return; }
        activeProcess.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) { Log("publisher: " + e.Data); Dispatcher.Invoke(() => Activity.Text = e.Data); } };
        activeProcess.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) { Log("publisher-error: " + e.Data); Dispatcher.Invoke(() => Activity.Text = e.Data); } };
        activeProcess.BeginOutputReadLine(); activeProcess.BeginErrorReadLine();
        await activeProcess.WaitForExitAsync(); using var p = activeProcess; activeProcess = null; CancelBuild.IsEnabled = false;
        if (p.ExitCode < 0) { BuildProgress.IsIndeterminate = false; BuildProgress.Visibility = Visibility.Collapsed; Activity.Text = "Build cancelled."; return; }
        if (p.ExitCode != 0) { BuildProgress.IsIndeterminate = false; BuildProgress.Visibility = Visibility.Collapsed; Log("Build failed: exit=" + p.ExitCode); Activity.Text = "Build failed. Review diagnostics for publisher output."; return; }
        var exe = Directory.EnumerateFiles(folder, "*.exe").FirstOrDefault();
        if (exe is null) { BuildProgress.IsIndeterminate = false; BuildProgress.Visibility = Visibility.Collapsed; Activity.Text = "Build finished, but no executable was produced."; return; }
        using var executableStream = File.OpenRead(exe);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(executableStream)).ToLowerInvariant();
        var manifest = new { product = "Packbench", update = next, createdUtc = DateTime.UtcNow, project = Path.GetFileNameWithoutExtension(csproj), runtime, selfContained = SelfContained.IsChecked == true, singleFile = SingleFile.IsChecked == true, executable = Path.GetFileName(exe), sha256 = hash };
        var zip = await PackagingEngine.CompleteReleaseAsync(folder, manifest);
        Log($"Build completed: update={next:D3}, output={folder}, package={zip}");
        BuildProgress.IsIndeterminate = false; BuildProgress.Value = 100; OpenOutput.IsEnabled = true; Activity.Text = $"Release complete.\n\nUpdate: {next:D3}\nSaved to:\n{folder}\nPackage:\n{zip}\n\n{Path.GetFileName(exe)}";
        HealthText.Text = "Release ready to run.";
        }
        catch (Exception ex)
        {
            Log("Build failed: " + ex.GetType().Name);
            Activity.Text = "Release failed: " + ex.Message;
            HealthText.Text = "Release was not completed.";
            OpenOutput.IsEnabled = false;
        }
        finally
        {
            activeProcess?.Dispose(); activeProcess = null;
            CancelBuild.IsEnabled = false;
            BuildRelease.IsEnabled = true;
            BuildProgress.IsIndeterminate = false;
            BuildProgress.Visibility = Visibility.Collapsed;
        }
    }
    async Task PackageSourceProject(string source)
    {
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".git", "bin", "obj", "node_modules", ".venv", "venv", "dist", "__pycache__" };
        var sourceFiles = PackagingEngine.EnumerateSourceFiles(source, excluded);
        var github = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "GITHUB"); Directory.CreateDirectory(github);
        var next = Directory.EnumerateDirectories(github).Select(Path.GetFileName).Where(n => n is not null && int.TryParse(n, out _)).Select(n => int.Parse(n!)).DefaultIfEmpty(0).Max() + 1;
        var folder = Path.Combine(github, next.ToString("D3")); Directory.CreateDirectory(folder); lastOutput = folder; OpenOutput.IsEnabled = false; BuildProgress.Visibility = Visibility.Visible; BuildProgress.IsIndeterminate = true; Activity.Text = $"Bundling update {next:D3}...";
        var kind = File.Exists(Path.Combine(source, "package.json")) ? "node" : "python"; var bundle = Path.Combine(folder, Path.GetFileName(source)); Directory.CreateDirectory(bundle);
        foreach (var file in sourceFiles) { var relative = Path.GetRelativePath(source, file); var dest = Path.Combine(bundle, relative); Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(file, dest, true); }
        var files = Directory.EnumerateFiles(bundle, "*", SearchOption.AllDirectories).ToArray(); var manifest = new { product = "Packbench", update = next, createdUtc = DateTime.UtcNow, project = Path.GetFileName(source), adapter = kind, type = "source-bundle", fileCount = files.Length }; var zip = await PackagingEngine.CompleteReleaseAsync(folder, manifest); Log($"Source bundle completed: update={next:D3}, adapter={kind}, output={folder}"); BuildProgress.IsIndeterminate = false; BuildProgress.Value = 100; OpenOutput.IsEnabled = true; Activity.Text = $"Source bundle complete.\n\nAdapter: {kind}\nUpdate: {next:D3}\nPackage:\n{zip}"; HealthText.Text = "Release ready to distribute.";
    }
}
record UserSettings(string Theme, double AccentHue, string? LastProject);
