namespace Noot_Proto.NootFX;

// One timeline for production and slowed inspection. No overshoot: the capture
// remains recognisable during gathering, settles compactly, then leaves.
internal readonly record struct DiscardPose(float Deformation, float Throw, float Opacity);
internal static class DiscardMotion
{
    public const double DurationMs = 760;
    public static DiscardPose At(float progress) => At(progress, .05f);
    public static DiscardPose At(float progress, float hold)
    {
        float p = Math.Clamp(progress, 0, 1);
        float gather = Smooth(p / .70f);
        float releaseStart = .70f + hold;
        float release = Math.Clamp((p - releaseStart) / (1 - releaseStart), 0, 1);
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
