using System.Collections.Generic;
using Microsoft.UI;
using Windows.UI;

namespace Scrunch;

// Single source of truth for the paper feel. Carried into the real build unchanged.
//
// Shadow is a pre-rendered texture (Assets/ShadowPaper.png), not a live
// DropShadow: identical motion language (offset/scale/opacity per state),
// zero compositor shadow cost, and it survives any host.
internal sealed record ShadowSpec(float OffsetY, float Scale, float Opacity);

internal static class PaperTokens
{
    public static readonly IReadOnlyDictionary<string, Color> Colours = new Dictionary<string, Color>
    {
        ["yellow"] = ColorHelper.FromArgb(255, 0xFD, 0xEE, 0x9E),
        ["pink"] = ColorHelper.FromArgb(255, 0xF9, 0xCF, 0xCF),
        ["mint"] = ColorHelper.FromArgb(255, 0xC4, 0xEB, 0xD3),
        ["blue"] = ColorHelper.FromArgb(255, 0xC5, 0xDF, 0xF5),
        ["lavender"] = ColorHelper.FromArgb(255, 0xD9, 0xD2, 0xF0),
        ["peach"] = ColorHelper.FromArgb(255, 0xFB, 0xD9, 0xB8),
    };

    public const string DefaultColour = "yellow";

    // Resting handmade imperfection. Visual-only; the OS window stays axis-aligned.
    public const float RestTilt = -0.8f;

    // Texture carries the blur; state animates placement + presence.
    public static readonly ShadowSpec ShadowTight = new(4f, 0.98f, 0.45f); // peel start
    public static readonly ShadowSpec ShadowRest = new(10f, 1.0f, 0.8f);
    public static readonly ShadowSpec ShadowLifted = new(24f, 1.07f, 1.0f); // dragging
    public static readonly ShadowSpec ShadowBall = new(4f, 0.4f, 0.45f); // crumpled ball

    // Texture overhang around the paper; LayoutShadow keeps it concentric.
    public const double ShadowPad = 120.0;

    public const int PeelMs = 220;
    public const int LiftMs = 120;
    public const int FallMs = 180;
    public const int CrumpleMs = 190;
    public const int ThrowMs = 180;
    public const int ThrowDelayMs = 100; // let the gathering read before release; 280ms total
    public const int UndoSeconds = 4;

    public const float SpringDamping = 0.75f;
    public const int SpringPeriodMs = 180;

    // Drag tilt follows horizontal velocity, clamped. Sign verified by feel.
    public const float TiltGain = 0.0035f;
    public const float TiltMin = -4.5f;
    public const float TiltMax = 3.5f;

    public const double DefaultWidth = 300;
    public const double DefaultHeight = 320;
    public const double MinWidth = 200;
    public const double MaxWidth = 480;
    public const double MinHeight = 160;
    public const double MaxHeight = 480;
}
