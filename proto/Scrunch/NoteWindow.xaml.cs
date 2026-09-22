using System;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using Windows.Graphics;
using WinRT.Interop;

namespace Scrunch;

// One floating sticky note: borderless, transparent surroundings, pinned
// above other windows, hidden from taskbar and Alt+Tab. Dragging the paper
// moves this OS window; tilt/spring/shadow stay inside NoteView.
//
// Sizing source of truth is the shadow's explicit size (LayoutShadow), never
// measured DesiredSize: measurement clamps to the window and feeds back into
// a shrink loop.
public sealed partial class NoteWindow : Window
{


    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_FRAMECHANGED = 0x0020;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS pMarInset);

    private readonly AppWindow _appWin;
    private readonly IntPtr _hwnd;
    private readonly NativeTransparency _transparency;
    private PointInt32 _dragWinPos;
    public NoteRecord? Record { get; private set; }
    internal bool EditorLoaded => Note.IsLoaded;
    private bool _recordReady;
    public event EventHandler? RecordChanged;
    public event EventHandler? NewRequested;
    public event EventHandler? UndoRequested;
    public event EventHandler? HomeRequested;
    public event EventHandler<bool>? DiscardRequested;
    private bool _closed;
    private CancellationTokenSource? _fxCancellation;
    private XamlRoot? _observedRoot;
    private readonly List<Action> _detachNativeEvents = new();
    public bool IsDiscarding { get; private set; }
    public int DiscardVersion { get; private set; }
    public int LastDiscardFrames { get; private set; }
    public string LastDiscardOutcome { get; private set; } = "Not started";
    internal long NativeHandle => _hwnd.ToInt64();
#if DEBUG
    internal double RasterScaleForCheck => Note.XamlRoot.RasterizationScale;
    internal WeakReference<NoteView> ViewReferenceForCheck => new(Note);
