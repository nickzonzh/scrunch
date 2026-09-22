#if DEBUG
using System.Diagnostics;
using System.Text.Json;
using Scrunch.ScrunchFX;

namespace Scrunch;

public sealed partial class ProductWindow
{
    private void AddFxSeedControls(Microsoft.UI.Xaml.Controls.StackPanel stack)
    {
        var seed = new Microsoft.UI.Xaml.Controls.TextBox { Header = "FX seed (uint32)", Text = "0" };
        var selected = new Microsoft.UI.Xaml.Controls.TextBlock { TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(seed, "FxSeed");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(selected, "FxSelection");
        void Update()
        {
            if (!uint.TryParse(seed.Text, out uint value)) { selected.Text = "Enter an integer from 0 to 4294967295"; return; }
            ScrunchFxService.SeedOverride = value;
            var v = FxVariation.FromSeed(value);
            double duration = DiscardMotion.DurationMs * v.DurationScale;
            selected.Text = $"{v.FamilyName} · {v.OrientationName}\n{duration:F0}ms · gather {duration * DiscardMotion.GatherEnd:F0} / hold {duration * v.Hold:F0} / exit {duration * (1 - DiscardMotion.GatherEnd - v.Hold):F0}ms\nthrow {(v.Direction < 0 ? "left" : "right")} · reach {v.Travel:P0} · rotation {v.Rotation * 180 / Math.PI:F0}°";
        }
        seed.TextChanged += (_, _) => Update(); Update();
        stack.Children.Add(seed); stack.Children.Add(selected);
        var buttons = new Microsoft.UI.Xaml.Controls.StackPanel { Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal, Spacing = 8 };
        foreach (var (name, id, delta) in new[] { ("Previous seed", "FxPreviousSeed", -1), ("Next seed", "FxNextSeed", 1) })
        {
            var button = new Microsoft.UI.Xaml.Controls.Button { Content = name };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, id);
            button.Click += (_, _) => { if (uint.TryParse(seed.Text, out uint value)) seed.Text = unchecked(value + (uint)delta).ToString(); };
            buttons.Children.Add(button);
        }
        stack.Children.Add(buttons);
        var nextQa = new Microsoft.UI.Xaml.Controls.Button { Content = "Next QA seed" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(nextQa, "FxNextQaSeed");
        nextQa.Click += (_, _) =>
        {
            if (!uint.TryParse(seed.Text, out uint value)) return;
            int index = Array.IndexOf(FxVariation.GoldenSeeds, value);
            seed.Text = FxVariation.GoldenSeeds[(index + 1) % FxVariation.GoldenSeeds.Length].ToString();
        };
        stack.Children.Add(nextQa);
        var replay = new Microsoft.UI.Xaml.Controls.Button { Content = "Replay current seed" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(replay, "FxReplay");
        replay.Click += async (_, _) =>
        {
            if (ScrunchFxService.Shared.Active || !uint.TryParse(seed.Text, out _)) return;
            var note = _windows.Values.LastOrDefault();
            if (note == null) { UndoDiscard(); note = _windows.Values.LastOrDefault(); }
            if (note == null) { await NextFxSampleAsync(); note = _windows.Values.LastOrDefault(); }
            if (note != null) { for (int i = 0; !note.EditorLoaded && i < 60; i++) await Task.Delay(50); if (note.EditorLoaded) await note.PlayDiscardAsync(); }
        };
        stack.Children.Add(replay);
    }
    private int _fxSample;
    private int _fxCorner;
    private void NextFxCorner()
    {
        if (ScrunchFxService.Shared.Active || _windows.Values.LastOrDefault() is not { } note) return;
        var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(note.AppWindow.Id,
            Microsoft.UI.Windowing.DisplayAreaFallback.Nearest).WorkArea;
        int overhang = (int)Math.Round(60 * note.RasterScaleForCheck);
        int corner = _fxCorner++ % 4;
        note.AppWindow.Move(new Windows.Graphics.PointInt32(
            corner % 2 == 0 ? area.X - overhang : area.X + area.Width - note.AppWindow.Size.Width + overhang,
            corner < 2 ? area.Y - overhang : area.Y + area.Height - note.AppWindow.Size.Height + overhang));
        note.PinForInspection();
    }
    private static readonly (string Colour, int Width, int Height, string Text)[] FxSamples =
    [
        ("yellow", 220, 180, "SMALL / 220 × 180\nMilk + bread"),
        ("mint", 440, 180, "WIDE / 440 × 180\nRemember the green notebook.\nLonger handwritten lines stay on the same paper."),
        ("pink", 220, 440, "TALL / 220 × 440\nOne\nTwo\nThree\nFour\nFive\nSix\nSeven\nEight\nNine\nTen\nEleven\nTwelve\nStill the same note."),
        ("blue", 440, 440, "LARGE / 440 × 440\n\nA note with room to breathe.\nThe colour, grain and ink should survive every fold."),
        ("lavender", 300, 320, ""),
        ("peach", 300, 320, string.Join("\n", Enumerable.Range(1, 18).Select(i => $"{i}. A dense note wraps onto another line.")))
    ];
    private async Task NextFxSampleAsync()
    {
        if (ScrunchFxService.Shared.Active) return;
        var note = _windows.Values.LastOrDefault() ?? CreateNote();
        if (note == null) return;
        for (int i = 0; i < 60 && !note.EditorLoaded; i++) await Task.Delay(50);
        if (!note.EditorLoaded) return;
        var sample = FxSamples[_fxSample++ % FxSamples.Length];
        note.SetTestContent(sample.Text, sample.Colour, sample.Width, sample.Height);
        note.PinForInspection(); note.FocusEditor();
    }
    // Exercises actual product windows, storage, capture, menu automation peer,
    // cancellation and native D3D rendering. Always uses an isolated data folder.
    private async Task VerifyFxAsync()
    {
        var checks = new List<string>(); var samples = new List<object>(); string? error = null;
        var fx = ScrunchFxService.Shared;
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
        string NormalText(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');
        async Task Until(Func<bool> condition, int milliseconds = 5000)
        {
            var watch = Stopwatch.StartNew();
            while (!condition()) { if (watch.ElapsedMilliseconds > milliseconds) throw new TimeoutException("FX check timed out"); await Task.Delay(25); }
        }
        object Resources(int iteration)
        {
            using var process = Process.GetCurrentProcess();
            return new { iteration, privateBytes = process.PrivateMemorySize64, workingSet = process.WorkingSet64,
                handles = process.HandleCount, devices = fx.DeviceCreations, fx.Active, fx.Drawing, fx.TotalFrames };
        }
        try
        {
            ScrunchFxService.SeedOverride = FxVariation.GoldenSeeds[0];
            Check(fx.DeviceCreations == 0 && !fx.Active, "Cold startup creates no D3D device");
            using var process = Process.GetCurrentProcess();
            await Task.Delay(3000);
            var cpuBefore = process.TotalProcessorTime; long frameBefore = fx.TotalFrames;
            await Task.Delay(1200);
            samples.Add(new { stage = "cold idle 1200ms", cpuMs = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds });
            var note = CreateNote()!; await Until(() => note.EditorLoaded);
            note.SetTestContent("ScrunchFX regression\nActual native ink / 21 September\n\nRemember the green notebook.", "mint", 360, 280);
            await Task.Delay(400);
            var record = note.Record!;
            samples.Add(new { stage = "native display", rasterScale = note.RasterScaleForCheck,
                displays = Microsoft.UI.Windowing.DisplayArea.FindAll().Count });
            await note.InvokeMenuDiscardForCheckAsync();
            await Until(() => !_windows.ContainsKey(record.Id));
            Check(note.LastDiscardOutcome == "Animated" && note.LastDiscardFrames > 5, "Native menu discard captures the real note and renders D3D frames");
            Check(record.DeletedAt != null && !fx.Active && !fx.Drawing, "Deletion persisted and renderer dormant");
            UndoDiscard(); await Until(() => _windows.TryGetValue(record.Id, out var w) && w.EditorLoaded);
            note = _windows[record.Id]; note.Discard(animate: true); UndoDiscard();
            await Until(() => !fx.Active);
            Check(record.DeletedAt == null && _windows[record.Id] == note && !note.IsDiscarding, "Undo during capture preserves the original editable window");
            note.SetTestContent("Editing after cancellation works", "peach", 300, 320);
            Check(record.Text == "Editing after cancellation works", "Editing still updates the record after cancellation");
            foreach (var sample in FxSamples)
            {
                note.SetTestContent(sample.Text, sample.Colour, sample.Width, sample.Height);
                await Task.Delay(150);
                await note.PlayDiscardAsync();
                Check(note.LastDiscardOutcome == "Animated" && !fx.Drawing && NormalText(record.Text) == NormalText(sample.Text),
                    $"{sample.Colour} {sample.Width}x{sample.Height}: capture, surface resize, playback and editing content preserved");
            }
            // Move the real WinUI note between the actual attached displays;
            // capture follows XamlRoot's native scale rather than a mocked DPI.
            var displays = Microsoft.UI.Windowing.DisplayArea.FindAll();
            // Indexed access avoids this SDK projection's IVectorView enumerator
            // cast failure on Windows 10; Count/GetAt are supported here.
            for (int displayIndex = 0; displayIndex < displays.Count; displayIndex++)
            {
                var area = displays[displayIndex].WorkArea;
                note.AppWindow.Move(new Windows.Graphics.PointInt32(area.X + 100, area.Y + 100));
                await Task.Delay(500);
                await note.PlayDiscardAsync();
                Check(note.LastDiscardOutcome == "Animated" && !fx.Drawing,
                    $"Display at {area.X},{area.Y}, scale {note.RasterScaleForCheck}: native capture and playback");
                samples.Add(new { stage = "display playback", x = area.X, y = area.Y, area.Width, area.Height, rasterScale = note.RasterScaleForCheck });
            }
            // Isolate renderer/capture retention from WinUI window recreation.
            bool retentionProbe = Environment.GetCommandLineArgs().Contains("--fx-retention-probe");
            for (int i = 0; i < (retentionProbe ? 0 : 30); i++)
            {
                ScrunchFxService.SeedOverride = FxVariation.GoldenSeeds[i % FxVariation.GoldenSeeds.Length];
                await note.PlayDiscardAsync();
                if (i % 10 == 9)
                {
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); await Task.Delay(200);
                    samples.Add(new { stage = "same live note, after GC", iteration = i + 1, resources = Resources(i + 1) });
                }
            }
            // Many ordinary notes must not create D3D devices or render callbacks.
            int devices = fx.DeviceCreations;
            for (int i = 0; i < 12; i++) CreateNote();
            await Task.Delay(3000);
            frameBefore = fx.TotalFrames; cpuBefore = process.TotalProcessorTime;
            await Task.Delay(1200);
            Check(fx.DeviceCreations == devices && fx.TotalFrames == frameBefore && !fx.Drawing, "13 ordinary notes share one dormant D3D service with zero FX frames during idle");
            samples.Add(new { stage = "13 notes idle 1200ms", cpuMs = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds });
            foreach (var extra in _windows.Values.Where(w => w.Record!.Id != record.Id).ToArray()) extra.Discard();
            // Warm up JIT/WinUI allocations, then sample three equal batches. Do
            // not force GC: this reflects the application's actual resource use.
            var closedNotes = new List<WeakReference<NoteWindow>>();
            var closedViews = new List<WeakReference<NoteView>>();
            for (int i = 0; i < (retentionProbe ? 10 : 40); i++)
            {
                ScrunchFxService.SeedOverride = FxVariation.GoldenSeeds[i % FxVariation.GoldenSeeds.Length];
                if (record.DeletedAt != null) { UndoDiscard(); await Until(() => _windows.TryGetValue(record.Id, out var w) && w.EditorLoaded); }
                note = _windows[record.Id]; note.Discard(animate: true);
                await Until(() => !_windows.ContainsKey(record.Id));
                closedNotes.Add(new WeakReference<NoteWindow>(note));
                closedViews.Add(note.ViewReferenceForCheck);
                Check(note.LastDiscardOutcome == "Animated", $"Repeat {i + 1}: native effect completes");
                if (i % 10 == 9)
                {
                    await Task.Delay(250); samples.Add(Resources(i + 1));
                    // Separate managed/WinRT deferred collection from retained
                    // native allocations; never run GC in production playback.
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                    await Task.Delay(250); samples.Add(new { stage = "after diagnostic full GC", iteration = i + 1, resources = Resources(i + 1),
                        liveClosedNotes = closedNotes.Count(w => w.TryGetTarget(out _)), liveClosedViews = closedViews.Count(w => w.TryGetTarget(out _)) });
                }
            }
            int retainedClosedNotes = closedNotes.Count(w => w.TryGetTarget(out _));
            int retainedClosedViews = closedViews.Count(w => w.TryGetTarget(out _));
            // Faults are deliberate and remain separate from quality/perf data.
            foreach (string failure in new[] { "initialization", "asset", "render" })
            {
                UndoDiscard(); await Until(() => _windows.TryGetValue(record.Id, out var w) && w.EditorLoaded);
                ScrunchFxService.InjectFailure = failure;
                note = _windows[record.Id]; note.Discard(animate: true);
                await Until(() => !_windows.ContainsKey(record.Id));
                Check(record.DeletedAt != null && note.LastDiscardOutcome.StartsWith("Fallback:") && !fx.Drawing && !fx.Active,
                    failure + " failure still deletes and releases active resources");
                ScrunchFxService.InjectFailure = null;
            }
            UndoDiscard(); await Until(() => _windows.TryGetValue(record.Id, out var w) && w.EditorLoaded);
            note = _windows[record.Id]; note.Discard(animate: true);
            await Until(() => !_windows.ContainsKey(record.Id));
            Check(note.LastDiscardOutcome == "Animated", "Device and rendering recover after injected faults");
            record.ReducedMotion = true;
            UndoDiscard(); await Until(() => _windows.TryGetValue(record.Id, out var w) && w.EditorLoaded);
            note = _windows[record.Id]; frameBefore = fx.TotalFrames;
            note.Discard(animate: true);
            Check(record.DeletedAt != null && !_windows.ContainsKey(record.Id) && note.LastDiscardFrames == 0 && fx.TotalFrames == frameBefore,
                "Reduced-motion product discard saves and closes immediately without FX frames");
            frameBefore = fx.TotalFrames; await Task.Delay(1200);
            Check(fx.TotalFrames == frameBefore && !fx.Drawing && !fx.Active, "Final idle has no frame callbacks");
            samples.Add(Resources(41));
            // Report retention after the independent fallback/recovery checks,
            // so one failure does not suppress their evidence. Threshold unchanged.
            Check(retainedClosedNotes <= 2, "Closed note windows are collectible after repeated deletion");
            Check(retainedClosedViews <= 2, "Closed note visual trees are collectible after repeated deletion");
        }
        catch (Exception exception) { error = exception.ToString(); }
        finally { ScrunchFxService.InjectFailure = null; ScrunchFxService.SeedOverride = null; }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "fx-verification.json"),
            JsonSerializer.Serialize(new { passed = error == null, checks, samples, adapter = fx.Adapter, error }, new JsonSerializerOptions { WriteIndented = true }));
        if (!Environment.GetCommandLineArgs().Contains("--inspect-note") && PrepareQuit()) Close();
    }
}
#endif
