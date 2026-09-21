using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading.Tasks;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.Foundation;
using Windows.UI;

namespace Noot_Proto;

// Native compositor mesh. One cached editor snapshot is shared by all patches.
// Texture upload happens on content changes, never per animation frame.
internal sealed class PaperRenderer : IDisposable
{
    private const int Columns = PaperGeometry.Columns, Rows = PaperGeometry.Rows;
    private readonly Compositor _com;
    private readonly FrameworkElement _host;
    private readonly ContainerVisual _root;
    private readonly ContainerVisual _stage;
    private LoadedImageSurface? _surface;
    private bool _disposed;
    private readonly List<(SpriteVisual Face, SpriteVisual Shade, SpriteVisual Highlight, CompositionSurfaceBrush Brush)> _tiles = new();
    private readonly SpriteVisual _shadow;
    private readonly DropShadow _drop;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastTime;
    private bool _ticking;
    private float _bend, _velocity, _twist, _twistVelocity, _targetBend, _targetTwist, _peel;
    private float _width = 300, _height = 320;
    private TaskCompletionSource<bool>? _discard;
    private double _discardStarted;
    private float _crumple;
    private bool _crumplePose;
    private Vector3 _flightOffset;
    private float _flight;
    private readonly Vector3[] _points = new Vector3[(Columns + 1) * (Rows + 1)];
    private readonly Vector2[] _projected = new Vector2[(Columns + 1) * (Rows + 1)];
    private readonly float[] _lighting = new float[(Columns + 1) * (Rows + 1)];
    private static readonly Vector3 LightDirection = Vector3.Normalize(new Vector3(-0.25f, -0.45f, 1));
    public bool IsDiscarding => _discard != null;
    public int DiscardFrames { get; private set; }
    public PaperFeel Feel { get; set; } = PaperFeel.All[1];
    public bool ReducedMotion { get; set; }
    public bool IsAnimating => _ticking;
    public int PatchCount => Columns * Rows;
    public string TextureDigest { get; private set; } = "";
    public Color PaperColour { get; set; } = Color.FromArgb(255, 253, 238, 158);

    public PaperRenderer(FrameworkElement host, FrameworkElement source)
    {
        _host = host;
        _com = ElementCompositionPreview.GetElementVisual(host).Compositor;
        _stage = _com.CreateContainerVisual();
        _stage.Offset = new Vector3(PaperGeometry.Padding, PaperGeometry.Padding, 0);
        _root = _com.CreateContainerVisual();
        _stage.Children.InsertAtTop(_root);
        ElementCompositionPreview.SetElementChildVisual(host, _stage);
        _shadow = _com.CreateSpriteVisual();
        _drop = _com.CreateDropShadow();
        _drop.Color = Colors.Black;
        _drop.BlurRadius = 18;
        _drop.Opacity = 0.22f;
        _shadow.Shadow = _drop;
        _stage.Children.InsertAtBottom(_shadow);
        for (int i = 0; i < Columns * Rows; i++)
        {
            var face = _com.CreateSpriteVisual();
            face.BorderMode = CompositionBorderMode.Soft;
            var brush = _com.CreateSurfaceBrush();
            brush.Stretch = CompositionStretch.None;
            brush.HorizontalAlignmentRatio = 0;
            brush.VerticalAlignmentRatio = 0;
            face.Brush = brush;
            var shade = _com.CreateSpriteVisual();
            shade.Brush = CreateLightingBrush();
            face.Children.InsertAtTop(shade);
            var highlight = _com.CreateSpriteVisual();
            highlight.Brush = CreateLightingBrush();
            highlight.Opacity = 0;
            face.Children.InsertAtTop(highlight);
            _root.Children.InsertAtTop(face);
            _tiles.Add((face, shade, highlight, brush));
        }
        Resize(_width, _height);
        Visible = false;
    }

    private CompositionLinearGradientBrush CreateLightingBrush()
    {
        var brush = _com.CreateLinearGradientBrush();
        brush.ColorStops.Add(_com.CreateColorGradientStop(0, Colors.Transparent));
        brush.ColorStops.Add(_com.CreateColorGradientStop(1, Colors.Transparent));
        return brush;
    }

