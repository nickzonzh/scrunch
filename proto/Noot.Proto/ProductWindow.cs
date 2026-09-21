using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;

namespace Noot_Proto;

// A small launch/control surface. Notes themselves remain the workspace.
public sealed partial class ProductWindow : Window
{
    private readonly NoteStore _store;
    private readonly Dictionary<Guid, NoteWindow> _windows = new();
    private readonly DispatcherTimer _save = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly ShellView _shell = new();
    private readonly InfoBar _notice = new() { IsClosable = true };
    private readonly Button _undo = new() { Content = "Undo last discard" };
    private readonly NewNoteHotkey _hotkey;
    private bool _quitting;
    private bool _dirty;

    public ProductWindow(NoteStore store)
    {
        _store = store;
        Title = "Scrunch";
        SystemBackdrop = new MicaBackdrop();
        double scale = GetDpiForWindow(WindowNative.GetWindowHandle(this)) / 96.0;
        AppWindow.ResizeClient(new SizeInt32((int)(420 * scale), (int)(540 * scale)));
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        { presenter.IsMaximizable = false; presenter.IsResizable = false; }
        _shell.NewRequested += () => CreateNote();
        _shell.NoteRequested += ActivateNote;
        _shell.UndoRequested += UndoDiscard;
        _shell.QuitRequested += () => { if (PrepareQuit()) Close(); };
        _shell.DefaultsChanged += defaults => { _store.Document.Defaults = defaults; ScheduleSave(); };
        _shell.DataFolderRequested += OpenDataFolder;
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(_shell);
        Grid.SetRow(_notice, 1); root.Children.Add(_notice);
        Content = root;
#if DEBUG
        if (Environment.GetCommandLineArgs().Contains("--fx-lab"))
        {
            Title = "Scrunch — isolated FX lab";
            root.Children.Remove(_notice);
            var stack = new StackPanel { Spacing = 10, Padding = new Thickness(28) };
            var create = new Button { Content = "New note", HorizontalAlignment = HorizontalAlignment.Stretch };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(create, "NewNote");
            create.Click += (_, _) => CreateNote();
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var show = new Button { Content = "Show notes" };
            show.Click += (_, _) => { foreach (var window in _windows.Values) window.Activate(); };
            _undo.Click += (_, _) => UndoDiscard();
            actions.Children.Add(show); actions.Children.Add(_undo);
            stack.Children.Add(new TextBlock { Text = "NootFX · discard lab", FontSize = 26 });
            stack.Children.Add(new TextBlock { Text = "Isolated test notes · exact seed replay", Opacity = .7 });
            stack.Children.Add(create); stack.Children.Add(actions); stack.Children.Add(_notice);
            AddFxSeedControls(stack);
            var slow = new CheckBox { Content = "Slow NootFX (18 seconds)", IsChecked = true };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(slow, "FxSlow");
            NootFX.NootFxService.SlowPlayback = true;
            slow.Checked += (_, _) => NootFX.NootFxService.SlowPlayback = true;
            slow.Unchecked += (_, _) => NootFX.NootFxService.SlowPlayback = false;
            stack.Children.Add(slow);
            var hold = new CheckBox { Content = "Hold deformation for inspection" };
            var progress = new Slider { Minimum = 0, Maximum = 1, StepFrequency = .05, Value = .5, Header = "Deformation" };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(progress, "FxProgress");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(hold, "FxHold");
            hold.Checked += (_, _) => NootFX.NootFxService.HeldProgress = (float)progress.Value;
            hold.Unchecked += (_, _) => NootFX.NootFxService.HeldProgress = null;
            progress.ValueChanged += (_, _) => { if (hold.IsChecked == true) NootFX.NootFxService.HeldProgress = (float)progress.Value; };
            stack.Children.Add(hold); stack.Children.Add(progress);
            var production = new Button { Content = "Production speed" };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(production, "FxProduction");
            production.Click += (_, _) => { hold.IsChecked = false; slow.IsChecked = false; };
            stack.Children.Add(production);
            var discard = new Button { Content = "Delete latest test note" };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(discard, "FxDelete");
            discard.Click += (_, _) => _windows.Values.LastOrDefault(w => !w.IsDiscarding)?.Discard(animate: true);
            stack.Children.Add(discard);
            var sample = new Button { Content = "Next paper sample" };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(sample, "FxSample");
            sample.Click += async (_, _) => await NextFxSampleAsync();
            stack.Children.Add(sample);
            var corner = new Button { Content = "Move sample to next screen corner" };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(corner, "FxCorner");
            corner.Click += (_, _) => NextFxCorner();
            stack.Children.Add(corner);
            stack.Width = 440; stack.HorizontalAlignment = HorizontalAlignment.Left;
            AppWindow.MoveAndResize(new RectInt32(100, 100, 1240, 840));
            Content = new ScrollViewer { Content = stack, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        }
#endif
        _hotkey = new NewNoteHotkey(WindowNative.GetWindowHandle(this), () => CreateNote());
        _shell.Configure(_store.Document.Defaults, _hotkey.Registered);
        _save.Tick += (_, _) => { _save.Stop(); SaveNow(); RefreshCount(); };
        var retry = new Button { Content = "Try saving again" };
        retry.Click += (_, _) => { if (SaveNow()) _notice.IsOpen = false; };
        _notice.ActionButton = retry;
        AppWindow.Closing += (_, e) =>
        {
            if (!PrepareQuit()) e.Cancel = true;
        };
        Closed += (_, _) => { NootFX.NootFxService.Shared.Dispose(); _hotkey.Dispose(); _store.Dispose(); Application.Current.Exit(); };
        ((FrameworkElement)Content).Loaded += OnLoaded;
        RefreshCount();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private void ActivateNote(Guid id)
    {
        var record = _store.Document.Notes.FirstOrDefault(n => n.Id == id && n.DeletedAt == null);
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
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter) presenter.Restore();
        AppWindow.Show(); Activate();
    }

    private void OpenDataFolder()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_store.DirectoryPath) { UseShellExecute = true }); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        { Notice("Could not open the data folder. " + e.Message, InfoBarSeverity.Warning); }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ((FrameworkElement)Content).Loaded -= OnLoaded;
        await Task.Yield();
        foreach (var record in _store.Document.Notes.Where(n => n.DeletedAt == null).ToArray()) Open(record, false);
        if (_store.RecoveryMessage != null) Notice(_store.RecoveryMessage, InfoBarSeverity.Warning);
        if (!_hotkey.Registered && _store.RecoveryMessage == null)
            Notice("Ctrl + Alt + N is in use. New note and Ctrl + N still work in Scrunch.", InfoBarSeverity.Warning);
        if (Environment.GetCommandLineArgs().Contains("--verify-product")) await VerifyAsync();
