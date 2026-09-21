namespace Noot_Proto.NootFX;

// One timeline for production and slowed inspection. No overshoot: the capture
// remains recognisable during gathering, settles compactly, then leaves.
internal readonly record struct DiscardPose(float Deformation, float Throw, float Opacity);
internal static class DiscardMotion
{
    public const double DurationMs = 760;
    public static DiscardPose At(float progress)
    {
        float p = Math.Clamp(progress, 0, 1);
        float gather = Smooth(p / .70f);
        float release = Math.Clamp((p - .75f) / .25f, 0, 1);
        float travel = release * release; // Accelerate away after the compact hold.
        float opacity = 1 - Smooth((release - .45f) / .55f);
        return new(gather, travel, opacity);
    }
    private static float Smooth(float value)
    {
        float t = Math.Clamp(value, 0, 1);
        return t * t * (3 - 2 * t);
    }
}
