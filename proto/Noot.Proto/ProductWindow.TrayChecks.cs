#if DEBUG
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using WinRT.Interop;

namespace Noot_Proto;

public sealed partial class ProductWindow
{
    private async Task VerifyTrayAsync()
    {
        var checks = new List<string>(); string? error = null;
        double idleCpuMs = 0; long privateBytes = 0;
        void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add(label); }
        var hwnd = WindowNative.GetWindowHandle(this);
        try
        {
            await Task.Delay(700);
            Check(_tray.Registered, "Real Shell_NotifyIcon registration succeeds");
            Check(AppWindow.IsVisible && !AppWindow.IsShownInSwitchers, "Cold launch shows shell without duplicate taskbar presence");
            var note = CreateNote()!;
            await Task.Delay(500);
            note.SetTestContent("Tray verification\nRestore this note after quit.", "mint", 300, 320);
            await Task.Delay(500);
            var record = note.Record!;
            var originalPosition = note.AppWindow.Position;
            SendMessageForTrayCheck(hwnd, 0x10, IntPtr.Zero, IntPtr.Zero);
            Check(!AppWindow.IsVisible && !_quitting && note.AppWindow.IsVisible && record.DeletedAt == null,
                "WM_CLOSE hides only the shell; resident session and note remain alive");
            void Select() => PostMessageForTrayCheck(hwnd, TrayIcon.CallbackMessage, IntPtr.Zero, new IntPtr(0x10000 | 0x400));
            Select(); await Task.Delay(300);
            Check(AppWindow.IsVisible && _trayOpened, "Version-4 notification callback opens the reused shell");
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
            Check(AppWindow.Position.X >= area.X && AppWindow.Position.Y >= area.Y &&
                AppWindow.Position.X + AppWindow.Size.Width <= area.X + area.Width &&
                AppWindow.Position.Y + AppWindow.Size.Height <= area.Y + area.Height,
                "Native popup placement stays inside the invocation monitor work area");
            Select(); await Task.Delay(200);
            Check(!AppWindow.IsVisible, "Second notification selection toggles the shell closed");
            ShowShell(true); await Task.Delay(150);
            note.FocusEditor(); TrayIcon.SetForegroundWindow(new IntPtr(note.NativeHandle)); await Task.Delay(250);
            Check(!AppWindow.IsVisible && note.AppWindow.IsVisible, "Actual native deactivation dismisses tray shell and preserves note");
            Check(note.AppWindow.Position == originalPosition, "Shell opening and hiding do not move notes");
            var displays = DisplayArea.FindAll();
            for (int displayIndex = 0; displayIndex < displays.Count; displayIndex++)
            {
                var display = displays[displayIndex];
                var work = display.WorkArea;
                if (work.X >= 0 && work.Y >= 0) continue;
                var placed = TrayIcon.PositionNear(new(work.X + work.Width / 2, work.Y + work.Height,
                    work.X + work.Width / 2 + 20, work.Y + work.Height + 20), AppWindow.Size.Width, AppWindow.Size.Height);
                AppWindow.Move(new Windows.Graphics.PointInt32(placed.Left, placed.Top));
                await Task.Delay(100);
                Check(AppWindow.Position.X >= work.X && AppWindow.Position.X + AppWindow.Size.Width <= work.X + work.Width &&
                    AppWindow.Position.Y >= work.Y && AppWindow.Position.Y + AppWindow.Size.Height <= work.Y + work.Height,
                    $"Real shell and native positioning clamp on connected negative-coordinate monitor ({work.X},{work.Y})");
            }
            bool pinned = record.Pinned;
            RouteTrayCommand(TrayCommand.ShowNotes); await Task.Delay(100);
            Check(record.Pinned == pinned && ((OverlappedPresenter)note.AppWindow.Presenter).IsAlwaysOnTop == pinned,
                "Show notes preserves saved and native pinning state");
            RouteTrayCommand(TrayCommand.Settings); await Task.Delay(200);
            Check(AppWindow.IsVisible && ((Grid)_shell.FindName("SettingsPanel")).Visibility == Visibility.Visible,
                "Tray Settings reuses the existing shell settings surface");
            var combo = (ComboBox)_shell.FindName("DefaultColour");
            combo.IsDropDownOpen = true; await Task.Delay(200);
            Check(AppWindow.IsVisible && combo.IsDropDownOpen, "Settings dropdown does not prematurely dismiss its shell");
            combo.IsDropDownOpen = false;
            _shell.ShowHome(); HideShell();
            int count = _windows.Count;
            RouteTrayCommand(TrayCommand.NewNote); await Task.Delay(300);
            Check(_windows.Count == count + 1, "Tray New note creates a physical editor");
            var disposable = _windows.Values.Last();
            disposable.Discard(); await Task.Delay(100);
            Check(_store.Document.Notes.Any(n => n.DeletedAt != null), "Discard enables native Undo command state");
            RouteTrayCommand(TrayCommand.Undo); await Task.Delay(300);
            Check(disposable.Record!.DeletedAt == null && _windows.ContainsKey(disposable.Record.Id), "Tray Undo uses existing recoverable discard path");
            HideShell();
            PostMessageForTrayCheck(hwnd, RegisterWindowMessageForTrayCheck("TaskbarCreated"), IntPtr.Zero, IntPtr.Zero);
            await Task.Delay(300);
            Check(_tray.Registered && !AppWindow.IsVisible && note.AppWindow.IsVisible,
                "TaskbarCreated re-registers the real icon without recreating notes or showing shell");
            using (var second = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--tray-check") { UseShellExecute = false })!)
            {
                await second.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
                await Task.Delay(300);
                Check(second.ExitCode == 0 && AppWindow.IsVisible && !_trayOpened,
                    "Second executable launch redirects and exits before opening another note session");
            }
            HideShell();
            Check(_hotkey.Registered, "Global Ctrl+Alt+N registration succeeds on the actual desktop");
            count = _windows.Count;
            note.FocusEditor();
            keybd_event(0x11, 0, 0, 0); keybd_event(0x12, 0, 0, 0); keybd_event(0x4e, 0, 0, 0);
            keybd_event(0x4e, 0, 2, 0); keybd_event(0x12, 0, 2, 0); keybd_event(0x11, 0, 2, 0);
            await Task.Delay(500);
            Check(_windows.Count == count + 1 && !AppWindow.IsVisible, "OS-delivered global hotkey creates a note with shell hidden");
            await Task.Delay(1000);
            using var process = Process.GetCurrentProcess();
            var cpu = process.TotalProcessorTime;
            await Task.Delay(10000);
            process.Refresh(); idleCpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds; privateBytes = process.PrivateMemorySize64;
            Check(!_save.IsEnabled, "Save timer is stopped during idle tray residency");
            Check(PrepareQuit() && _store.Document.Notes.All(n => n.DeletedAt == null), "Clean quit saves and closes every active note without discarding");
            _tray.Dispose(); Check(!_tray.Registered, "Clean quit releases the notification registration");
        }
        catch (Exception exception) { error = exception.ToString(); }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "tray-verification.json"), System.Text.Json.JsonSerializer.Serialize(
            new { passed = error == null, checks, error, idleCpuMs, idleSampleSeconds = 10, privateBytes, noteCount = _store.Document.Notes.Count },
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Quit();
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern IntPtr SendMessageForTrayCheck(IntPtr hwnd, uint message, IntPtr w, IntPtr l);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] private static extern bool PostMessageForTrayCheck(IntPtr hwnd, uint message, IntPtr w, IntPtr l);
    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessageForTrayCheck(string name);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);
}
#endif
