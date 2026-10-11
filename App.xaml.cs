using System.Windows;
using System.Windows.Media;
using System.Windows.Interop;
using System.IO;
namespace Packbench;
public partial class App : System.Windows.Application
{
    public App()
    {
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        DispatcherUnhandledException += (_, e) =>
        {
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "Packbench-crash.log"), $"{DateTime.UtcNow:O}\n{e.Exception}"); } catch { }
            System.Windows.MessageBox.Show("Packbench hit an unexpected error. A diagnostic was saved to your temp folder.", "Packbench", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
    }
}
