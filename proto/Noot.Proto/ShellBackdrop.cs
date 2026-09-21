using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Noot_Proto;

// One instance per shell. XAML owns activation, theme and accessibility policy;
// the native controller owns blur, transparency policy and energy-saving fallback.
// The controller route lets us select Thin on App SDK 1.8 (the XAML convenience
// DesktopAcrylicBackdrop does not expose Kind in that version).
internal sealed class ShellBackdrop : SystemBackdrop
{
    private DesktopAcrylicController? _controller;
    private Windows.UI.Composition.Compositor? _fallbackCompositor;
    private Windows.UI.Composition.CompositionColorBrush? _fallback;
    internal bool ForceFallbackForCheck { get; init; }
    internal event Action? ConfigurationChanged;
    internal string State => _controller?.State.ToString() ??
        (ForceFallbackForCheck ? "Forced solid fallback" : "Unsupported / solid fallback");
    internal object Diagnostics => new
    {
        supported = DesktopAcrylicController.IsSupported(), state = State,
        kind = _controller?.Kind.ToString(), tint = _controller?.TintColor.ToString(),
        tintOpacity = _controller?.TintOpacity, luminosityOpacity = _controller?.LuminosityOpacity,
        fallback = _controller?.FallbackColor.ToString() ?? _fallback?.Color.ToString()
    };

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot root)
    {
        // System composition needs a Windows.System queue even when Acrylic is
        // unavailable and therefore has not created one on our behalf.
        DispatcherQueue.EnsureSystemDispatcherQueue();
        base.OnTargetConnected(target, root);
        if (DesktopAcrylicController.IsSupported() && !ForceFallbackForCheck)
        {
            _controller = new DesktopAcrylicController { Kind = DesktopAcrylicKind.Thin };
            _controller.SetSystemBackdropConfiguration(GetDefaultSystemBackdropConfiguration(target, root));
            ApplyPalette(root);
            _controller.AddSystemBackdropTarget(target);
        }
        else
        {
            _fallbackCompositor = new Windows.UI.Composition.Compositor();
            _fallback = _fallbackCompositor.CreateColorBrush(FallbackColour(root));
            target.SystemBackdrop = _fallback;
        }
    }

    private static Color FallbackColour(XamlRoot root) => new AccessibilitySettings().HighContrast
        ? new UISettings().GetColorValue(UIColorType.Background)
        : root.Content is FrameworkElement { ActualTheme: ElementTheme.Dark }
            ? Color.FromArgb(255, 32, 32, 32) : Color.FromArgb(255, 243, 243, 243);

    private void ApplyPalette(XamlRoot root)
    {
        if (_controller == null) return;
        bool dark = root.Content is FrameworkElement { ActualTheme: ElementTheme.Dark };
        // Custom values opt out of the controller's automatic theme palette.
        // Reapply on configuration changes; Windows still owns policy fallback.
        _controller.TintColor = dark ? Color.FromArgb(255, 32, 32, 32) : Color.FromArgb(255, 243, 243, 243);
        // A stronger neutral veil keeps warm wallpapers from colouring the
        // entire dark shell while still letting desktop variation show through.
        _controller.TintOpacity = dark ? .40f : .12f;
        _controller.LuminosityOpacity = dark ? .86f : .72f;
        _controller.FallbackColor = FallbackColour(root);
    }

    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot root)
    {
        // The default configuration is already updated and shared with the
        // controller. Its base notification rejects the target during a live
        // theme change on SDK 1.8; only our custom palette needs refreshing.
        ApplyPalette(root);
        if (_fallback != null) _fallback.Color = FallbackColour(root);
        ConfigurationChanged?.Invoke();
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        _controller?.RemoveSystemBackdropTarget(target);
        _controller?.Dispose(); _controller = null;
        if (_fallback != null) target.SystemBackdrop = null;
        _fallback?.Dispose(); _fallback = null;
        _fallbackCompositor?.Dispose(); _fallbackCompositor = null;
        base.OnTargetDisconnected(target);
    }
}
