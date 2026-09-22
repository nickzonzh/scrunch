using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Scrunch.ScrunchFX;
using Xunit;

namespace Scrunch.Tests;

// Prepared assets, seed contract and discard timeline. The prepared samples are
// half precision on disk, so geometry comparisons allow one fp16 step (about
// 2.4e-4 at unit magnitude) where the authoring maths is exact.
public sealed class FxTests : IDisposable
{
    private const float HalfStep = 5e-4f;
    private const int Side = 25;
    private static readonly string Assets = Path.Combine(AppContext.BaseDirectory, "Assets", "ScrunchFX");
    private static readonly Dictionary<string, PaperBake> Loaded =
        FxVariation.Families.ToDictionary(name => name, name => new PaperBake(Path.Combine(Assets, name + ".nfx")));
    private static PaperBake Bake(string family = "corner-crush") => Loaded[family];
    private static Vector3 Position(PaperBake bake, int frame, int id)
    {
        var p = bake.Samples[(frame * bake.VertexCount + id) * 2];
        return new Vector3(p.X, p.Y, p.Z);
    }
    private static Vector3 Normal(PaperBake bake, int frame, int id)
    {
        var n = bake.Samples[(frame * bake.VertexCount + id) * 2 + 1];
        return new Vector3(n.X, n.Y, n.Z);
    }
    private readonly string _temporary = Path.Combine(Path.GetTempPath(), "scrunch-tests-" + Guid.NewGuid());
    public void Dispose() { if (Directory.Exists(_temporary)) Directory.Delete(_temporary, true); }

    [Fact]
    public void PreparedLatticeAndFrameCountsAreFixed()
    {
        var bake = Bake();
        Assert.Equal(625, bake.VertexCount);
        Assert.Equal(61, bake.FrameCount);
        Assert.Equal(3456, bake.Indices.Length);
        Assert.Equal(Side * Side, bake.VertexCount);
    }

    [Fact]
    public void FlatFrameMatchesTheNoteUVs()
    {
        // No flipped or stretched handoff: frame zero is the captured rectangle.
        var bake = Bake();
        float error = 0;
        for (int id = 0; id < bake.VertexCount; id++)
        {
            var p = Position(bake, 0, id);
            error = Math.Max(error, Vector2.Distance(new Vector2(p.X + .5f, p.Y + .5f), bake.UVs[id]));
        }
        Assert.InRange(error, 0, .002f);
    }

    [Fact]
    public void BakeStartsPlanarAndEndsCompact()
    {
        var bake = Bake();
        var first = Extent(bake, 0);
        var final = Extent(bake, bake.FrameCount - 1);
        Assert.True(first.Z < .01f, $"initial depth {first.Z}");
        Assert.True(final.Z > .15f, $"final depth {final.Z}");
        Assert.True(final.X < first.X * .6f && final.Y < first.Y * .6f, $"final extent {final} against {first}");
    }

    private static Vector3 Extent(PaperBake bake, int frame)
    {
        var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
        for (int id = 0; id < bake.VertexCount; id++)
        {
            var p = Position(bake, frame, id);
            min = Vector3.Min(min, p); max = Vector3.Max(max, p);
        }
        return max - min;
    }

    [Theory]
    [InlineData("corner-crush")]
    [InlineData("side-scrunch")]
    [InlineData("centre-collapse")]
    public void EveryBakedNormalIsUnitLength(string family)
    {
        var bake = Bake(family);
        float error = 0;
        for (int frame = 0; frame < bake.FrameCount; frame++)
            for (int id = 0; id < bake.VertexCount; id++)
                error = Math.Max(error, Math.Abs(Normal(bake, frame, id).Length() - 1));
        Assert.InRange(error, 0, .001f);
    }

    [Theory]
    [InlineData("side-scrunch")]
    [InlineData("centre-collapse")]
    public void FamiliesShareTopologyUVsAndFrames(string family)
    {
        var bake = Bake(); var other = Bake(family);
        Assert.Equal(bake.FrameCount, other.FrameCount);
        Assert.Equal(bake.UVs, other.UVs);
        Assert.Equal(bake.Indices, other.Indices);
    }

