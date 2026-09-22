using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Scrunch;

public sealed partial class ProductWindow
{
    private async Task VerifyShellAsync(Action<bool, string> check)
    {
        T Control<T>(string name) where T : FrameworkElement => (T)_shell.FindName(name);
        void Invoke(string name) => ((IInvokeProvider)new ButtonAutomationPeer(Control<Button>(name)).GetPattern(PatternInterface.Invoke)).Invoke();
        var list = Control<ListView>("NotesList");
        var search = Control<TextBox>("SearchBox");
        check(Title == "Scrunch" && typeof(App).Assembly.GetName().Name == "Scrunch", "Product window and executable identity use Scrunch");
        check(list.Items.Count == 0 && !Control<Button>("UndoButton").IsEnabled, "Empty shell has no invented notes or enabled undo");
        await CaptureShellAsync("shell-empty", ElementTheme.Light);
        _shell.Configure(_store.Document.Defaults, shortcutRegistered: false);
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
        check(!_dirty && _store.Document.Defaults.Colour == "lavender" && _store.Document.Defaults.Pinned && _store.Document.Defaults.ReducedMotion, "Native settings controls persist new-note defaults");
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
        var record = window.Record!;
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
        _shell.UpdateNotes(_store.Document.Notes);
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
        using var savedDocument = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(_store.DirectoryPath, "notes.json")));
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
}