    private void LightPatch(SpriteVisual shade, SpriteVisual highlight, float a, float b, float c, float d)
    {
        float average = (a + b + c + d) / 4;
        var gradient = new Vector2((b + c - a - d) / 2, (c + d - a - b) / 2);
        float length = gradient.Length();
        var direction = length > 0.00001f ? gradient / length : Vector2.UnitY;
        var low = new Vector2(0.5f) - direction * 0.707107f;
        var high = new Vector2(0.5f) + direction * 0.707107f;
        var dark = (CompositionLinearGradientBrush)shade.Brush;
        var bright = (CompositionLinearGradientBrush)highlight.Brush;
        dark.StartPoint = bright.StartPoint = low; dark.EndPoint = bright.EndPoint = high;
        for (int i = 0; i < 2; i++)
        {
            float light = average + (i == 0 ? -1 : 1) * length * 0.707107f;
            byte dim = (byte)(255 * Math.Clamp((0.9f - light) * 0.5f + _crumple * 0.015f, 0, 0.42f));
            byte gleam = (byte)(255 * _crumple * Math.Clamp((light - 0.72f) * 0.12f, 0, 0.04f));
            dark.ColorStops[i].Color = Color.FromArgb(dim, 38, 28, 15);
            bright.ColorStops[i].Color = Color.FromArgb(gleam, 255, 255, 255);
        }
        shade.Opacity = highlight.Opacity = 1;
    }

