namespace Scrunch.ScrunchFX;

// Version 1 seed contract: explicit integer mixing, independent of framework
// Random/GetHashCode implementations and of the order that effects are played.
public readonly record struct FxVariation(uint Seed, int Family, int Orientation,
    float DurationScale, float Hold, float Direction, float Travel, float Rotation, float Lift)
{
    public static readonly string[] Families = ["corner-crush", "side-scrunch", "centre-collapse"];
    public static readonly uint[] GoldenSeeds = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 42, 99, 314, 1337, 9787, 4399];
    public string FamilyName => Families[Family];
    public string OrientationName => Orientation switch { 1 => "Mirror X", 2 => "Mirror Y", 3 => "Mirror X + Y", _ => "Original" };
    public static FxVariation FromSeed(uint seed)
    {
        uint state = seed;
        float Next()
        {
            unchecked
            {
                state += 0x9e3779b9u;
                uint x = state;
                x = (x ^ (x >> 16)) * 0x21f0aaadu;
                x = (x ^ (x >> 15)) * 0x735a2d97u;
                x ^= x >> 15;
                return (x >> 8) * (1f / 16777215f);
            }
        }
        // Twelve consecutive seeds cover the family/orientation cross product.
        int family = (int)(seed % 3), orientation = (int)(seed / 3 % 4);
        float duration = .95f + .10f * Next(), hold = .035f + .03f * Next();
        float direction = Next() < .5f ? -1 : 1;
        return new(seed, family, orientation, duration, hold, direction,
            .24f + .06f * Next(), .40f + .18f * Next(), -.44f + .18f * Next());
    }
}
