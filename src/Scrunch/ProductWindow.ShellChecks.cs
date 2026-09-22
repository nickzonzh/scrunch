using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Scrunch;

public sealed partial class ProductWindow
{
    private async Task VerifyTypographyAsync(Action<bool, string> check, NoteWindow window)
    {
        var view = (NoteView)window.Content;
        var editor = (TextBox)view.FindName("NoteText");
        string text = editor.Text;
        double width = view.PaperWidth, height = view.PaperHeight;
        await view.WarmTextureAsync();
        string previousTexture = view.TextureDigest;
        _shell.ShowSettings();
        ((ComboBox)_shell.FindName("NoteFont")).SelectedIndex = 1;
        ((NumberBox)_shell.FindName("NoteFontSize")).Value = 28;
        await Task.Delay(500);
        check(editor.FontFamily.Source.Contains("InterVariable.ttf") && editor.FontSize == 28 &&
            editor.Text == text && view.PaperWidth == width && view.PaperHeight == height,
            "Settings font and size update an existing editor without changing text or paper geometry");
        check(!_session.Dirty && _session.Document.Typography.Font == "inter" && _session.Document.Typography.Size == 28,
            "Settings typography autosaves independently of new-note defaults");
        var preview = (TextBlock)_shell.FindName("FontPreview");
        check(preview.FontFamily.Source == editor.FontFamily.Source && preview.FontSize == 28,
            "Settings preview matches the note typography");
        await CaptureShellAsync("shell-font-settings", ElementTheme.Light);
        await CaptureElementAsync(view, "note-inter-28");
        await view.WarmTextureAsync();
        check(previousTexture.Length > 0 && previousTexture != view.TextureDigest,
            "Font changes invalidate the motion texture and recapture the new glyph layout");
        var created = CreateNote()!;
        await Task.Delay(350);
        var createdEditor = (TextBox)((NoteView)created.Content).FindName("NoteText");
        check(createdEditor.FontFamily.Source == editor.FontFamily.Source && createdEditor.FontSize == 28,
            "New notes inherit the current global font and size");
        created.Discard();
        await Task.Delay(100);
        UndoDiscard();
        await Task.Delay(350);
        var restoredEditor = (TextBox)((NoteView)_windows[created.Record.Id].Content).FindName("NoteText");
        check(restoredEditor.FontFamily.Source == editor.FontFamily.Source && restoredEditor.FontSize == 28,
            "Undo restores notes using the current global typography");
        _windows[created.Record.Id].Discard();
        ((NumberBox)_shell.FindName("NoteFontSize")).Value = double.NaN;
        check(_session.Document.Typography.Size == 28, "Clearing the size input does not save an invalid font size");
        ((NumberBox)_shell.FindName("NoteFontSize")).Value = 28;
        _shell.ShowHome();
    }