#endif
    internal bool HasNativeFrame => (GetWindowLong(_hwnd, -16) & 0x00C40000) != 0 || (GetWindowLong(_hwnd, -20) & 0x00020301) != 0;
    internal void PinForInspection()
    {
        if (_appWin.Presenter is OverlappedPresenter presenter) presenter.IsAlwaysOnTop = true;
        _appWin.Show(); Activate();
    }
    internal Task InspectMaterialAsync(float amount, bool lift = false) => Note.InspectMaterialAsync(amount, lift);
    private MenuFlyout? _menu;
    private MenuFlyoutItem? _discardItem;
    private bool _discardAfterMenu;

    public NoteWindow(int feel = 1, string colour = "yellow", string text = "", bool reducedMotion = false, NoteRecord? record = null)
    {
        InitializeComponent();
        Record = record;
        SystemBackdrop = new TransparentBackdrop();

        _hwnd = WindowNative.GetWindowHandle(this);
        _appWin = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(_hwnd));
        Note.SetFeel(record?.Feel ?? feel);
        Note.SetColour(record?.Colour ?? colour);
        Note.Text = record?.Text ?? text;
        Note.SetReducedMotion(record?.ReducedMotion ?? reducedMotion);
        if (record != null)
        {
            Note.AnimateOnLoad = false;
            Note.SetPaperSize(record.Width, record.Height);
        }

        // Borderless, no frame resize, pinned above other windows.
        _appWin.SetPresenter(AppWindowPresenterKind.Overlapped);
        if (_appWin.Presenter is OverlappedPresenter pres)
        {
            pres.SetBorderAndTitleBar(false, false);
            pres.IsResizable = false;
            pres.IsMinimizable = false;
            pres.IsMaximizable = false;
            pres.IsAlwaysOnTop = record?.Pinned ?? true;
        }

        // Hide from taskbar and Alt+Tab.
        var ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
        SetWindowLong(_hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW);
        SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);

        _transparency = new NativeTransparency(_hwnd);
        // WinUI Window.Close does not reliably raise UserControl.Unloaded.
        // Explicitly release subscriptions/composition resources on window close.
        Windows.Foundation.TypedEventHandler<object, WindowEventArgs>? closed = null;
        closed = (_, _) =>
        {
            Closed -= closed;
            _closed = true; _fxCancellation?.Cancel();
            // Closed is terminal, but DWM may still present this HWND while
            // backdrop/content teardown runs. Hide first to avoid a white
            // rectangle flashing after the discarded paper has faded away.
            _appWin.Hide();
            Activated -= FirstActivated; Note.Loaded -= FocusWhenLoaded;
            foreach (var detach in _detachNativeEvents) detach();
            _detachNativeEvents.Clear(); Note.KeyboardAccelerators.Clear();
            if (_observedRoot != null) { _observedRoot.Changed -= RootChanged; _observedRoot = null; }
            _appWin.Changed -= AppWindowChanged;
            Note.ReleaseForClose();
            _menu?.Items.Clear(); _menu = null; _discardItem = null;
            RecordChanged = null; NewRequested = null; UndoRequested = null;
            HomeRequested = null; DiscardRequested = null;
            _transparency.Dispose(); SystemBackdrop = null; Content = null;
        };
        Closed += closed;

        SetWindowPos(_hwnd, IntPtr.Zero, 640, 320, 430, 450, SWP_NOZORDER);

        Note.WindowDragStarted += (_, _) => _dragWinPos = _appWin.Position;
        Note.WindowDragDelta += (_, d) => _appWin.Move(new PointInt32(
            _dragWinPos.X + (int)d.X,
            _dragWinPos.Y + (int)d.Y));
        Note.ContentSizeChanged += (_, _) => FitWindowToContent();
        RoutedEventHandler loaded = (_, _) => { _observedRoot = Note.XamlRoot; _observedRoot.Changed += RootChanged; FitWindowToContent(); };
        Note.Loaded += loaded; _detachNativeEvents.Add(() => Note.Loaded -= loaded);
        var menu = new MenuFlyout();
        _menu = menu;
        EventHandler<object> opened = async (_, _) => await Note.WarmTextureAsync();
        menu.Opened += opened; _detachNativeEvents.Add(() => menu.Opened -= opened);
        EventHandler<object> menuClosed = (_, _) =>
        {
            if (!_discardAfterMenu) return;
            _discardAfterMenu = false;
            // Let the flyout finish restoring focus before hiding the editor.
            DispatcherQueue.TryEnqueue(() => Discard(animate: true));
        };
        menu.Closed += menuClosed; _detachNativeEvents.Add(() => menu.Closed -= menuClosed);
        if (record != null)
        {
            var create = new MenuFlyoutItem { Text = "New note", KeyboardAcceleratorTextOverride = "Ctrl+N" };
            OnClick(create, (_, _) => NewRequested?.Invoke(this, EventArgs.Empty));
            menu.Items.Add(create);
            var colours = new MenuFlyoutSubItem { Text = "Paper colour" };
            foreach (var key in PaperTokens.Colours.Keys)
            {
                var swatch = new MenuFlyoutItem { Text = char.ToUpperInvariant(key[0]) + key.Substring(1) };
                OnClick(swatch, (_, _) => Note.SetColour(key));
                colours.Items.Add(swatch);
            }
            menu.Items.Add(colours);
        }
        for (int i = 0; i < PaperFeel.All.Length; i++)
        {
            int index = i;
            var item = new MenuFlyoutItem { Text = PaperFeel.All[i].Name };
            OnClick(item, (_, _) => Note.SetFeel(index));
            menu.Items.Add(item);
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        var peel = new MenuFlyoutItem { Text = "Peel again" };
        OnClick(peel, async (_, _) => await Note.PeelAsync());
        menu.Items.Add(peel);
        var pin = new ToggleMenuFlyoutItem { Text = "Keep above other windows", IsChecked = record?.Pinned ?? true };
        OnClick(pin, (_, _) => { if (_appWin.Presenter is OverlappedPresenter p) p.IsAlwaysOnTop = pin.IsChecked; CaptureRecord(); });
        menu.Items.Add(pin);
        if (record != null)
        {
            var motion = new ToggleMenuFlyoutItem { Text = "Reduce motion", IsChecked = record.ReducedMotion };
            OnClick(motion, (_, _) => Note.SetReducedMotion(motion.IsChecked));
            menu.Items.Add(motion);
            var undo = new MenuFlyoutItem { Text = "Undo last discard", KeyboardAcceleratorTextOverride = "Ctrl+Shift+Z" };
            OnClick(undo, (_, _) => UndoRequested?.Invoke(this, EventArgs.Empty));
            menu.Items.Add(undo);
            var home = new MenuFlyoutItem { Text = "Open Scrunch" };
            OnClick(home, (_, _) => HomeRequested?.Invoke(this, EventArgs.Empty));
            menu.Items.Add(home);
            menu.Items.Add(new MenuFlyoutSeparator());
        }
        var close = new MenuFlyoutItem { Text = record == null ? "Close test note" : "Discard note", KeyboardAcceleratorTextOverride = record == null ? "" : "Ctrl+Shift+Delete" };
        _discardItem = close;
        OnClick(close, (_, _) => { _discardAfterMenu = true; menu.Hide(); });
        menu.Items.Add(close);
        Note.ContextFlyout = menu;

        if (record != null)
        {
            Note.Changed += (_, _) => CaptureRecord();
            Note.ContentSizeChanged += (_, _) => CaptureRecord();
            Note.WindowDragEnded += (_, _) => CaptureRecord();
            _appWin.Changed += AppWindowChanged;
            AddShortcut(Windows.System.VirtualKey.N, Windows.System.VirtualKeyModifiers.Control, () => NewRequested?.Invoke(this, EventArgs.Empty));
            AddShortcut(Windows.System.VirtualKey.Z, Windows.System.VirtualKeyModifiers.Control | Windows.System.VirtualKeyModifiers.Shift, () => UndoRequested?.Invoke(this, EventArgs.Empty));
            AddShortcut(Windows.System.VirtualKey.Delete, Windows.System.VirtualKeyModifiers.Control | Windows.System.VirtualKeyModifiers.Shift, () => Discard());
            // The focused TextBox consumes Delete combinations before parent
            // accelerators. Honor the advertised discard shortcut in preview.
            Note.PreviewKeyDown += DiscardPreviewKeyDown;
            _detachNativeEvents.Add(() => Note.PreviewKeyDown -= DiscardPreviewKeyDown);
        }

        // Backup: AppWindow sizing pre-Activate is unreliable on some machines.
        Activated += FirstActivated;
        FitWindowToContent();
        if (Record != null) RestorePosition();
        _recordReady = true;
    }

    private void FirstActivated(object sender, WindowActivatedEventArgs e)
    {
        Activated -= FirstActivated;
        FitWindowToContent();
        CaptureRecord();
    }
    private void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => FitWindowToContent();
    private void AppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args) { if (args.DidPositionChange) CaptureRecord(); }

    private void DiscardPreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (_closed || e.Key != Windows.System.VirtualKey.Delete) return;
        bool Down(Windows.System.VirtualKey key) => Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (Down(Windows.System.VirtualKey.Control) && Down(Windows.System.VirtualKey.Shift) &&
            !Down(Windows.System.VirtualKey.Menu) && !Down(Windows.System.VirtualKey.LeftWindows) && !Down(Windows.System.VirtualKey.RightWindows))
        {
            e.Handled = true;
            Discard();
        }
    }

    private void AddShortcut(Windows.System.VirtualKey key, Windows.System.VirtualKeyModifiers modifiers, Action action)
    {
        var shortcut = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = key, Modifiers = modifiers };
        Windows.Foundation.TypedEventHandler<Microsoft.UI.Xaml.Input.KeyboardAccelerator, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs> invoked = (_, e) => { action(); e.Handled = true; };
        shortcut.Invoked += invoked;
        _detachNativeEvents.Add(() => shortcut.Invoked -= invoked);
        Note.KeyboardAccelerators.Add(shortcut);
    }
    private void OnClick(MenuFlyoutItem item, RoutedEventHandler handler)
    {
        item.Click += handler; _detachNativeEvents.Add(() => item.Click -= handler);
    }

    public void FocusEditor()
    {
        Activate();
        if (Note.IsLoaded) Note.FocusEditor();
        else Note.Loaded += FocusWhenLoaded;
    }
    private void FocusWhenLoaded(object sender, RoutedEventArgs e) { Note.Loaded -= FocusWhenLoaded; Note.FocusEditor(); }
    public void Discard(bool animate = false)
    {
        if (Record == null)
        {
            if (animate) DiscardTestNote();
            else Close();
        }
        else DiscardRequested?.Invoke(this, animate);
    }
    private async void DiscardTestNote()
    {
        if (IsDiscarding || _closed) return;
        await PlayDiscardAsync();
        if (!_closed) Close();
    }
    public async Task PlayDiscardAsync()
    {
        int version = ++DiscardVersion;
        IsDiscarding = true;
        Note.IsHitTestVisible = false;
        using var cancellation = new CancellationTokenSource();
        _fxCancellation?.Cancel(); _fxCancellation = cancellation;
        try
        {
            var result = Note.AllowNativeFx
                ? await ScrunchFX.ScrunchFxService.Shared.PlayAsync(() => Note.CaptureForFxAsync(_hwnd),
                    visible => { if (!_closed && (!visible || Record?.DeletedAt == null)) Note.ShowFxSource(visible); }, cancellation.Token)
                : new ScrunchFX.FxResult("Reduced motion or editor unavailable", 0);
            if (version == DiscardVersion) { LastDiscardFrames = result.Frames; LastDiscardOutcome = result.Outcome; }
        }
        finally
        {
            if (version == DiscardVersion)
            {
                IsDiscarding = false; if (!_closed) Note.IsHitTestVisible = true;
            }
            if (_fxCancellation == cancellation) _fxCancellation = null;
        }
    }
    public void CancelDiscard()
    {
        DiscardVersion++;
        _fxCancellation?.Cancel();
        Note.ShowFxSource(true);
        Note.CancelDiscard();
        IsDiscarding = false;
        if (!_closed) Note.IsHitTestVisible = true;
    }

    internal void SetTestContent(string text, string colour, double width, double height)
    {
        Note.Text = text; Note.SetColour(colour); Note.SetPaperSize(width, height);
    }
    internal async Task InvokeMenuDiscardForCheckAsync()
    {
        for (int i = 0; i < 40 && !Note.IsLoaded; i++) await Task.Delay(50);
        if (!Note.IsLoaded) throw new InvalidOperationException("The note must be loaded before testing its menu.");
        _menu!.ShowAt(Note);
        await Task.Delay(80);
        var peer = new Microsoft.UI.Xaml.Automation.Peers.MenuFlyoutItemAutomationPeer(_discardItem!);
        var provider = (Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke);
        provider.Invoke();
    }

    public void CaptureRecord()
    {
        if (Record == null || !_recordReady) return;
        Record.Text = Note.Text; Record.Colour = Note.ColourKey; Record.Feel = Note.FeelIndex;
        Record.Width = Note.PaperWidth; Record.Height = Note.PaperHeight;
        Record.ReducedMotion = Note.ReducedMotion;
        Record.X = _appWin.Position.X; Record.Y = _appWin.Position.Y;
        Record.Pinned = (_appWin.Presenter as OverlappedPresenter)?.IsAlwaysOnTop ?? false;
        Record.UpdatedAt = DateTimeOffset.UtcNow;
        RecordChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RestorePosition()
    {
        if (Record == null) return;
        var requested = new PointInt32(Record.X, Record.Y);
        var area = DisplayArea.GetFromPoint(requested, DisplayAreaFallback.Nearest).WorkArea;
        // Keep the paper reachable after a monitor is unplugged. Window bounds include 80 DIP padding.
        int pad = (int)Math.Round(80 * GetDpiForWindow(_hwnd) / 96.0);
        int x = Math.Clamp(requested.X, area.X - pad, Math.Max(area.X - pad, area.X + area.Width - _appWin.Size.Width + pad));
        int y = Math.Clamp(requested.Y, area.Y - pad, Math.Max(area.Y - pad, area.Y + area.Height - _appWin.Size.Height + pad));
        _appWin.Move(new PointInt32(x, y));
    }

    private void FitWindowToContent()
    {
        if (double.IsNaN(Note.ContentWidth) || Note.ContentWidth < 50 || Note.ContentHeight < 50)
        {
            return;
        }
        double scale = GetDpiForWindow(_hwnd) / 96.0;
        var w = (int)Math.Round(Note.ContentWidth * scale);
        var h = (int)Math.Round(Note.ContentHeight * scale);
        GetWindowRect(_hwnd, out var r);
        if (Math.Abs(w - (r.Right - r.Left)) > 1 || Math.Abs(h - (r.Bottom - r.Top)) > 1)
        {
            SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, w, h, SWP_NOMOVE | SWP_NOZORDER);
        }
    }
}