    [Theory]
    [InlineData("corner-crush")]
    [InlineData("side-scrunch")]
    [InlineData("centre-collapse")]
    public void EveryGeometryReflectionKeepsTheOriginalFlatAppearance(string family)
    {
        // The renderer reflects the deformation field and samples the original
        // vertex UV, so all four orientations must read as unmirrored ink.
        var bake = Bake(family);
        for (int orientation = 0; orientation < 4; orientation++)
            for (int id = 0; id < bake.VertexCount; id++)
            {
                int x = id % Side, y = id / Side;
                int mx = (orientation & 1) != 0 ? Side - 1 - x : x, my = (orientation & 2) != 0 ? Side - 1 - y : y;
                var p = Position(bake, 0, my * Side + mx);
                p.X *= (orientation & 1) != 0 ? -1 : 1; p.Y *= (orientation & 2) != 0 ? -1 : 1;
                Assert.InRange(Vector2.Distance(new Vector2(p.X + .5f, p.Y + .5f), bake.UVs[id]), 0, HalfStep);
            }
    }

    [Theory]
    [InlineData("corner-crush")]
    [InlineData("side-scrunch")]
    [InlineData("centre-collapse")]
    public void FamilyEndsCompactWithBoundedMotionAndNoStretchedSpikes(string family)
    {
        var bake = Bake(family);
        float step = 0, stretch = 0;
        for (int frame = 0; frame < bake.FrameCount; frame++)
        {
            if (frame > 0)
                for (int id = 0; id < bake.VertexCount; id++)
                    step = Math.Max(step, Vector3.Distance(Position(bake, frame, id), Position(bake, frame - 1, id)));
            for (int t = 0; t < bake.Indices.Length; t += 3)
                for (int e = 0; e < 3; e++)
                {
                    int a = (int)bake.Indices[t + e], b = (int)bake.Indices[t + (e + 1) % 3];
                    stretch = Math.Max(stretch, Vector3.Distance(Position(bake, frame, a), Position(bake, frame, b)) /
                        Vector2.Distance(bake.UVs[a], bake.UVs[b]));
                }
        }
        var extent = Extent(bake, bake.FrameCount - 1);
        Assert.True(extent.X < .52f && extent.Y < .52f, $"final extent {extent}");
        Assert.InRange(extent.Z, .20f, .52f);
        Assert.True(step < .15f, $"max inter-frame step {step}");
        Assert.True(stretch < 2, $"max edge stretch {stretch}");
    }

    [Fact]
    public void EveryRestPanelHasConsistentOrientation()
    {
        var bake = Bake();
        for (int t = 0; t < bake.Indices.Length; t += 3)
        {
            Vector2 a = bake.UVs[bake.Indices[t]], b = bake.UVs[bake.Indices[t + 1]], c = bake.UVs[bake.Indices[t + 2]];
            Assert.True((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X) > 0, $"inverted panel at {t}");
        }
    }

    [Theory]
    [InlineData("magic")]
    [InlineData("dimension")]
    [InlineData("truncated")]
    [InlineData("index")]
    [InlineData("nan")]
    public void InvalidAssetsAreRejectedBeforeGpuAllocation(string fault)
    {
        var bake = Bake();
        var bytes = File.ReadAllBytes(Path.Combine(Assets, "corner-crush.nfx"));
        switch (fault)
        {
            case "magic": bytes[3] = (byte)'1'; break; // a retired NFX1 file
            case "dimension": BitConverter.GetBytes(int.MaxValue).CopyTo(bytes, 4); break;
            case "truncated": bytes = bytes[..^1]; break;
            case "index": BitConverter.GetBytes(uint.MaxValue).CopyTo(bytes, 16 + bake.VertexCount * 8); break;
            case "nan": BitConverter.GetBytes(float.NaN).CopyTo(bytes, 16); break;
        }
        Directory.CreateDirectory(_temporary);
        string path = Path.Combine(_temporary, fault + ".nfx");
        File.WriteAllBytes(path, bytes);
        Assert.Throws<InvalidDataException>(() => new PaperBake(path));
    }

