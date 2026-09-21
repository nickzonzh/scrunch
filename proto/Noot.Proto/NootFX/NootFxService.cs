using System.Diagnostics;
using System.Numerics;
using Microsoft.UI.Dispatching;

namespace Noot_Proto.NootFX;

internal sealed record NoteSnapshot(byte[] Pixels, int Width, int Height, int ScreenX, int ScreenY, Vector4 Colour);
internal sealed record FxResult(string Outcome, int Frames);

// UI-thread owned, one effect at a time. Never instantiated per note. The domain
// action belongs to ProductWindow; this service may only delay its visual close.
internal sealed class NootFxService : IDisposable
{
    public static NootFxService Shared { get; } = new();
    private D3DRenderer? _renderer;
    public bool Active { get; private set; }
    public bool Drawing { get; private set; }
    public int DeviceCreations { get; private set; }
    public int CompletedEffects { get; private set; }
    public long TotalFrames { get; private set; }
    public FxResult LastResult { get; private set; } = new("Not started", 0);
    public string Adapter => _renderer?.Adapter ?? "Dormant (device not created)";
    public static bool SlowPlayback { get; set; }
#if DEBUG
    internal static string? InjectFailure { get; set; }
    internal static float? HeldProgress { get; set; }
#endif
    private Action? _stop;
    public async Task<FxResult> PlayAsync(Func<Task<NoteSnapshot>> capture, Action<bool> showSource, CancellationToken cancellation)
    {
        if (Active) return new FxResult("Busy fallback", 0);
        Active = true;
        var intervals = new List<double>(); var draws = new List<double>();
        int frames = 0; string outcome = "Immediate fallback";
        var total = Stopwatch.StartNew();
        try
        {
#if DEBUG
            if (InjectFailure == "initialization") throw new InvalidOperationException("Injected graphics initialization failure");
#endif
            var snapshot = await capture().WaitAsync(TimeSpan.FromSeconds(1.5), cancellation);
            cancellation.ThrowIfCancellationRequested();
#if DEBUG
            if (InjectFailure == "asset") throw new InvalidDataException("Injected missing or invalid bake");
#endif
            if (_renderer == null) { _renderer = D3DRenderer.Create(); DeviceCreations++; }
            _renderer.Prepare(snapshot, (int)Math.Ceiling(Math.Max(snapshot.Width, snapshot.Height) * .65));
            _renderer.Render(snapshot, 0, 0, 1);
            _renderer.Overlay.Show(); showSource(false);
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var clock = Stopwatch.StartNew(); double previous = 0;
            double duration = SlowPlayback ? 18000 : 680;
            Exception? renderError = null;
            // WinUI's Rendering event measured ~31ms on this Windows 10 host,
            // even though D3D draws took <1ms. An effect-scoped dispatcher timer
            // drives the native swap chain; Present(1) synchronizes with DWM.
            // No timer exists until playback, and Stop removes its handler.
            var timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            // 16ms rounds to ~31ms with the desktop's timer resolution. 8ms
            // measured as ~15.6ms here; DXGI remains the presentation throttle.
            timer.Interval = TimeSpan.FromMilliseconds(8);
            void Stop() { timer.Stop(); timer.Tick -= Draw; Drawing = false; done.TrySetResult(); }
            void Draw(DispatcherQueueTimer sender, object args)
            {
                try
                {
                    if (cancellation.IsCancellationRequested) { Stop(); return; }
#if DEBUG
                    if (InjectFailure == "render" && frames > 2) throw new InvalidOperationException("Injected mid-frame failure");
#endif
                    double elapsed = clock.Elapsed.TotalMilliseconds;
                    float progress = Math.Clamp((float)(elapsed / duration), 0, 1);
                    float deformation = Math.Clamp(progress / .77f, 0, 1);
                    float discard = Math.Clamp((progress - .77f) / .23f, 0, 1);
#if DEBUG
                    if (HeldProgress is { } held) { deformation = held; discard = 0; }
#endif
                    var draw = Stopwatch.StartNew();
                    _renderer.Render(snapshot, deformation, discard, 1 - Math.Clamp((discard - .55f) / .45f, 0, 1));
                    draws.Add(draw.Elapsed.TotalMilliseconds);
                    if (frames > 0) intervals.Add(elapsed - previous);
                    previous = elapsed; frames++; TotalFrames++;
#if DEBUG
                    if (HeldProgress != null && elapsed < 60000) return;
#endif
                    if (progress >= 1) Stop();
                }
                catch (Exception error) { renderError = error; Stop(); }
            }
            _stop = Stop; Drawing = true; timer.Tick += Draw; timer.Start();
            // Bound the lifecycle even if composition callbacks stop (minimize,
            // display sleep, shutdown). Cancellation is observed outside Draw too.
            try { await done.Task.WaitAsync(TimeSpan.FromSeconds(65), cancellation); }
            finally { Stop(); _stop = null; }
            if (renderError != null) throw renderError;
            cancellation.ThrowIfCancellationRequested();
            CompletedEffects++; outcome = "Animated";
        }
        catch (OperationCanceledException) { outcome = "Cancelled"; }
        catch (Exception error)
        {
            outcome = "Fallback: " + error.Message;
            _renderer?.Dispose(); _renderer = null; // Allows recovery on the next action.
        }
        finally
        {
            _renderer?.EndEffect();
            showSource(true); Active = false;
            LastResult = new FxResult(outcome, frames);
            using var process = Process.GetCurrentProcess();
            WriteDiagnostic(new { outcome, frames, elapsedMs = total.Elapsed.TotalMilliseconds, adapter = Adapter,
                frameMedianMs = Percentile(intervals, .5), frameP95Ms = Percentile(intervals, .95),
                drawMedianMs = Percentile(draws, .5), drawP95Ms = Percentile(draws, .95),
                Active, Drawing, DeviceCreations, CompletedEffects, TotalFrames,
                privateBytes = process.PrivateMemorySize64, handles = process.HandleCount });
        }
        return LastResult;
    }
    private static double Percentile(List<double> values, double percentile)
    { if (values.Count == 0) return 0; values.Sort(); return values[(int)((values.Count - 1) * percentile)]; }
    [Conditional("DEBUG")]
    internal static void WriteDiagnostic(object value)
    {
        try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "nootfx.jsonl"), System.Text.Json.JsonSerializer.Serialize(value) + "\n"); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    public void Dispose() { _stop?.Invoke(); _renderer?.Dispose(); _renderer = null; }
}
