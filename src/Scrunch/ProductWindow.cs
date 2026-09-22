using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;

namespace Scrunch;

// A small launch/control surface. Notes themselves remain the workspace.
// Composition and routing only: NoteSession owns the notebook and the save
// policy, NoteWindow owns one paper, TrayIcon owns the notification area.
public sealed partial class ProductWindow : Window
{
    private readonly NoteSession _session;
    private readonly Dictionary<Guid, NoteWindow> _windows = new();
    private readonly DispatcherTimer _save = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly ShellView _shell = new();
    private readonly InfoBar _notice = new() { IsClosable = true };
    private readonly Button _undo = new() { Content = "Undo last discard" };
    private readonly NewNoteHotkey _hotkey;
    private readonly TrayIcon _tray;
    private bool _trayOpened;
    private bool _shellActive;
    private long _trayDismissedAt = long.MinValue;
    private bool _started;
    private bool _quitting;
    private bool _fitQueued;
    private readonly Grid _caption = new() { Height = 32, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
    private readonly Windows.UI.ViewManagement.AccessibilitySettings _accessibility = new();

    // Debug-only verification surfaces are implemented in ProductWindow.*Checks.cs,
    // which the Release build never compiles (Scrunch.csproj). Unimplemented
    // partial methods compile away there, so no #if DEBUG is needed around the
    // dispatch itself.
    partial void ConfigureCheckSurfaces(Grid root);
    partial void PlaceNoteForCheck(ref int x, ref int y);
    partial void RunChecks();

    public ProductWindow(NoteStore store)
    {
        _session = new NoteSession(store);
        Title = "Scrunch";
        SystemBackdrop = new ShellBackdrop
        {
#if DEBUG
            // Init-only, so this one flag cannot move into a Debug-only partial.
            ForceFallbackForCheck = Environment.GetCommandLineArgs().Contains("--shell-preview") &&
                Environment.GetCommandLineArgs().Contains("--solid-backdrop")
#endif
        };
        double scale = GetDpiForWindow(WindowNative.GetWindowHandle(this)) / 96.0;
        AppWindow.ResizeClient(new SizeInt32((int)(400 * scale), (int)(240 * scale)));
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        { presenter.IsMaximizable = false; presenter.IsMinimizable = false; presenter.IsResizable = false; }
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Scrunch.ico"));
        _shell.NewRequested += () => CreateNote();
        _shell.NoteRequested += ActivateNote;
        _shell.UndoRequested += UndoDiscard;
        _shell.QuitRequested += Quit;
        _shell.DismissRequested += () => { if (_trayOpened) HideShell(); };
        _shell.DefaultsChanged += defaults => { _session.SetDefaults(defaults); ScheduleSave(); };
        _shell.TypographyChanged += typography =>
        {
            _session.SetTypography(typography);
            foreach (var window in _windows.Values)
                if (!window.IsDiscarding) window.SetTypography(typography);
            ScheduleSave();
        };
        _shell.DataFolderRequested += OpenDataFolder;
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(_caption);
        Grid.SetRow(_shell, 1);
        root.Children.Add(_shell);
        Grid.SetRow(_notice, 2); root.Children.Add(_notice);
        Content = root;
        // Extend the same backdrop under the system caption buttons. The empty
        // strip is solely a native drag/system-menu region; no duplicate brand.
        if (Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(_caption);
        }
        else _caption.Visibility = Visibility.Collapsed;
        _shell.FitRequested += QueueFitShell;
        root.Loaded += (_, _) => QueueFitShell();
        root.ActualThemeChanged += (_, _) => QueueFitShell();
        root.Loaded += (_, _) => UpdateShellFrame(root);
        root.ActualThemeChanged += (_, _) => UpdateShellFrame(root);
        ((ShellBackdrop)SystemBackdrop).ConfigurationChanged += () => UpdateShellFrame(root);
        _notice.SizeChanged += (_, _) => QueueFitShell();
        AppWindow.Changed += (_, e) =>
        {
            if (e.DidPositionChange) { UpdateShellFrame(root); QueueFitShell(); }
        };
        ConfigureCheckSurfaces(root);
        _hotkey = new NewNoteHotkey(WindowNative.GetWindowHandle(this), () => CreateNote());
        _tray = new TrayIcon(WindowNative.GetWindowHandle(this), ToggleTrayShell, RouteTrayCommand,
            () => _session.HasDiscards);
        _tray.RegistrationChanged += UpdateTrayAvailability;
        _tray.MenuClosed += () => { if (_trayOpened && !_shellActive) HideShell(); };
        UpdateTrayAvailability();
        Activated += (_, e) =>
        {
            _shellActive = e.WindowActivationState != WindowActivationState.Deactivated;
            if (e.WindowActivationState != WindowActivationState.Deactivated || !_trayOpened || _tray.MenuOpen || _quitting) return;
            // Explorer takes foreground on mouse-down, before NIN_SELECT arrives on mouse-up.
            // Remember this particular dismissal so the same click cannot reopen the shell.
            if (_tray.IsLeftClickOnIcon()) _trayDismissedAt = Environment.TickCount64;
            HideShell();
        };
        _shell.Configure(_session.Document.Defaults, _hotkey.Registered);
        _shell.ConfigureTypography(_session.Document.Typography);
        _save.Tick += (_, _) => { _save.Stop(); SaveNow(); RefreshCount(); };
        var retry = new Button { Content = "Try saving again" };
        retry.Click += (_, _) => { if (SaveNow()) _notice.IsOpen = false; };
        _notice.ActionButton = retry;
        AppWindow.Closing += (_, e) =>
        {
            if (_quitting) return;
            e.Cancel = true;
            HideShell();
        };
        Closed += (_, _) =>
        {
            // Terminal: stop scheduling work and mute the children's callbacks
            // before anything is released. The notebook lease is dropped only
            // after every note window has gone, so no late capture can reach
            // a disposed store.
            _quitting = true;
            _save.Stop();
            foreach (var window in _windows.Values.ToArray()) window.Close();
            _tray.Dispose(); ScrunchFX.ScrunchFxService.Shared.Dispose(); _hotkey.Dispose(); _session.Dispose();
            Application.Current.Exit();
        };
        RefreshCount();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private void UpdateShellFrame(FrameworkElement root)
    {
        if (_quitting) return;
        var handle = WindowNative.GetWindowHandle(this);
        // Keep native buttons, shadow and border; DWM owns window behaviour.
        int dark = root.ActualTheme == ElementTheme.Dark ? 1 : 0;
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18985))
            DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            int smallRound = 3; // DWMWCP_ROUNDSMALL
            DwmSetWindowAttribute(handle, 33, ref smallRound, sizeof(int));
        }
        if (!ExtendsContentIntoTitleBar) return;
        var caption = AppWindow.TitleBar;
        double scale = GetDpiForWindow(handle) / 96.0;
        _caption.Height = Math.Max(32, caption.Height / scale);
        if (_accessibility.HighContrast)
        {
            // Let Windows supply all caption colours in a contrast theme.
            caption.ButtonBackgroundColor = caption.ButtonInactiveBackgroundColor = null;
            caption.ButtonForegroundColor = caption.ButtonInactiveForegroundColor = null;
            caption.ButtonHoverBackgroundColor = caption.ButtonHoverForegroundColor = null;
            caption.ButtonPressedBackgroundColor = caption.ButtonPressedForegroundColor = null;
            return;
        }
        caption.ButtonBackgroundColor = caption.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        caption.ButtonForegroundColor = dark == 1 ? Microsoft.UI.Colors.White : Windows.UI.Color.FromArgb(255, 32, 32, 32);
        caption.ButtonInactiveForegroundColor = dark == 1 ? Windows.UI.Color.FromArgb(255, 160, 160, 160) : Windows.UI.Color.FromArgb(255, 100, 100, 100);
        caption.ButtonHoverForegroundColor = caption.ButtonPressedForegroundColor = caption.ButtonForegroundColor;
        caption.ButtonHoverBackgroundColor = dark == 1 ? Windows.UI.Color.FromArgb(24, 255, 255, 255) : Windows.UI.Color.FromArgb(16, 0, 0, 0);
        caption.ButtonPressedBackgroundColor = dark == 1 ? Windows.UI.Color.FromArgb(16, 255, 255, 255) : Windows.UI.Color.FromArgb(24, 0, 0, 0);
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    private void QueueFitShell()
    {
        if (_fitQueued || _quitting || Content is not Grid) return;
        _fitQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _fitQueued = false;
            if (_quitting || Content is not Grid root || !root.IsLoaded) return;
            // Measure unconstrained height, with the list/settings caps in XAML.
            // Never derive the next height from the current window's stretched rows.
            root.InvalidateMeasure();
            root.Measure(new Windows.Foundation.Size(400, double.PositiveInfinity));
            double dpiScale = GetDpiForWindow(WindowNative.GetWindowHandle(this)) / 96.0;
            var work = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id,
                Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
            double maxHeight = Math.Min(560, Math.Max(180, work.Height / dpiScale - 64));
            var desired = new SizeInt32((int)Math.Round(400 * dpiScale),
                (int)Math.Ceiling(Math.Clamp(root.DesiredSize.Height, Math.Min(220, maxHeight), maxHeight) * dpiScale));
            if (ExtendsContentIntoTitleBar)
            {
                // ResizeClient counts the old non-client caption on Windows 10,
                // even though XAML now draws there. Size the rendered root plus
                // the measured native frame so the caption is counted only once.
                // XamlRoot.Size is the host viewport; ActualHeight can still be
                // the previous arrange size while a list/theme change settles.
                var frameWidth = AppWindow.Size.Width - (int)Math.Round(root.XamlRoot.Size.Width * dpiScale);
                var frameHeight = AppWindow.Size.Height - (int)Math.Round(root.XamlRoot.Size.Height * dpiScale);
                var outer = new SizeInt32(desired.Width + frameWidth, desired.Height + frameHeight);
                if (AppWindow.Size != outer) AppWindow.Resize(outer);
            }
            else if (AppWindow.ClientSize != desired) AppWindow.ResizeClient(desired);
            if (_trayOpened && AppWindow.IsVisible) PositionShell();
            else if (AppWindow.IsVisible)
            {
                var current = new DesktopRect(AppWindow.Position.X, AppWindow.Position.Y,
                    AppWindow.Position.X + AppWindow.Size.Width, AppWindow.Position.Y + AppWindow.Size.Height);
                var clamped = TrayPlacement.Clamp(current, new(work.X, work.Y, work.X + work.Width, work.Y + work.Height));
                if (current != clamped)
                    AppWindow.MoveAndResize(new RectInt32(clamped.Left, clamped.Top, clamped.Width, clamped.Height));
            }
        });
    }

    private void ActivateNote(Guid id)
    {
        var record = _session.FindLive(id);
        if (record == null) return;
        if (_windows.TryGetValue(id, out var window))
        {
            if (window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter) presenter.Restore();
            window.AppWindow.Show(); window.FocusEditor();
        }
        else Open(record, true);
    }

    private void ShowHome()
    {
        _shell.ShowHome();
        ShowShell(fromTray: false);
    }

    public void Start(bool quiet)
    {
        if (_started) return;
        _started = true;
        // Restore notes even when the shell has never loaded (quiet sign-in launch).
        foreach (var record in _session.LiveNotes()) Open(record, false);
        if (!quiet || !_tray.Registered) ShowShell(fromTray: false);
        OnLoaded(this, new RoutedEventArgs());
    }

    public void ShowShell(bool fromTray)
    {
        if (_quitting) return;
        _trayOpened = fromTray && _tray.Registered;
        _trayDismissedAt = long.MinValue;
        PositionShell();
        AppWindow.Show(); Activate();
        TrayIcon.SetForegroundWindow(WindowNative.GetWindowHandle(this));
        QueueFitShell();
    }

    private void PositionShell()
    {
        var rect = _tray.Position(AppWindow.Size.Width, AppWindow.Size.Height);
        if (AppWindow.Position.X != rect.Left || AppWindow.Position.Y != rect.Top ||
            AppWindow.Size.Width != rect.Width || AppWindow.Size.Height != rect.Height)
            AppWindow.MoveAndResize(new RectInt32(rect.Left, rect.Top, rect.Width, rect.Height));
    }

    private void HideShell()
    {
        if (!_tray.Registered || _quitting) return;
        _trayOpened = false;
        AppWindow.Hide();
    }

    private void ToggleTrayShell()
    {
        if (_trayDismissedAt != long.MinValue && Environment.TickCount64 - _trayDismissedAt < 1000)
        { _trayDismissedAt = long.MinValue; return; }
        if (AppWindow.IsVisible && _trayOpened) HideShell();
        else ShowShell(fromTray: true);
    }

    private void RouteTrayCommand(TrayCommand command)
    {
        if (_quitting) return;
        switch (command)
        {
            case TrayCommand.NewNote: HideShell(); CreateNote(); break;
            case TrayCommand.ShowNotes:
                HideShell();
                foreach (var record in _session.LiveNotes()) ActivateNote(record.Id);
                break;
            case TrayCommand.Undo: HideShell(); UndoDiscard(); break;
            case TrayCommand.Settings: _shell.ShowSettings(); ShowShell(fromTray: true); break;
            case TrayCommand.Quit: Quit(); break;
        }
    }

    private void UpdateTrayAvailability()
    {
        // A working tray replaces the shell's taskbar button. On failure keep it reachable.
        AppWindow.IsShownInSwitchers = !_tray.Registered;
        _shell.SetTrayStatus(_tray.Registered);
        if (!_tray.Registered && _started) ShowShell(fromTray: false);
    }

    private void Quit() { if (!_quitting && !PrepareQuit()) return; _tray.Dispose(); Close(); }

    private void OpenDataFolder()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_session.DirectoryPath) { UseShellExecute = true }); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        { Notice("Could not open the data folder. " + e.Message, InfoBarSeverity.Warning); }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await Task.Yield();
        if (_session.RecoveryMessage != null) Notice(_session.RecoveryMessage, InfoBarSeverity.Warning);
        // The everyday shell reports shortcut conflicts in its compact inline
        // notice; preserve the lab's existing notice when the shell is absent.
        if (!_hotkey.Registered && _session.RecoveryMessage == null && Content is not Grid)
            Notice("Ctrl + Alt + N is in use. New note and Ctrl + N still work in Scrunch.", InfoBarSeverity.Warning);
        RunChecks();
    }

    public NoteWindow? CreateNote()
    {
        var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
        int offset = (_windows.Count % 7) * 26;
        int x = area.X + area.Width / 2 - 230 + offset;
        int y = area.Y + area.Height / 2 - 240 + offset;
        PlaceNoteForCheck(ref x, ref y);
        var record = _session.Create(x, y, out var error);
        Surface(error);
        return record == null ? null : Open(record, true);
    }

    private NoteWindow Open(NoteRecord record, bool focus)
    {
        var window = new NoteWindow(record);
        window.SetTypography(_session.Document.Typography);
        _windows.Add(record.Id, window);
        window.RecordChanged += (_, _) => { if (!_quitting && _session.Apply(record.Id, window.Capture())) ScheduleSave(); };
        window.NewRequested += (_, _) => CreateNote();
        window.UndoRequested += (_, _) => UndoDiscard();
        window.HomeRequested += (_, _) => ShowHome();
        bool Discard()
        {
            if (record.DeletedAt != null) return true;
            _session.Apply(record.Id, window.Capture());
            bool discarded = _session.Discard(record.Id, DateTimeOffset.UtcNow, out var error);
            Surface(error);
            return discarded;
        }
        window.DiscardRequested += async (_, animate) =>
        {
            if (window.IsDiscarding && animate) return;
            if (!Discard()) return;
            RefreshCount();
            if (animate)
            {
                var animation = window.PlayDiscardAsync();
                int version = window.DiscardVersion;
                await animation;
                if (version != window.DiscardVersion) return;
            }
            // Undo or quit can happen while the compositor is finishing.
            if (!_quitting && record.DeletedAt != null && _windows.TryGetValue(record.Id, out var current) && current == window)
                window.Close();
        };
        Windows.Foundation.TypedEventHandler<Microsoft.UI.Windowing.AppWindow, Microsoft.UI.Windowing.AppWindowClosingEventArgs> closing = (_, e) =>
        {
            if (_quitting) return;
            if (!Discard()) e.Cancel = true;
        };
        window.AppWindow.Closing += closing;
        Windows.Foundation.TypedEventHandler<object, WindowEventArgs>? closed = null;
        closed = (_, _) =>
        {
            window.Closed -= closed; window.AppWindow.Closing -= closing; _windows.Remove(record.Id);
            // On quit the shell is tearing down: no list refresh, no capture, no save.
            if (!_quitting) RefreshCount();
        };
        window.Closed += closed;
        window.AppWindow.Show(activateWindow: focus);
        if (focus) window.FocusEditor();
        RefreshCount();
        return window;
    }

    public void UndoDiscard()
    {
        var record = _session.UndoLastDiscard(out var error);
        Surface(error);
        if (record == null) return;
        if (_windows.TryGetValue(record.Id, out var existing))
        {
            existing.CancelDiscard(); existing.SetTypography(_session.Document.Typography); existing.FocusEditor(); RefreshCount();
        }
        else Open(record, true);
        _notice.IsOpen = false;
    }

    private void ScheduleSave()
    {
        // A bounded 350ms batch, not a trailing debounce: continuous typing is saved too.
        if (!_save.IsEnabled) _save.Start();
    }

    private bool SaveNow()
    {
        bool saved = _session.TrySave(out var error);
        Surface(error);
        return saved;
    }

    // The session composes the message; the shell only decides where it lands.
    private void Surface(string? error)
    {
        if (error == null) return;
        Notice(error, InfoBarSeverity.Error);
        ShowShell(fromTray: false);
    }

    private bool PrepareQuit()
    {
        if (_quitting) return true;
        foreach (var (id, window) in _windows) _session.Apply(id, window.Capture());
        if (!SaveNow()) return false;
        _quitting = true;
        _save.Stop();
        foreach (var window in _windows.Values.ToArray()) window.Close();
        return true;
    }

    private void Notice(string message, InfoBarSeverity severity)
    {
        _notice.Message = message; _notice.Severity = severity; _notice.IsOpen = true;
        if (_notice.ActionButton is UIElement button) button.Visibility = severity == InfoBarSeverity.Error ? Visibility.Visible : Visibility.Collapsed;
    }
    private void RefreshCount()
    {
        _shell.UpdateNotes(_session.Document.Notes);
        _undo.IsEnabled = _session.HasDiscards;
    }
}
