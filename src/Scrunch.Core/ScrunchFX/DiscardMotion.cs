namespace Scrunch.ScrunchFX;

// One timeline for production and slowed inspection. No overshoot: the capture
// remains recognisable during gathering, settles compactly, then leaves.
public readonly record struct DiscardPose(float Deformation, float Throw, float Opacity);
public static class DiscardMotion
{
    public const double DurationMs = 760;
    public const float GatherEnd = .70f;

    // Shape and rigid motion that the prepared bake does not carry. The vertex
    // path and its desktop shadow quad both converge the captured rectangle
    // toward a square as it gathers; the shadow used to repeat the fully
    // gathered value as a literal. Travel, in-plane rotation and vertical
    // departure are seeded per effect (FxVariation); these are fixed.
    public const float GatherOnset = .15f; // deformation at which convergence starts
    public const float GatherConvergence = .85f; // fraction of the way to the geometric mean
    public const float DepthRotation = .24f; // radians about Y at full release, signed by direction
    public const float ThrowArc = .65f; // downward t^2 term, in units of throw reach
    public const float ThrowDepth = .7f; // recession from the camera, in units of throw reach
    public static DiscardPose At(float progress) => At(progress, .05f);
    public static DiscardPose At(float progress, float hold)
    {
        float p = Math.Clamp(progress, 0, 1);
        float gather = Gather(p / GatherEnd);
        float releaseStart = GatherEnd + hold;
        float release = Math.Clamp((p - releaseStart) / (1 - releaseStart), 0, 1);
        float travel = release * release; // Accelerate away after the compact hold.
        float opacity = 1 - Smooth((release - .45f) / .55f);
        return new(gather, travel, opacity);
    }
    private static float Gather(float value)
    {
        // Short acceleration, steady readable compression, tiny deceleration.
        // Integrating this trapezoidal velocity keeps position and speed
        // continuous while spending less time on the almost-flat bake frames.
        const float entry = .12f, settle = .18f;
        const float speed = 1 / (1 - (entry + settle) / 2);
        float t = Math.Clamp(value, 0, 1);
        if (t < entry) return speed * t * t / (2 * entry);
        if (t > 1 - settle) return 1 - speed * (1 - t) * (1 - t) / (2 * settle);
        return speed * (t - entry / 2);
    }
    private static float Smooth(float value)
    {
        float t = Math.Clamp(value, 0, 1);
        return t * t * (3 - 2 * t);
    }
}