    public bool Visible { get => _stage.IsVisible; set => _stage.IsVisible = value; }
    public async Task<bool> CaptureAsync(FrameworkElement source)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(source);
        var pixels = await bitmap.GetPixelsAsync();
        if (_disposed || bitmap.PixelWidth == 0) return false;
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        var bytes = pixels.ToArray();
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, bytes);
        await encoder.FlushAsync();
        stream.Seek(0);
        var loaded = LoadedImageSurface.StartLoadFromStream(stream, new Size(_width, _height));
        var completion = new TaskCompletionSource<bool>();
        loaded.LoadCompleted += (_, e) => completion.TrySetResult(e.Status == LoadedImageSourceLoadStatus.Success);
        if (await Task.WhenAny(completion.Task, Task.Delay(5000)) != completion.Task ||
            !await completion.Task || _disposed) { loaded.Dispose(); return false; }
        var old = _surface;
        _surface = loaded;
        foreach (var tile in _tiles)
        {
            tile.Brush.Surface = loaded;
            tile.Brush.Scale = new Vector2(_width / (float)loaded.DecodedSize.Width, _height / (float)loaded.DecodedSize.Height);
        }
        old?.Dispose();
        TextureDigest = Convert.ToHexString(SHA256.HashData(bytes));
        return true;
    }

    public void Resize(float width, float height)
    {
        _width = width; _height = height;
        _root.Size = new Vector2(width, height);
        float w = width / Columns, h = height / Rows;
        for (int y = 0; y < Rows; y++)
        for (int x = 0; x < Columns; x++)
        {
            var tile = _tiles[y * Columns + x];
            // Overlap internal seams only; sampling outside the texture at the
            // silhouette used to leave ragged/transparent strips on the edge.
            tile.Face.Size = new Vector2(w + (x < Columns - 1 ? 0.5f : 0), h + (y < Rows - 1 ? 0.5f : 0));
            tile.Shade.Size = tile.Face.Size;
            tile.Highlight.Size = tile.Face.Size;
            tile.Brush.Offset = new Vector2(-x * w, -y * h);
        }
        Draw();
    }

    public void Peel()
    {
        if (_crumplePose) CancelDiscard();
        if (ReducedMotion) { Rest(); return; }
        _peel = 1;
        _targetBend = 0; _targetTwist = 0;
        Wake();
    }

    public void Hold(float horizontalSpeed = 0, float grab = 0.5f)
    {
        if (_crumplePose) CancelDiscard();
        _peel = 0; // A grab immediately interrupts arrival.
        _targetBend = ReducedMotion ? 0 : 0.65f;
        _targetTwist = ReducedMotion ? 0 : Math.Clamp(horizontalSpeed / 1800 + (grab - 0.5f), -1, 1);
        Wake();
    }

    public void Rest()
    {
        if (_crumplePose) CancelDiscard();
        _targetBend = _targetTwist = _peel = 0;
        if (ReducedMotion) { _bend = _twist = _velocity = _twistVelocity = 0; Stop(); Draw(); }
        else Wake();
    }

    public void Preview(float amount)
    {
        CancelDiscard();
        Stop();
        _peel = ReducedMotion ? 0 : amount;
        _bend = _twist = _velocity = _twistVelocity = _targetBend = _targetTwist = 0;
        Draw();
    }

    public void Refresh() => Draw();

    public Task<bool> DiscardAsync()
    {
        CancelDiscard();
        if (_disposed || ReducedMotion) return Task.FromResult(false);
        _discard = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _discardStarted = _clock.Elapsed.TotalSeconds;
        DiscardFrames = 0;
        _crumplePose = true; _crumple = 0;
        Wake();
        return _discard.Task;
    }

    public void PreviewCrumple(float amount)
    {
        CancelDiscard(); Stop();
        _crumplePose = true; _crumple = Math.Clamp(amount, 0, 1);
        Draw();
    }

    private void CancelDiscard()
    {
        var pending = _discard; _discard = null;
        _crumplePose = false; _crumple = 0;
        _flightOffset = Vector3.Zero; _flight = 0;
        _root.Offset = Vector3.Zero; _root.RotationAngleInDegrees = 0; _root.Opacity = 1;
        pending?.TrySetResult(false);
    }

    private void Wake()
    {
        if (ReducedMotion) { _bend = _twist = _peel = 0; Stop(); Draw(); return; }
        if (_ticking) return;
        _lastTime = _clock.Elapsed.TotalSeconds;
        _ticking = true;
        CompositionTarget.Rendering += Tick;
    }

    private void Tick(object? sender, object e)
    {
        double now = _clock.Elapsed.TotalSeconds;
        if (_discard != null)
        {
            DiscardFrames++;
            float ms = (float)((now - _discardStarted) * 1000);
            _crumple = PaperGeometry.CrumpleEase(ms / PaperTokens.CrumpleMs);
            float flight = Math.Clamp((ms - PaperTokens.ThrowDelayMs) / PaperTokens.ThrowMs, 0, 1);
            _flight = flight;
            _root.CenterPoint = new Vector3(_width / 2, _height / 2, 0);
            _root.RotationAngleInDegrees = 38 * flight;
            _flightOffset = new Vector3(78 * flight, 90 * flight - 160 * flight * (1 - flight), 0);
            _root.Offset = _flightOffset;
            _root.Opacity = Math.Clamp((1 - flight) / 0.2f, 0, 1);
            Draw();
            if (flight >= 1)
            {
                var finished = _discard; _discard = null;
                Stop(); finished.TrySetResult(true);
            }
            return;
        }
        float dt = (float)Math.Clamp(now - _lastTime, 0, 0.04);
        _lastTime = now;
        // Small bounded steps keep the spring stable after a stalled UI frame.
        int steps = Math.Max(1, (int)Math.Ceiling(dt / 0.008));
        float step = dt / steps;
        for (int i = 0; i < steps; i++)
        {
            Integrate(ref _bend, ref _velocity, _targetBend, step);
            Integrate(ref _twist, ref _twistVelocity, _targetTwist, step);
            _peel *= MathF.Exp(-step * 15);
        }
        Draw();
        if (Math.Abs(_bend - _targetBend) + Math.Abs(_twist - _targetTwist) +
            Math.Abs(_velocity) + Math.Abs(_twistVelocity) + _peel < 0.002f)
        {
            _bend = _targetBend; _twist = _targetTwist; _peel = 0;
            _velocity = _twistVelocity = 0;
            Stop(); Draw();
        }
    }

    private void Integrate(ref float value, ref float velocity, float target, float dt)
    {
        velocity += ((target - value) * Feel.Frequency * Feel.Frequency - 2 * Feel.Damping * velocity) * dt;
        value += velocity * dt;
    }

    private void Draw()
    {
        float w = _width / Columns, h = _height / Rows;
        var minimum = new Vector2(float.MaxValue);
        var maximum = new Vector2(float.MinValue);
        // Shared vertex normals remove the checkerboard of independently lit tiles.
        // Cache positions once per frame; adjacent patches use identical samples.
        for (int y = 0; y <= Rows; y++)
        for (int x = 0; x <= Columns; x++)
        {
            int i = y * (Columns + 1) + x;
            float u = (float)x / Columns, v = (float)y / Rows;
            _points[i] = _crumplePose
                ? PaperGeometry.DiscardPoint(u, v, _width, _height, Feel, _bend, _twist, _peel, _crumple)
                : PaperGeometry.Point(u, v, _width, _height, Feel, _bend, _twist, _peel);
            _projected[i] = PaperGeometry.Project(_points[i], _width, _height);
            minimum = Vector2.Min(minimum, _projected[i]); maximum = Vector2.Max(maximum, _projected[i]);
        }
        for (int y = 0; y <= Rows; y++)
        for (int x = 0; x <= Columns; x++)
        {
            var across = _points[y * (Columns + 1) + Math.Min(x + 1, Columns)] - _points[y * (Columns + 1) + Math.Max(x - 1, 0)];
            var down = _points[Math.Min(y + 1, Rows) * (Columns + 1) + x] - _points[Math.Max(y - 1, 0) * (Columns + 1) + x];
            var cross = Vector3.Cross(across, down);
            _lighting[y * (Columns + 1) + x] = Vector3.Dot(cross.LengthSquared() > 0.000001f ? Vector3.Normalize(cross) : Vector3.UnitZ, LightDirection);
        }
        for (int y = 0; y < Rows; y++)
        for (int x = 0; x < Columns; x++)
        {
            int ai = y * (Columns + 1) + x, bi = ai + 1, di = ai + Columns + 1, ci = di + 1;
            var a = _points[ai]; var b = _points[bi]; var d = _points[di];
            var tile = _tiles[y * Columns + x];
            tile.Face.TransformMatrix = PaperGeometry.Quad(_projected[ai], _projected[bi], _projected[ci], _projected[di], w, h);
            var cross = Vector3.Cross(b - a, d - a);
            var normal = cross.LengthSquared() > 0.000001f ? Vector3.Normalize(cross) : Vector3.UnitZ;
            // Back faces are unprinted paper, not mirrored writing.
            var shadeBrush = (CompositionLinearGradientBrush)tile.Shade.Brush;
            if (normal.Z < 0)
            {
                var back = Color.FromArgb(255, (byte)(PaperColour.R * 0.87f),
                    (byte)(PaperColour.G * 0.87f), (byte)(PaperColour.B * 0.87f));
                shadeBrush.ColorStops[0].Color = shadeBrush.ColorStops[1].Color = back;
                tile.Shade.Opacity = 1;
                tile.Highlight.Opacity = 0;
            }
            else
            {
                LightPatch(tile.Shade, tile.Highlight, _lighting[ai], _lighting[bi], _lighting[ci], _lighting[di]);
            }
        }
        float lift = (Math.Abs(_bend) * Feel.Flex * 28 + _peel * 28) * (1 - _crumple) + _crumple * 14 + _flight * 12;
        _shadow.Size = Vector2.Max(new Vector2(1), maximum - minimum - new Vector2(8, 12));
        _shadow.CenterPoint = new Vector3(_shadow.Size / 2, 0);
        _shadow.Scale = new Vector3(1, 1 - _crumple * 0.3f, 1);
        _shadow.Offset = new Vector3(minimum.X + 4 + lift * 0.15f + _flightOffset.X, minimum.Y + 6 + lift * 0.5f + 90 * _flight, 0);
        _shadow.Opacity = _root.Opacity;
        _drop.BlurRadius = 12 + lift;
        _drop.Opacity = 0.18f;
    }

    private void Stop()
    {
        if (!_ticking) return;
        CompositionTarget.Rendering -= Tick;
        _ticking = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelDiscard();
        Stop();
        ElementCompositionPreview.SetElementChildVisual(_host, null);
        foreach (var tile in _tiles) { tile.Shade.Brush.Dispose(); tile.Shade.Dispose(); tile.Highlight.Brush.Dispose(); tile.Highlight.Dispose(); tile.Face.Dispose(); tile.Brush.Dispose(); }
        _shadow.Dispose(); _drop.Dispose(); _surface?.Dispose(); _root.Dispose(); _stage.Dispose();
    }
}