    private async Task VerifyShellAsync(Action<bool, string> check)
    {
        T Control<T>(string name) where T : FrameworkElement => (T)_shell.FindName(name);
        void Invoke(string name) => ((IInvokeProvider)new ButtonAutomationPeer(Control<Button>(name)).GetPattern(PatternInterface.Invoke)).Invoke();
        var list = Control<ListView>("NotesList");
        var search = Control<TextBox>("SearchBox");
        check(Title == "Scrunch" && typeof(App).Assembly.GetName().Name == "Scrunch", "Product window and executable identity use Scrunch");
        check(list.Items.Count == 0 && !Control<Button>("UndoButton").IsEnabled, "Empty shell has no invented notes or enabled undo");
        await CaptureShellAsync("shell-empty", ElementTheme.Light);
        _shell.Configure(_session.Document.Defaults, shortcutRegistered: false);
        await Task.Delay(150);
        var shortcutNotice = Control<Grid>("ShortcutNotice");
        double heightWithNotice = ((FrameworkElement)Content).ActualHeight;
        check(shortcutNotice.Visibility == Visibility.Visible && shortcutNotice.ActualHeight <= 36 && !_notice.IsOpen,
            "Shortcut conflict uses a compact inline notice without a warning banner");
        Invoke("DismissShortcutNotice");
        await Task.Delay(150);
        check(shortcutNotice.Visibility == Visibility.Collapsed && ((FrameworkElement)Content).ActualHeight < heightWithNotice,
            "Dismissing the shortcut notice removes its space from the shell");
        Invoke("SettingsButton");
        await Task.Delay(100);
        check(Control<Grid>("SettingsPanel").Visibility == Visibility.Visible, "Settings button opens the separate native settings view");
        var colour = Control<ComboBox>("DefaultColour");
        colour.SelectedItem = colour.Items.Cast<ComboBoxItem>().Single(i => (string)i.Tag == "lavender");
        Control<ToggleSwitch>("DefaultPinned").IsOn = true;
        Control<ToggleSwitch>("DefaultReducedMotion").IsOn = true;
        await Task.Delay(500);
        check(!_session.Dirty && _session.Document.Defaults.Colour == "lavender" && _session.Document.Defaults.Pinned && _session.Document.Defaults.ReducedMotion, "Native settings controls persist new-note defaults");
        await CaptureShellAsync("shell-settings", ElementTheme.Light);
        var settingsScroll = Control<ScrollViewer>("SettingsScroll");
        settingsScroll.ChangeView(null, settingsScroll.ScrollableHeight, null, disableAnimation: true);
        await CaptureShellAsync("shell-settings-bottom", ElementTheme.Light);
        check(settingsScroll.VerticalOffset > 0 && Control<Button>("DataFolderButton").ActualHeight > 0,
            "Settings scrolls to the data-folder and version controls");
        settingsScroll.ChangeView(null, 0, null, disableAnimation: true);
        Invoke("BackButton");
        Invoke("NewButton");
        await Task.Delay(500);
        var window = _windows.Values.Single();
        var record = window.Record;
        check(record.Colour == "lavender" && record.Pinned && record.ReducedMotion, "New note button applies saved colour, pinning and reduced motion");
        window.SetTestContent("Call the dentist\nBook the annual check-up", "lavender", 300, 320);
        await Task.Delay(500);
        var weekend = CreateNote()!;
        var idea = CreateNote()!;
        await Task.Delay(300);
        weekend.SetTestContent("A little time outside\nFind a new walk for Sunday", "mint", 300, 320);
        idea.SetTestContent("An idea for later\nKeep the first version simple", "yellow", 300, 320);
        await Task.Delay(500);
        await CaptureShellAsync("shell-notes", ElementTheme.Light);
        await CaptureShellAsync("shell-notes-dark", ElementTheme.Dark);
        double scale = _shell.XamlRoot.RasterizationScale;
        check(AppWindow.ClientSize.Width == (int)Math.Round(400 * scale) && _shell.ActualHeight < 360,
            $"Three-note shell is compact: {AppWindow.ClientSize.Width / scale:0} x {_shell.ActualHeight:0} DIP (excluding any notice)");
        var root = (FrameworkElement)Content;
        check(Math.Abs(root.ActualHeight - root.DesiredSize.Height) <= 1,
            "Extended caption is counted once: root fits measured content without an extra title-bar gap");
        check(!ExtendsContentIntoTitleBar || _shell.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point()).Y >= _caption.ActualHeight,
            "Native caption drag area does not overlap the shell actions or search");
        var longList = Enumerable.Range(0, 30).Select(i => new NoteRecord { Text = $"Note {i + 1}\nA second line", Colour = "yellow" }).ToArray();
        _shell.UpdateNotes(longList);
        await Task.Delay(200);
        var listScroll = FindDescendant<ScrollViewer>(list)!;
        check(listScroll.ScrollableHeight > 0 && _shell.ActualHeight <= 560,
            $"Long note lists scroll within the bounded shell (scroll={listScroll.ScrollableHeight:0}, shell={_shell.ActualHeight:0} DIP)");
        await CaptureShellAsync("shell-long-list", ElementTheme.Light);
        _shell.UpdateNotes(_session.Document.Notes);
        await Task.Delay(150);
        check(_shell.ActualHeight < 360, "Shell shrinks back to content after a long list");

