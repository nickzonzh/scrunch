using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
namespace Noot_Proto;
public sealed partial class MainPage : Page
{
    private readonly List<NoteWindow> _notes = new();
    private readonly DispatcherTimer _stats = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Process _process = Process.GetCurrentProcess();
    private TimeSpan _previousCpu;
    private long _previousTick;
    public MainPage()
    {
        InitializeComponent();
        FeelPicker.SelectedIndex = 1;
        Note.SetFeel(1);
        FeelDescription.Text = "Convincing paper. A little personality in your hands.";
        Note.StatusChanged += (_, message) => StatusText.Text = message;
        _stats.Tick += (_, _) => UpdateStats();
        Loaded += (_, _) => { _previousCpu = _process.TotalProcessorTime; _previousTick = Environment.TickCount64; _stats.Start(); };
        Loaded += async (_, _) => { if (Environment.GetCommandLineArgs().Contains("--verify")) await VerifyAsync(); };
        Unloaded += (_, _) => { _stats.Stop(); foreach (var note in _notes.ToArray()) note.Close(); _process.Dispose(); };
    }
    private void Feel_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (Note == null || FeelDescription == null) return;
        Note.SetFeel(FeelPicker.SelectedIndex);
        FeelDescription.Text = FeelPicker.SelectedIndex switch
        {
            0 => "Quiet, light and close to real stationery.",
            2 => "Bigger bends and a more expressive spring.",
            _ => "Convincing paper. A little personality in your hands."
        };
    }
    private void Swatch_Click(object sender, RoutedEventArgs e) => Note.SetColour((string)((Button)sender).Tag);
    private async void Peel_Click(object sender, RoutedEventArgs e) { BendSlider.Value = 0; await Note.PeelAsync(); }
    private void Lift_Click(object sender, RoutedEventArgs e) { BendSlider.Value = 0; Note.Lift(); }
    private void Release_Click(object sender, RoutedEventArgs e) { BendSlider.Value = 0; Note.Release(); }
    private void Bend_Changed(object sender, RangeBaseValueChangedEventArgs e) => Note?.PreviewBend((float)e.NewValue);
    private async void Crumple_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (Note != null) await Note.PreviewCrumpleAsync((float)e.NewValue);
    }
    private async void Crumple_Click(object sender, RoutedEventArgs e)
    {
        await Note.DiscardAsync();
        StatusText.Text = "Discard preview complete. Centre restores the paper; the slider lets you inspect the folds.";
    }
    private void Motion_Toggled(object sender, RoutedEventArgs e) => Note?.SetReducedMotion(MotionSwitch.IsOn);
    private async void Reset_Click(object sender, RoutedEventArgs e) { BendSlider.Value = 0; await Note.ResetAsync(); }
    private void Background_Click(object sender, RoutedEventArgs e) => Stage.Background = new SolidColorBrush(
        (string)((Button)sender).Tag == "dark" ? Color.FromArgb(255, 38, 41, 38) : Color.FromArgb(255, 233, 229, 220));
    private void Float_Click(object sender, RoutedEventArgs e)
    {
        var window = new NoteWindow(Note.FeelIndex, Note.ColourKey, Note.Text, Note.ReducedMotion);
        _notes.Add(window);
        window.Closed += (_, _) => _notes.Remove(window);
        window.AppWindow.Show();
        window.Activate();
    }
    private void UpdateStats()
    {
        _process.Refresh();
        long now = Environment.TickCount64;
        var cpu = _process.TotalProcessorTime;
        double percent = (cpu - _previousCpu).TotalMilliseconds / Math.Max(1, now - _previousTick) * 100;
        _previousCpu = cpu; _previousTick = now;
        ResourceText.Text = $"{_process.WorkingSet64 / 1048576.0:F0} MB working set · {percent:F1}% of one CPU core · " +
            $"{(Note.IsAnimating ? "paper moving" : "paper renderer asleep")} · {_notes.Count} floating";
    }

    // Opt-in integration checks run through the actual native controls/renderer.
    // They do not claim to test pointer delivery, compositor pixels or GPU usage.
    private async Task VerifyAsync()
    {
        var checks = new List<string>();
        string? error = null;
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            checks.Add(label);
        }
        async Task Idle()
        {
            for (int i = 0; i < 80 && Note.IsAnimating; i++) await Task.Delay(50);
            Check(!Note.IsAnimating, "Renderer unsubscribes after settling");
        }
        try
        {
            await Task.Delay(800);
            Check(Note.HasPaddedMotionViewport, "Composition viewport includes padding beyond every paper edge");
            for (int i = 0; i < 3; i++)
            {
                FeelPicker.SelectedIndex = i;
                await Note.SetPreviewAsync(0.65f);
                Check(Note.FeelIndex == i && Note.MeshVisible && Note.TextureDigest.Length == 64, $"Preset {i}: textured mesh available");
                Check(!Note.IsAnimating, $"Preset {i}: held inspection does not tick");
                Note.Release(); await Idle();
            }
            var before = Note.TextureDigest;
            Note.Text = "Remember the good stuff.\n\nMilk.\nA walk outside.\nThat idea from yesterday.";
            Check(!Note.MeshVisible, "Typing returns to native editing immediately");
            await Note.SetPreviewAsync(0.6f);
            Check(Note.TextureDigest != before, "Edited text produces a fresh texture");
            Note.SetColour("pink");
            var previous = Note.TextureDigest;
            await Note.SetPreviewAsync(0.6f);
            Check(Note.TextureDigest != previous, "Colour changes refresh the texture");
            await Note.PeelAsync();
            Note.SetReducedMotion(true);
            Check(!Note.IsAnimating && !Note.MeshVisible, "Reduced motion interrupts an active peel");
            await Note.SetPreviewAsync(0.8f);
            Check(!Note.MeshVisible && !Note.IsAnimating, "Reduced motion blocks geometric previews");
            Note.SetReducedMotion(false);
            await Note.PreviewCrumpleAsync(0.6f);
            Check(Note.MeshVisible && !Note.IsAnimating, "Held crumple pose has a texture and does not tick");
            var discardTexture = Note.TextureDigest;
            await Note.DiscardAsync();
            Check(!Note.IsAnimating && Note.DiscardFrames > 0 && Note.TextureDigest == discardTexture, "Crumple and throw renders frames, finishes and reuses the texture");
            Note.CancelDiscard();
            var interruptedDiscard = Note.DiscardAsync();
            await Task.Delay(40);
            Note.CancelDiscard();
            Check(await Task.WhenAny(interruptedDiscard, Task.Delay(1000)) == interruptedDiscard && !Note.IsAnimating && !Note.MeshVisible,
                "Undo cancels discard and restores native editing");
            var reducedDiscard = Note.DiscardAsync();
            await Task.Delay(40);
            Note.SetReducedMotion(true);
            Check(await Task.WhenAny(reducedDiscard, Task.Delay(1000)) == reducedDiscard && !Note.IsAnimating && !Note.MeshVisible,
                "Changing reduced motion settles a pending discard task");
            await Note.DiscardAsync();
            Check(!Note.IsAnimating && !Note.MeshVisible, "Reduced motion discards without animation");
            Note.SetReducedMotion(false);
            await Note.PeelAsync();
            await Note.ResetAsync();
            Check(!Note.IsAnimating && !Note.MeshVisible, "Reset cancels motion and restores editing");
            Note.SetColour("yellow");
            FeelPicker.SelectedIndex = 1;
            await Note.PeelAsync(); await Idle();
            _stats.Stop();
            _process.Refresh();
            var cpu = _process.TotalProcessorTime;
            long start = Environment.TickCount64;
            await Task.Delay(3000);
            _process.Refresh();
            double idleCpu = (_process.TotalProcessorTime - cpu).TotalMilliseconds / (Environment.TickCount64 - start) * 100;
            checks.Add($"Idle sample: {_process.WorkingSet64 / 1048576.0:F1} MB working set; {idleCpu:F2}% of one CPU core over 3 seconds (GPU not measured)");
            _stats.Start();
            // Leave a representative bend on screen for visual inspection.
            await Note.SetPreviewAsync(0.65f);
            if (Environment.GetCommandLineArgs().Contains("--inspect-crumple"))
            {
                CrumpleSlider.Focus(FocusState.Programmatic);
                CrumpleSlider.Value = 0.8;
                await Note.PreviewCrumpleAsync(0.8f);
            }
            var floating = new NoteWindow(1, "mint", "A little reminder.\n\nMove me around.");
            _notes.Add(floating);
            floating.Closed += (_, _) => _notes.Remove(floating);
            floating.AppWindow.Show();
            floating.Activate();
            Check(floating.AppWindow.Size.Width > 300, "Floating window created with padded bounds");
            if (!Environment.GetCommandLineArgs().Contains("--inspect-crumple"))
            {
                await floating.InvokeMenuDiscardForCheckAsync();
                await Task.Delay(2000);
                Check(floating.LastDiscardOutcome == "Animated" && floating.LastDiscardFrames > 0,
                    "Temporary bench note also animates through the native menu");
            }
        }
        catch (Exception exception) { error = exception.ToString(); }
        var result = new { passed = error == null, checks, error, timestamp = DateTimeOffset.Now };
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "verification.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        StatusText.Text = error == null ? $"{checks.Count} native checks complete. Ready for a feel test." : $"Verification failed: {error}";
        if (Environment.GetCommandLineArgs().Contains("--verify-exit")) Application.Current.Exit();
    }
}
