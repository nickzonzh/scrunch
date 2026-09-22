using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.Windows.ApplicationModel.DynamicDependency;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Scrunch;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private ProductWindow? _window;
    // The notebook this process actually opened; isolated check runs must not
    // drop crash reports into the everyday data directory.
    private string? _notebookDirectory;
    
    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        // Unpackaged and self-contained: nothing else records a fatal fault.
        // Never handled — the process must still fail loudly.
        UnhandledException += (_, e) => LogCrash(e.Exception?.ToString(), e.Message);
        // Self-contained: framework binaries ship alongside the exe,
        // no shared-runtime bootstrap or package identity required.
        InitializeComponent();
    }

    private void LogCrash(string? detail, string message)
    {
        try
        {
            string directory = _notebookDirectory ?? AppPaths.DataDirectory;
            System.IO.Directory.CreateDirectory(directory);
            string path = System.IO.Path.Combine(directory, "crash.log");
            // Keep the newest crashes only; an unattended loop must not grow a log forever.
            if (new System.IO.FileInfo(path) is { Exists: true, Length: > 256 * 1024 }) System.IO.File.Delete(path);
            System.IO.File.AppendAllText(path, $"{DateTime.UtcNow:O} {message}\n{detail}\n\n");
        }
        catch { /* Logging a crash must never cause one. */ }
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        var arguments = Environment.GetCommandLineArgs();
        try
        {
            bool isolated = false;
#if DEBUG
            isolated = arguments.Contains("--verify-product");
            isolated |= arguments.Contains("--fx-lab") || arguments.Contains("--verify-fx") || arguments.Contains("--shell-preview");
#endif
            var directory = isolated
                ? System.IO.Path.Combine(AppContext.BaseDirectory, "product-check-" + Guid.NewGuid().ToString("N"))
                : AppPaths.DataDirectory;
#if DEBUG
            // Repeatable native UI/restart checks without touching everyday notes.
            if (arguments.Contains("--shell-preview")) directory = System.IO.Path.Combine(AppContext.BaseDirectory,
                arguments.Contains("--visual-fixture") ? "product-check-shell-visual" : "product-check-shell-preview");
            if (arguments.Contains("--tray-check")) directory = System.IO.Path.Combine(AppContext.BaseDirectory, "product-check-tray");
#endif
            _notebookDirectory = directory;
            _window = new ProductWindow(new NoteStore(directory));
        }
        catch (Exception error)
        {
#if DEBUG
            System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "startup-error.txt"), error.ToString());
#endif
            MessageBox(IntPtr.Zero, "Scrunch could not open your notes. If Scrunch is already running, use its window or Ctrl + Alt + N. Otherwise check access to your local notes folder. Your saved files have not been replaced.\n\n" + error.Message, "Scrunch", 0x10);
            Exit(); return;
        }
        _window.Start(arguments.Contains("--startup"));
        Program.Ready(() => _window.ShowShell(fromTray: false));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int MessageBox(IntPtr window, string text, string caption, uint type);
}