    [Fact]
    public void ValidLengthIsTheOnlyAcceptedLength()
    {
        // A half-precision sample section is 12 bytes per point per frame; the
        // float32 layout NFX1 used would be exactly 32.
        var bake = Bake();
        Assert.Equal(16 + bake.VertexCount * 8 + bake.Indices.Length * 4 + bake.VertexCount * bake.FrameCount * 12,
            new FileInfo(Path.Combine(Assets, "corner-crush.nfx")).Length);
        var bytes = File.ReadAllBytes(Path.Combine(Assets, "corner-crush.nfx"));
        Directory.CreateDirectory(_temporary);
        string path = Path.Combine(_temporary, "padded.nfx");
        File.WriteAllBytes(path, [.. bytes, 0, 0]);
        Assert.Throws<InvalidDataException>(() => new PaperBake(path));
    }

    [Fact]
    public void DiscardStartsFlatStationaryAndOpaque() =>
        Assert.Equal(new DiscardPose(0, 0, 1), DiscardMotion.At(0));

    [Fact]
    public void DiscardIsMonotonicContinuousAndFinishesGone()
    {
        var last = DiscardMotion.At(0);
        for (int i = 1; i <= 1000; i++)
        {
            var pose = DiscardMotion.At(i / 1000f);
            Assert.True(pose.Deformation >= last.Deformation && pose.Throw >= last.Throw && pose.Opacity <= last.Opacity,
                $"motion reverses at {i}: {pose} after {last}");
            Assert.InRange(pose.Deformation, 0, 1);
            Assert.InRange(pose.Throw, 0, 1);
            Assert.InRange(pose.Opacity, 0, 1);
            Assert.True(pose.Throw == 0 || pose.Deformation == 1, $"throw before the crumple completes at {i}");
            Assert.True(pose.Deformation - last.Deformation <= .003f && pose.Throw - last.Throw <= .009f &&
                last.Opacity - pose.Opacity <= .012f, $"discontinuous motion at {i}: {pose} after {last}");
            last = pose;
        }
        Assert.Equal(new DiscardPose(1, 1, 0), last);
    }

    [Fact]
    public void CompactPaperHoldsBeforeRelease()
    {
        Assert.Equal(new DiscardPose(1, 0, 1), DiscardMotion.At(.72f));
        Assert.Equal(new DiscardPose(1, 0, 1), DiscardMotion.At(DiscardMotion.GatherEnd));
        Assert.Equal(760, DiscardMotion.DurationMs);
    }

    [Fact]
    public void MainCollapseReadsLongerThanAPlainSmoothstep()
    {
        // Baseline: the retired whole-gather smoothstep over the same 70%.
        static float TimeAt(float deformation, bool baseline)
        {
            float low = 0, high = 1;
            for (int i = 0; i < 24; i++)
            {
                float p = (low + high) / 2, t = Math.Clamp(p / DiscardMotion.GatherEnd, 0, 1);
                float d = baseline ? t * t * (3 - 2 * t) : DiscardMotion.At(p).Deformation;
                if (d < deformation) low = p; else high = p;
            }
            return high * (float)DiscardMotion.DurationMs;
        }
        float before = TimeAt(.85f, true) - TimeAt(.35f, true);
        float after = TimeAt(.85f, false) - TimeAt(.35f, false);
        Assert.True(after > before * 1.15f, $"collapse {before} -> {after}ms");
        Assert.True(TimeAt(.2f, false) < TimeAt(.2f, true), "the initial buckle must arrive earlier");
    }

    [Fact]
    public void GoldenSeedsCoverEveryFamilyAndOrientation()
    {
        var pairs = FxVariation.GoldenSeeds.Select(FxVariation.FromSeed).Select(v => (v.Family, v.Orientation)).Distinct();
        Assert.Equal(12, pairs.Count());
    }

    [Fact]
    public void SeedReferenceVectorIsStable() =>
        Assert.Equal(new FxVariation(0, 0, 0, .9892125f, .060517795f, 1, .26991993f, .5380957f, -.2699675f),
            FxVariation.FromSeed(0));

