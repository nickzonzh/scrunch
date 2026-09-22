using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.ViewManagement;
namespace Scrunch;
public sealed partial class NoteView : UserControl
{
    public bool DragMovesWindow { get; set; }
    public double ContentWidth => Root.Width;
    public double ContentHeight => Root.Height;
    public string Text { get => NoteText.Text; set { _contentVersion++; Edit(); NoteText.Text = value; } }
    public string ColourKey { get; private set; } = "yellow";
    public int FeelIndex { get; private set; } = 1;
    public bool ReducedMotion { get; private set; }
    internal bool AllowNativeFx => !ReducedMotion && _settings.AnimationsEnabled && IsLoaded;
    internal async Task<ScrunchFX.NoteSnapshot> CaptureForFxAsync(IntPtr window)
    {
        Edit();
        var bitmap = new RenderTargetBitmap();
        double scale = XamlRoot.RasterizationScale;
        await bitmap.RenderAsync(Paper, (int)Math.Round(Paper.Width * scale), (int)Math.Round(Paper.Height * scale));
        var buffer = await bitmap.GetPixelsAsync();
        byte[] pixels = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(buffer);
        if (bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0 || pixels.Length != bitmap.PixelWidth * bitmap.PixelHeight * 4)
            throw new InvalidOperationException("The native note snapshot is empty");
        var point = Paper.TransformToVisual(this).TransformPoint(new Point(0, 0));
        var screen = new FxPoint { X = (int)Math.Round(point.X * scale), Y = (int)Math.Round(point.Y * scale) };
        if (!ClientToScreen(window, ref screen)) throw new System.ComponentModel.Win32Exception();
        var colour = PaperTokens.Colours[ColourKey];
        return new ScrunchFX.NoteSnapshot(pixels, bitmap.PixelWidth, bitmap.PixelHeight, screen.X, screen.Y,
            new Vector4(colour.R / 255f, colour.G / 255f, colour.B / 255f, 1));
    }
    internal void ShowFxSource(bool visible) { if (IsLoaded) Opacity = visible ? 1 : 0; }
    [StructLayout(LayoutKind.Sequential)] private struct FxPoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref FxPoint point);
    public bool AnimateOnLoad { get; set; } = true;
    public double PaperWidth => Paper.Width;
    public double PaperHeight => Paper.Height;
    public event EventHandler? Changed;
    public string TextureDigest => _renderer?.TextureDigest ?? "";
    public event EventHandler? WindowDragStarted;
    public event EventHandler<Point>? WindowDragDelta;
    public event EventHandler? WindowDragEnded;
    public event EventHandler? ContentSizeChanged;
    private PaperRenderer? _renderer;
    private Visual? _visual;
    private bool _dragging, _resizing, _wired;
    private bool _closed;
    private Point _press, _resizePress;
    private Vector3 _offset;
    private double _resizeW, _resizeH, _lastX;
    private long _lastTick;
    private float _grab = 0.5f;
    private readonly UISettings _settings = new();
    private int _contentVersion;
    private int _capturedVersion = -1;
    private Task<bool>? _capture;
    private int _request;
    private bool _discarding;
    private bool _inspection;
    private int _discardGeneration;
    public string LastDiscardOutcome { get; private set; } = "Not started";
    public NoteView() => InitializeComponent();
    private async void NoteView_Loaded(object sender, RoutedEventArgs e)
    {
        if (_closed || _renderer != null) return;
        _visual = ElementCompositionPreview.GetElementVisual(TiltRoot);
        _renderer = new PaperRenderer(MeshHost, Paper) { Feel = PaperFeel.All[FeelIndex], PaperColour = PaperTokens.Colours[ColourKey] };
        _renderer.Resize((float)Paper.Width, (float)Paper.Height);
        SetReducedMotion(ReducedMotion);
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) _settings.AnimationsEnabledChanged += AnimationsChanged;
        if (!_wired)
        {
            _wired = true;
            GrabZone.PointerPressed += GrabPressed;
            GrabZone.PointerMoved += GrabMoved;
            GrabZone.PointerReleased += GrabReleased;
            GrabZone.PointerCanceled += GrabReleased;
            GrabZone.PointerCaptureLost += GrabReleased;
            Grip.PointerPressed += ResizePressed;
            Grip.PointerMoved += ResizeMoved;
            Grip.PointerReleased += ResizeReleased;
            Grip.PointerCanceled += ResizeReleased;
            Grip.PointerCaptureLost += ResizeReleased;
            NoteText.GotFocus += EditorGotFocus;
            NoteText.TextChanging += EditorTextChanging;
        }
        BuildFibres();
        try
        {
            using var stream = File.OpenRead(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "ShadowPaper.png"));
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
            if (!_closed) RestShadowBrush.ImageSource = bitmap;
        }
        catch { /* The paper still works without its rest shadow. */ }
        if (_closed) return;
        if (AnimateOnLoad && NoteText.FocusState == FocusState.Unfocused) await PrepareMotion(r => r.Peel());
    }
    private void AnimationsChanged(UISettings sender, object args) => DispatcherQueue.TryEnqueue(() => SetReducedMotion(ReducedMotion));
    private void NoteView_Unloaded(object sender, RoutedEventArgs e) => ReleaseGraphics();
    // Window.Close does not guarantee Unloaded. This is terminal, unlike a
    // temporary unload: sever callbacks into the owner and the native XAML tree.
    internal void ReleaseForClose()
    {
        if (_closed) return;
        _closed = true;
        Loaded -= NoteView_Loaded; Unloaded -= NoteView_Unloaded;
        CancelDiscard(); ReleaseGraphics();
        Changed = null; WindowDragStarted = null; WindowDragDelta = null;
        WindowDragEnded = null; ContentSizeChanged = null;
        ContextFlyout = null; RestShadowBrush.ImageSource = null;
        _visual = null;
    }
    internal void ReleaseGraphics()
    {
        _request++;
        if (_wired)
        {
            GrabZone.PointerPressed -= GrabPressed; GrabZone.PointerMoved -= GrabMoved;
            GrabZone.PointerReleased -= GrabReleased; GrabZone.PointerCanceled -= GrabReleased; GrabZone.PointerCaptureLost -= GrabReleased;
            Grip.PointerPressed -= ResizePressed; Grip.PointerMoved -= ResizeMoved;
            Grip.PointerReleased -= ResizeReleased; Grip.PointerCanceled -= ResizeReleased; Grip.PointerCaptureLost -= ResizeReleased;
            NoteText.GotFocus -= EditorGotFocus; NoteText.TextChanging -= EditorTextChanging;
            _wired = false;
        }
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) _settings.AnimationsEnabledChanged -= AnimationsChanged;
        _renderer?.Dispose(); _renderer = null;
    }
    private void EditorGotFocus(object sender, RoutedEventArgs args) { if (!_discarding && !_inspection) Edit(); }
    private void EditorTextChanging(TextBox sender, TextBoxTextChangingEventArgs args) { _contentVersion++; Edit(); Changed?.Invoke(this, EventArgs.Empty); }
    private void BuildFibres()
    {
        // Fixed DIP density: resizing reveals more paper instead of stretching grain.
        Fibres.Children.Clear();
        var random = new Random(729);
        var ink = new SolidColorBrush(Color.FromArgb(13, 91, 73, 36));
        for (int i = 0; i < Paper.Width * Paper.Height / 220; i++)
        {
            var fibre = new Rectangle { Width = 0.6 + random.NextDouble() * 1.2, Height = 0.5, Fill = ink };
            Canvas.SetLeft(fibre, random.NextDouble() * (Paper.Width - 2));
            Canvas.SetTop(fibre, random.NextDouble() * (Paper.Height - 2));
            Fibres.Children.Add(fibre);
        }
    }
    public void SetFeel(int index)
    {
        FeelIndex = Math.Clamp(index, 0, 2);
        if (_renderer != null) { _renderer.Feel = PaperFeel.All[FeelIndex]; _renderer.Refresh(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void SetReducedMotion(bool value)
    {
        ReducedMotion = value;
        if (_renderer != null) { _renderer.ReducedMotion = value || !_settings.AnimationsEnabled; _renderer.Rest(); }
        if (value || !_settings.AnimationsEnabled) Edit();
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void SetColour(string key)
    {
        if (!PaperTokens.Colours.TryGetValue(key, out var colour)) return;
        ColourKey = key; Paper.Background = new SolidColorBrush(colour); _contentVersion++;
        if (_renderer != null) _renderer.PaperColour = colour;
        Edit();
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void SetTypography(NoteTypography typography)
    {
        var family = (FontFamily)Application.Current.Resources[typography.ResourceKey];
        if (NoteText.FontFamily.Source == family.Source && NoteText.FontSize == typography.Size) return;
        NoteText.FontFamily = family;
        NoteText.FontSize = typography.Size;
        _contentVersion++;
        Edit(); // The next paper motion must capture the newly laid-out text.
    }
    public Task PeelAsync() => PrepareMotion(r => r.Peel());
    public async Task DiscardAsync()
    {
        if (_renderer == null) { LastDiscardOutcome = "Editor not loaded"; return; }
        if (_renderer.ReducedMotion) { LastDiscardOutcome = "Reduced motion"; return; }
        int generation = ++_discardGeneration;
        _discarding = true;
        try
        {
            Task<bool>? animation = null;
            var preparation = PrepareMotion(r => animation = r.DiscardAsync());
            // Menu opening prewarms the texture. Allow a cold capture to finish too;
            // the old 200ms deadline silently skipped valid first-time discards.
            if (await Task.WhenAny(preparation, Task.Delay(1500)) != preparation)
            {
                if (generation == _discardGeneration) { LastDiscardOutcome = "Capture timed out"; Edit(); }
                return;
            }
            await preparation;
            bool completed = animation != null && await animation;
            if (generation == _discardGeneration) LastDiscardOutcome = completed ? "Animated" : "Cancelled or capture unavailable";
        }
        finally { if (generation == _discardGeneration) _discarding = false; }
    }
    public void CancelDiscard() { _discardGeneration++; _discarding = false; LastDiscardOutcome = "Cancelled"; Edit(); }
    public async Task WarmTextureAsync()
    {
        if (_renderer == null || _renderer.ReducedMotion || _capturedVersion == _contentVersion) return;
        try
        {
            if (_capture == null) _capture = CaptureTextureAsync(_renderer, _contentVersion);
            await _capture;
        }
        catch { /* Motion is optional; the native editor stays usable. */ }
    }
    internal Task InspectMaterialAsync(float amount, bool lift)
    {
        _inspection = true;
        return PrepareMotion(r => { if (lift) r.Hold(1800, 1); else r.PreviewCrumple(amount); });
    }
    private void Edit()
    {
        _inspection = false;
        _request++;
        if (_renderer != null) { _renderer.Preview(0); _renderer.Visible = false; }
        SourceHost.Opacity = 1; RestShadow.Opacity = 0.5;
    }
    private async Task PrepareMotion(Action<PaperRenderer> action)
    {
        if (_renderer == null || _renderer.ReducedMotion) return;
        int request = ++_request;
        var renderer = _renderer;
        try
        {
            while (_capturedVersion != _contentVersion)
            {
                SourceHost.Opacity = 1; renderer.Visible = false;
                if (_capture == null) _capture = CaptureTextureAsync(renderer, _contentVersion);
                bool ready = await _capture;
                if (!ready) { Edit(); return; }
                if (request != _request || renderer != _renderer) return;
            }
            if (request != _request || renderer != _renderer) return;
            SourceHost.Opacity = 0; RestShadow.Opacity = 0;
            renderer.Visible = true;
            action(renderer);
        }
        catch { Edit(); /* Same: fall back to native editing rather than fault. */ }
    }
    private async Task<bool> CaptureTextureAsync(PaperRenderer renderer, int version)
    {
        try
        {
            bool ready = await renderer.CaptureAsync(Paper);
            if (ready) _capturedVersion = version;
            return ready;
        }
        finally { _capture = null; }
    }
    private Point Pointer(PointerRoutedEventArgs e)
    {
        // Screen coordinates remain stable while an OS window moves under the pointer.
        if (DragMovesWindow && GetCursorPos(out var p)) return new Point(p.X, p.Y);
        return e.GetCurrentPoint(Root).Position;
    }
    private Point ResizePointer(PointerRoutedEventArgs e) => GetCursorPos(out var p)
        ? new Point(p.X, p.Y) : e.GetCurrentPoint(Root).Position;
    private async void GrabPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging || _resizing || !e.GetCurrentPoint(GrabZone).Properties.IsLeftButtonPressed) return;
        _dragging = GrabZone.CapturePointer(e.Pointer);
        if (!_dragging) return;
        _grab = (float)Math.Clamp(e.GetCurrentPoint(GrabZone).Position.X / Paper.Width, 0, 1);
        _press = Pointer(e); _offset = _visual!.Offset;
        _lastX = _press.X; _lastTick = Environment.TickCount64;
        WindowDragStarted?.Invoke(this, EventArgs.Empty); e.Handled = true;
        await PrepareMotion(r => { if (_dragging) r.Hold(0, _grab); else r.Rest(); });
    }
    private void GrabMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        var p = Pointer(e);
        if (DragMovesWindow) WindowDragDelta?.Invoke(this, new Point(p.X - _press.X, p.Y - _press.Y));
        else
        {
            var next = _offset + new Vector3((float)(p.X - _press.X), (float)(p.Y - _press.Y), 0);
            _visual!.Offset = new Vector3(Math.Clamp(next.X, -120, 120), Math.Clamp(next.Y, -100, 100), 0);
        }
        long now = Environment.TickCount64;
        if (now > _lastTick)
        {
            float speed = (float)((p.X - _lastX) * 1000 / (now - _lastTick));
            _renderer?.Hold(speed, _grab); _lastX = p.X; _lastTick = now;
        }
        e.Handled = true;
    }
    private void GrabReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false; GrabZone.ReleasePointerCapture(e.Pointer); _renderer?.Rest();
        WindowDragEnded?.Invoke(this, EventArgs.Empty); e.Handled = true;
    }
    private void ResizePressed(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging || _resizing || !e.GetCurrentPoint(Grip).Properties.IsLeftButtonPressed) return;
        _resizing = Grip.CapturePointer(e.Pointer);
        _resizePress = ResizePointer(e); _resizeW = Paper.Width; _resizeH = Paper.Height;
        Edit(); e.Handled = true;
    }
    private void ResizeMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_resizing) return;
        var p = ResizePointer(e);
        double scale = XamlRoot.RasterizationScale;
        SetPaperSize(_resizeW + (p.X - _resizePress.X) / scale, _resizeH + (p.Y - _resizePress.Y) / scale, false);
        e.Handled = true;
    }
    private void ResizeReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_resizing) return;
        _resizing = false; Grip.ReleasePointerCapture(e.Pointer);
        BuildFibres(); e.Handled = true;
    }
    public void FocusEditor() { Edit(); NoteText.Focus(FocusState.Programmatic); }
    public void SetPaperSize(double width, double height, bool fibres = true)
    {
        Paper.Width = Math.Clamp(width, 220, 440);
        Paper.Height = Math.Clamp(height, 180, 440);
        RestShadow.Width = Paper.Width + 100; RestShadow.Height = Paper.Height + 100;
        Root.Width = Paper.Width + 160; Root.Height = Paper.Height + 160;
        _renderer?.Resize((float)Paper.Width, (float)Paper.Height);
        _contentVersion++;
        if (fibres) BuildFibres();
        ContentSizeChanged?.Invoke(this, EventArgs.Empty);
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X; public int Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
}