        const string typographyText = "A little time outside\nA walk. A call. An idea.\n0123456789 !? @#%\nCafé — 世界 Ελληνικά\nПривет مرحبا 😊 👩🏽‍💻\n\nKeep the first version simple, then leave a little room for tomorrow.";
        foreach (var (width, height) in new[] { (240, 240), (300, 320), (440, 420) })
        {
            window.SetTestContent(typographyText, "lavender", width, height);
            await Task.Delay(450);
            var noteView = (NoteView)window.Content;
            var editor = (TextBox)noteView.FindName("NoteText");
            check(editor.FontFamily.Source.Contains("DrawablyPen.ttf") && editor.Text.Replace("\r\n", "\n").Replace('\r', '\n') == typographyText,
                $"Drawably native editor preserves multiline Unicode, punctuation, digits and emoji at {width}x{height}");
            await CaptureElementAsync(noteView, $"note-typography-{width}x{height}");
            if (width == 240)
            {
                var editorScroll = FindDescendant<ScrollViewer>(editor)!;
                editorScroll.ChangeView(null, editorScroll.ScrollableHeight, null, disableAnimation: true);
                await Task.Delay(150);
                check(editorScroll.VerticalOffset > 0, "Long Unicode content scrolls in the smallest native note editor");
                await CaptureElementAsync(noteView, "note-typography-small-scrolled");
                editorScroll.ChangeView(null, 0, null, disableAnimation: true);
            }
        }
        check(SaveNow(), "Unicode typography sample saves");
        using var savedDocument = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(_session.DirectoryPath, "notes.json")));
        var savedText = savedDocument.RootElement.GetProperty("Notes").EnumerateArray()
            .Single(n => n.GetProperty("Id").GetGuid() == record.Id).GetProperty("Text").GetString();
        check(savedText?.Replace("\r\n", "\n").Replace('\r', '\n') == typographyText, "Unicode editor content is preserved in JSON storage (native line endings)");
        window.SetTestContent("Call the dentist\nBook the annual check-up", "lavender", 300, 320);
        await Task.Delay(450);
        weekend.Discard(); idea.Discard();
        await Task.Delay(100);
        search.Text = "ANNUAL";
        await Task.Delay(100); // Let the text-change and layout events drain.
        check(list.Items.Count == 1 && ((NoteListRow)list.Items[0]).Id == record.Id, "Search matches body text case-insensitively");
        search.Text = "absent phrase";
        await Task.Delay(100);
        check(list.Items.Count == 0 && Control<TextBlock>("EmptyLabel").Visibility == Visibility.Visible,
            $"Unmatched search shows a quiet empty result (text={search.Text}, rows={list.Items.Count}, home={Control<Grid>("HomePanel").Visibility})");
        await CaptureShellAsync("shell-search-empty", ElementTheme.Light);
        search.Text = "  ";
        await Task.Delay(100);
        check(list.Items.Count == 1, "Clearing search restores the note list");
        ShowHome();
        bool requested = false;
        void Selected(Guid id) => requested = id == record.Id;
        _shell.NoteRequested += Selected;
        try
        {
            list.UpdateLayout();
            var peer = new ListViewAutomationPeer(list);
            var invoke = peer.GetChildren().Select(p => p.GetPattern(PatternInterface.Invoke)).OfType<IInvokeProvider>().First();
            invoke.Invoke();
            await Task.Delay(100);
            check(requested && _windows[record.Id] == window && _windows.Count == 1,
                "Native list-item invocation activates the same note without creating a duplicate window");
        }
        finally { _shell.NoteRequested -= Selected; }
        window.Discard();
        await Task.Delay(100);
        check(list.Items.Count == 0 && Control<Button>("UndoButton").IsEnabled, "Discard updates list and enables shell undo");
        Invoke("UndoButton");
        await Task.Delay(300);
        check(record.DeletedAt == null && list.Items.Count == 1, "Shell undo button restores the discarded note");
        _windows[record.Id].Discard();
        colour.SelectedIndex = 0;
        Control<ToggleSwitch>("DefaultPinned").IsOn = false;
        Control<ToggleSwitch>("DefaultReducedMotion").IsOn = false;
        search.Text = "";
        check(SaveNow(), "Settings and shell test notes save successfully");
    }

    private async Task CaptureShellAsync(string name, ElementTheme theme)
    {
        // Native XAML pixels only. Acrylic and the native frame require desktop capture.
        // https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.media.imaging.rendertargetbitmap.getpixelsasync
        var root = (FrameworkElement)Content;
        var originalTheme = root.RequestedTheme;
        var surface = (Grid)_shell.Content;
        var background = surface.Background;
        try
        {
            root.RequestedTheme = theme;
            surface.Background = new SolidColorBrush(theme == ElementTheme.Dark
                ? Windows.UI.Color.FromArgb(255, 32, 32, 32) : Windows.UI.Color.FromArgb(255, 243, 243, 243));
            _shell.UpdateLayout();
            await Task.Delay(150);
            await CaptureElementAsync(_shell, name);
        }
        finally { surface.Background = background; root.RequestedTheme = originalTheme; }
    }

    private static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            if (FindDescendant<T>(child) is T nested) return nested;
        }
        return null;
    }

    private static async Task CaptureElementAsync(UIElement element, string name)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        var buffer = await bitmap.GetPixelsAsync();
        var pixels = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(buffer);
        if (bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0 || pixels.Length == 0)
            throw new InvalidOperationException("Native shell capture was empty");
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels);
        await encoder.FlushAsync();
        stream.Seek(0);
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);
        var png = new byte[(int)stream.Size]; reader.ReadBytes(png);
        File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, name + ".png"), png);
    }

    // Debug-only lab and preview surfaces, dispatched once from the constructor.
    partial void ConfigureCheckSurfaces(Grid root)
    {
        var arguments = Environment.GetCommandLineArgs();
        if (arguments.Contains("--shell-preview"))
        {
            root.RequestedTheme = arguments.Contains("--dark") ? ElementTheme.Dark : ElementTheme.Light;
            Activated += async (_, _) =>
            {
                await Task.Delay(150);
                if (!_quitting && SystemBackdrop is ShellBackdrop) WriteShellDiagnostics();
            };
        }
        if (arguments.Contains("--fx-lab")) BuildFxLab(root);
    }

    private void WriteShellDiagnostics() =>
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "shell-backdrop.json"),
            System.Text.Json.JsonSerializer.Serialize(ShellDiagnostics,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

    private object ShellDiagnostics => new
    {
        backdrop = ((ShellBackdrop)SystemBackdrop).Diagnostics,
        extendedCaption = ExtendsContentIntoTitleBar,
        captionHeight = _caption.ActualHeight,
        shellHeight = _shell.ActualHeight,
        rootHeight = ((FrameworkElement)Content).ActualHeight,
        outerHeight = AppWindow.Size.Height,
        clientHeight = AppWindow.ClientSize.Height,
        shortcutRegistered = _hotkey.Registered
    };

    // async void on purpose: a verification surface that throws must fail the
    // process loudly (and land in crash.log) exactly as the awaiting loaded
    // handler did, never vanish into an unobserved task.
    partial void RunChecks() => RunChecksAsync();

    private async void RunChecksAsync()
    {
        var arguments = Environment.GetCommandLineArgs();
        if (arguments.Contains("--verify-product")) await VerifyAsync();
        if (arguments.Contains("--verify-tray")) await VerifyTrayAsync();
        if (arguments.Contains("--verify-fx")) await VerifyFxAsync();
        if (arguments.Contains("--shell-preview"))
        {
            await Task.Delay(600);
            WriteShellDiagnostics();
        }
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
            var record = window.Record;
            Check(record.Text.Contains("A walk") && record.Colour == "mint" && record.Width == 360 && record.Height == 280, "Native changes update the persistent record");
            Check(record.X == 240 && record.Y == 160, "Physical window placement captured");
            Check(!_session.Dirty, "Autosave drains after edits");
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
            await VerifyTypographyAsync(Check, menuNote);
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
