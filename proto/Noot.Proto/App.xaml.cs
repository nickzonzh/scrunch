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

namespace Noot_Proto;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private Window? _window;
    
    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        // Self-contained: framework binaries ship alongside the exe,
        // no shared-runtime bootstrap or package identity required.
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            try { System.IO.File.AppendAllText(@"C:\Users\Nick\Projects\noot\proto\run.log", "UNHANDLED: " + e.Exception + "\n"); } catch { }
        };
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        var arguments = Environment.GetCommandLineArgs();
        if (arguments.Contains("--bench") || arguments.Contains("--verify")) _window = new MainWindow();
        else
        {
            try
            {
                bool isolated = arguments.Contains("--verify-product");
#if DEBUG
                isolated |= arguments.Contains("--fx-lab") || arguments.Contains("--verify-fx") || arguments.Contains("--shell-preview");
#endif
                var directory = isolated
                    ? System.IO.Path.Combine(AppContext.BaseDirectory, "product-check-" + Guid.NewGuid().ToString("N"))
                    : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Noot");
#if DEBUG
                // Repeatable native UI/restart checks without touching everyday notes.
                if (arguments.Contains("--shell-preview")) directory = System.IO.Path.Combine(AppContext.BaseDirectory,
                    arguments.Contains("--visual-fixture") ? "product-check-shell-visual" : "product-check-shell-preview");
#endif
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
        }
        _window.AppWindow.Show();
        _window.Activate();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int MessageBox(IntPtr window, string text, string caption, uint type);
}