#if DEBUG
        if (Environment.GetCommandLineArgs().Contains("--verify-fx")) await VerifyFxAsync();
#endif
    }

    public NoteWindow? CreateNote()
    {
        var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
        int offset = (_windows.Count % 7) * 26;
        var defaults = _store.Document.Defaults;
        var record = new NoteRecord { X = area.X + area.Width / 2 - 230 + offset, Y = area.Y + area.Height / 2 - 240 + offset,
            Colour = defaults.Colour, Pinned = defaults.Pinned, ReducedMotion = defaults.ReducedMotion };
#if DEBUG
        // Put genuine test notes against the lab's quiet backdrop for evidence.
        if (Environment.GetCommandLineArgs().Contains("--fx-lab"))
        { record.X = AppWindow.Position.X + 640; record.Y = AppWindow.Position.Y + 130; }
#endif
        _store.Document.Notes.Add(record); _dirty = true;
        if (!SaveNow()) { _store.Document.Notes.Remove(record); return null; }
        return Open(record, true);
    }

    private NoteWindow Open(NoteRecord record, bool focus)
    {
        var window = new NoteWindow(record: record);
        _windows.Add(record.Id, window);
        window.RecordChanged += (_, _) => ScheduleSave();
        window.NewRequested += (_, _) => CreateNote();
        window.UndoRequested += (_, _) => UndoDiscard();
        window.HomeRequested += (_, _) => ShowHome();
        bool Discard()
        {
            if (record.DeletedAt != null) return true;
            window.CaptureRecord();
            record.DeletedAt = DateTimeOffset.UtcNow; _dirty = true;
            if (!SaveNow()) { record.DeletedAt = null; return false; }
            return true;
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
        closed = (_, _) => { window.Closed -= closed; window.AppWindow.Closing -= closing; _windows.Remove(record.Id); RefreshCount(); };
        window.Closed += closed;
        window.AppWindow.Show();
        window.Activate();
        if (focus) window.FocusEditor();
        RefreshCount();
        return window;
    }

    public void UndoDiscard()
    {
        var record = _store.Document.Notes.Where(n => n.DeletedAt != null).OrderByDescending(n => n.DeletedAt).FirstOrDefault();
        if (record == null) return;
        var previous = record.DeletedAt;
        record.DeletedAt = null; _dirty = true;
        if (!SaveNow()) { record.DeletedAt = previous; return; }
        if (_windows.TryGetValue(record.Id, out var existing))
        {
            existing.CancelDiscard(); existing.FocusEditor(); RefreshCount();
        }
        else Open(record, true);
        _notice.IsOpen = false;
    }

    private void ScheduleSave()
    {
        _dirty = true;
        // A bounded 350ms batch, not a trailing debounce: continuous typing is saved too.
        if (!_save.IsEnabled) _save.Start();
    }

    private bool SaveNow()
    {
        if (!_dirty) return true;
        try { _store.Save(); _dirty = false; return true; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Notice("Your latest changes could not be saved. Keep Scrunch open and check available disk space and access. " + error.Message, InfoBarSeverity.Error);
            Activate();
            return false;
        }
    }

    private bool PrepareQuit()
    {
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
        _shell.UpdateNotes(_store.Document.Notes);
        _undo.IsEnabled = _store.Document.Notes.Any(n => n.DeletedAt != null);
    }

    private async Task VerifyAsync()
    {
        var checks = new List<string>();
        string? error = null;
        void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); checks.Add(label); }
        try
        {
            await VerifyShellAsync(Check);
            var window = CreateNote() ?? throw new InvalidOperationException("Create failed");
            await Task.Delay(800);
            Check(window.EditorLoaded, "Floating native editor loaded");
            window.SetTestContent("Remember the good stuff.\n\nA walk outside.", "mint", 360, 280);
            window.AppWindow.Move(new PointInt32(240, 160));
            await Task.Delay(600);
            var record = window.Record!;
            Check(record.Text.Contains("A walk") && record.Colour == "mint" && record.Width == 360 && record.Height == 280, "Native changes update the persistent record");
            Check(record.X == 240 && record.Y == 160, "Physical window placement captured");
            Check(!_dirty, "Autosave drains after edits");
            Check(((ListView)_shell.FindName("NotesList")).Items.Cast<NoteListRow>().Any(n => n.Id == record.Id && n.Title == "Remember the good stuff."),
                "Shell preview follows native editor changes after autosave: " + string.Join(" | ", ((ListView)_shell.FindName("NotesList")).Items.Cast<NoteListRow>().Select(n => n.Title)));
            window.Discard();
            await Task.Delay(100);
            Check(record.DeletedAt != null && !_windows.ContainsKey(record.Id), "Closing a note persists a recoverable discard");
            Check(!((ListView)_shell.FindName("NotesList")).Items.Cast<NoteListRow>().Any(n => n.Id == record.Id), "Discard removes the note from the searchable list");
            UndoDiscard();
            await Task.Delay(500);
            Check(record.DeletedAt == null && _windows.ContainsKey(record.Id), "Undo restores the same note");
            Check(((ListView)_shell.FindName("NotesList")).Items.Cast<NoteListRow>().Any(n => n.Id == record.Id), "Undo restores the note to the shell list");
            Check(_windows[record.Id].AppWindow.Position.X == 240 && _windows[record.Id].AppWindow.Position.Y == 160, "Reopened note restores placement");
            var restored = _windows[record.Id];
            restored.Discard(animate: true);
            Check(record.DeletedAt != null, "Animated discard commits before visual work begins");
            UndoDiscard();
            Check(_windows[record.Id] == restored && record.DeletedAt == null, "Undo during discard reuses the existing window");
            restored.Discard(animate: true);
            await Task.Delay(1800);
            Check(record.DeletedAt != null && !_windows.ContainsKey(record.Id), "Discard after an interrupted discard closes exactly once");
            UndoDiscard();
            await Task.Delay(500);
            Check(record.DeletedAt == null && _windows.ContainsKey(record.Id), "Completed animated discard remains recoverable");
            var menuNote = _windows[record.Id];
            menuNote.SetTestContent("Fresh writing, then a real menu discard.", "peach", 340, 280);
            await menuNote.InvokeMenuDiscardForCheckAsync();
            await Task.Delay(2000);
            Check(menuNote.LastDiscardOutcome == "Animated" && menuNote.LastDiscardFrames > 0 && !_windows.ContainsKey(record.Id),
                $"Native menu discard renders frames after fresh editing ({menuNote.LastDiscardFrames} frames; {menuNote.LastDiscardOutcome})");
            UndoDiscard();
            await Task.Delay(500);
            Check(!_windows[record.Id].HasNativeFrame, "Floating note has no native caption, resize frame or extended edge styles");
            if (Environment.GetCommandLineArgs().Contains("--inspect-note"))
            {
                _windows[record.Id].PinForInspection();
                Check(!_windows[record.Id].HasNativeFrame, "Pinning preserves frameless window styles");
            }
            Check(SaveNow(), "Final save succeeds");
            checks.Add(_hotkey.Registered ? "Global shortcut registered (physical key delivery not tested)" : "Shortcut conflict fallback shown");
            if (Environment.GetCommandLineArgs().Contains("--inspect-material"))
            {
                var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
                Title = "Scrunch material review — temporary";
                Content = new Microsoft.UI.Xaml.Controls.Grid { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 233, 229, 220)) };
                AppWindow.MoveAndResize(new RectInt32(area.X, area.Y, Math.Min(1300, area.Width), Math.Min(1300, area.Height)));
                if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter inspectionPresenter) inspectionPresenter.IsAlwaysOnTop = true;
                for (int i = 0; i < 4; i++)
                {
                    var sample = i == 0 ? _windows[record.Id] : CreateNote()!;
                    sample.SetTestContent("Leave a little room\nfor the good stuff.\n\nA walk. A call. An idea.", "yellow", i == 0 ? 440 : 300, i == 0 ? 440 : 320);
                    sample.AppWindow.Move(new PointInt32(area.X + 30 + (i % 2) * 640, area.Y + 30 + (i / 2) * 620));
                    sample.PinForInspection();
                    await Task.Delay(200);
                    await sample.InspectMaterialAsync(i == 1 ? 0.35f : i == 2 ? 0.7f : 1, lift: i == 0);
                }
                await Task.Delay(600);
            }
            if (!Environment.GetCommandLineArgs().Contains("--inspect-note") && !Environment.GetCommandLineArgs().Contains("--inspect-material"))
                Check(PrepareQuit() && record.DeletedAt == null, "Quitting closes windows without discarding saved notes");
        }
        catch (Exception e) { error = e.ToString(); }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "product-verification.json"), System.Text.Json.JsonSerializer.Serialize(new { passed = error == null, checks, error, windows = _windows.Values.Select(w => new { handle = w.NativeHandle, x = w.AppWindow.Position.X, y = w.AppWindow.Position.Y }) }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Notice(error == null ? "Product checks passed. This session uses isolated test notes." : "Product check failed: " + error, error == null ? InfoBarSeverity.Success : InfoBarSeverity.Error);
        if (!Environment.GetCommandLineArgs().Contains("--inspect-note") && !Environment.GetCommandLineArgs().Contains("--inspect-material"))
        {
            var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer((Button)_shell.FindName("QuitButton"));
            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
        }
    }
}
