using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Scrunch;

internal static class Program
{
    private static readonly object Gate = new();
    private static DispatcherQueue? _dispatcher;
    private static Action? _activate;
    private static bool _pending;

    [STAThread]
    private static int Main(string[] arguments)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        // Verification/bench processes use isolated data and must not activate everyday notes.
        bool isolated = false;
#if DEBUG
        isolated = arguments.Any(a => a is "--verify-product");
        isolated |= arguments.Any(a => a is "--shell-preview" or "--verify-fx" or "--fx-lab");
#endif
        using var installerMutex = new Mutex(false, @"Local\Scrunch.Installer.Resident");
        AppInstance? instance = null;
        try
        {
            if (!isolated)
            {
                string key = AppPaths.InstanceKey;
#if DEBUG
                // Fixed isolated path plus a separate instance key for native restart tests.
                if (arguments.Contains("--tray-check")) key += ".check." + AppContext.BaseDirectory;
#endif
                instance = AppInstance.FindOrRegisterForKey(key);
                if (!instance.IsCurrent)
                {
                    AllowSetForegroundWindow(instance.ProcessId);
                    var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
                    using var completed = new EventWaitHandle(false, EventResetMode.ManualReset);
                    var redirect = Task.Run(async () =>
                    {
                        try { await instance.RedirectActivationToAsync(activation); }
                        finally { completed.Set(); }
                    });
                    // Pump COM on the STA while the MTA performs redirection (Microsoft's pattern).
                    Marshal.ThrowExceptionForHR(CoWaitForMultipleObjects(0, uint.MaxValue, 1,
                        [completed.SafeWaitHandle.DangerousGetHandle()], out _));
                    redirect.GetAwaiter().GetResult();
                    return 0;
                }
                instance.Activated += (_, args) =>
                {
                    // A sign-in launch should not disturb a running session.
                    if (args.Data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launch &&
                        launch.Arguments.Split(' ').Contains("--startup")) return;
                    lock (Gate)
                    {
                        if (_dispatcher == null || _activate == null) _pending = true;
                        else _dispatcher.TryEnqueue(() => _activate?.Invoke());
                    }
                };
            }
            Application.Start(parameters =>
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                _ = new App();
            });
            return 0;
        }
        catch (Exception error)
        {
            MessageBox(IntPtr.Zero, "Scrunch could not start or reach its running instance. Please try again.\n\n" + error.Message, "Scrunch", 0x10);
            return 1;
        }
        finally { if (instance?.IsCurrent == true) instance.UnregisterKey(); }
    }

    internal static void Ready(Action activate)
    {
        lock (Gate)
        {
            _dispatcher = DispatcherQueue.GetForCurrentThread(); _activate = activate;
            if (_pending) { _pending = false; _dispatcher.TryEnqueue(() => _activate?.Invoke()); }
        }
    }

    [DllImport("ole32.dll")] private static extern int CoWaitForMultipleObjects(uint flags, uint milliseconds, uint count, IntPtr[] handles, out uint index);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int MessageBox(IntPtr window, string text, string caption, uint type);
}
