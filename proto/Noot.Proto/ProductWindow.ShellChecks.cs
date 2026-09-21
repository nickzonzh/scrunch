using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Noot_Proto;

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
        // Native XAML pixels, independent of desktop occlusion. Mica/title bar are not captured.
        // https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.media.imaging.rendertargetbitmap.getpixelsasync
        var originalTheme = _shell.RequestedTheme;
        var surface = (Grid)_shell.Content;
        var background = surface.Background;
        try
        {
            _shell.RequestedTheme = theme;
            surface.Background = new SolidColorBrush(theme == ElementTheme.Dark
                ? Windows.UI.Color.FromArgb(255, 32, 32, 32) : Windows.UI.Color.FromArgb(255, 243, 243, 243));
            _shell.UpdateLayout();
            await Task.Delay(150);
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(_shell);
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
        finally { surface.Background = background; _shell.RequestedTheme = originalTheme; }
    }
}