    [Fact]
    public void FullSeedRangeIncludingTheWrapBoundaryIsMapped()
    {
        var wrap = FxVariation.FromSeed(uint.MaxValue);
        Assert.Equal(0, wrap.Family);
        Assert.Equal(1, wrap.Orientation);
    }

    [Fact]
    public void EverySeedStaysBoundedAndKeepsACoherentTimeline()
    {
        for (uint seed = 0; seed < 10000; seed++)
        {
            var v = FxVariation.FromSeed(seed);
            Assert.Equal(v, FxVariation.FromSeed(seed));
            Assert.InRange(v.DurationScale, .95f, 1.05f);
            Assert.InRange(v.Hold, .035f, .065f);
            Assert.Equal(1, Math.Abs(v.Direction));
            Assert.InRange(v.Travel, .24f, .30f);
            Assert.InRange(v.Rotation, .40f, .58f);
            Assert.InRange(v.Lift, -.44f, -.26f);
            Assert.Equal(FxVariation.Families[v.Family], v.FamilyName);
            var previous = DiscardMotion.At(0, v.Hold);
            for (int t = 1; t <= 100; t++)
            {
                var pose = DiscardMotion.At(t / 100f, v.Hold);
                Assert.True(pose.Deformation >= previous.Deformation && pose.Throw >= previous.Throw &&
                    pose.Opacity <= previous.Opacity && (pose.Throw == 0 || pose.Deformation == 1),
                    $"seed {seed} timeline invalid at {t}: {pose} after {previous}");
                previous = pose;
            }
        }
    }

    [Fact]
    public void NativeCaptureAndTestsShareOneGoldenSeedFixture()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "fx-golden-seeds.json")));
        Assert.Equal(FxVariation.GoldenSeeds, fixture.RootElement.GetProperty("seeds").EnumerateArray().Select(x => x.GetUInt32()));
    }

    [Fact]
    public void GoldenSeedsReplayByteExactlyAfterUnrelatedSeedsAndFreshAssetLoads()
    {
        var seeds = FxVariation.GoldenSeeds.Append(uint.MaxValue).ToArray();
        var fingerprints = seeds.ToDictionary(seed => seed, Fingerprint);
        foreach (uint seed in seeds.Reverse())
        {
            _ = Fingerprint(unchecked(seed + 101));
            Assert.Equal(fingerprints[seed], Fingerprint(seed));
        }
        Assert.Equal(seeds.Length, fingerprints.Values.Distinct().Count());
    }

    // The selected and reflected prepared path, normals, original ink UVs and
    // elapsed-time poses, not merely that FromSeed equals itself. Reloads the
    // asset so a stale buffer or load-order dependency would change the hash.
    private static string Fingerprint(uint seed)
    {
        var v = FxVariation.FromSeed(seed);
        var prepared = new PaperBake(Path.Combine(Assets, v.FamilyName + ".nfx"));
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(JsonSerializer.Serialize(v));
        for (int ms = 0; ms <= 800; ms++)
        {
            var pose = DiscardMotion.At((float)(ms / (DiscardMotion.DurationMs * v.DurationScale)), v.Hold);
            writer.Write(pose.Deformation); writer.Write(pose.Throw); writer.Write(pose.Opacity);
        }
        int sx = (v.Orientation & 1) == 0 ? 1 : -1, sy = (v.Orientation & 2) == 0 ? 1 : -1;
        for (int frame = 0; frame < prepared.FrameCount; frame++)
            for (int id = 0; id < prepared.VertexCount; id++)
            {
                int x = id % Side, y = id / Side;
                int reflected = (sy < 0 ? Side - 1 - y : y) * Side + (sx < 0 ? Side - 1 - x : x);
                writer.Write(prepared.UVs[id].X); writer.Write(prepared.UVs[id].Y);
                for (int component = 0; component < 2; component++)
                {
                    var sample = prepared.Samples[(frame * prepared.VertexCount + reflected) * 2 + component];
                    writer.Write(sample.X * sx); writer.Write(sample.Y * sy); writer.Write(sample.Z);
                }
            }
        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }
}
