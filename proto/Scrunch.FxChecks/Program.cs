using System.Numerics;
using Scrunch.ScrunchFX;

string path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../Scrunch/Assets/ScrunchFX/corner-crush.nfx"));
if (!File.Exists(path)) path = Path.GetFullPath("proto/Scrunch/Assets/ScrunchFX/corner-crush.nfx");
var bake = new PaperBake(path);
void Check(bool success, string label) { if (!success) throw new Exception(label); Console.WriteLine("PASS: " + label); }
Check(bake.VertexCount == 625 && bake.FrameCount == 61 && bake.Indices.Length == 3456, "Prepared Scrunch lattice and frame counts");
float maxUvError = 0;
for (int i = 0; i < bake.VertexCount; i++)
{
    var p = bake.Samples[i * 2]; var uv = bake.UVs[i];
    maxUvError = Math.Max(maxUvError, Vector2.Distance(new(p.X + .5f, p.Y + .5f), uv));
}
Check(maxUvError < .002, "Flat positions align with note UVs (no flipped or stretched handoff)");
Vector3 Extents(int frame)
{
    var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
    for (int i = 0; i < bake.VertexCount; i++) { var p = bake.Samples[(frame * bake.VertexCount + i) * 2]; var v = new Vector3(p.X, p.Y, p.Z); min = Vector3.Min(min, v); max = Vector3.Max(max, v); }
    return max - min;
}
var first = Extents(0); var final = Extents(bake.FrameCount - 1);
Check(first.Z < .01 && final.Z > .15 && final.X < first.X * .6 && final.Y < first.Y * .6, "Bake loses planar shape and ends as a compact 3D object");
for (int f = 0; f < bake.FrameCount; f++)
    for (int v = 0; v < bake.VertexCount; v++)
    {
        var n = bake.Samples[(f * bake.VertexCount + v) * 2 + 1];
        if (Math.Abs(new Vector3(n.X,n.Y,n.Z).Length() - 1) > .001) throw new Exception("Invalid normal");
    }
Check(true, "Every baked normal is normalized");
var bytes = File.ReadAllBytes(path); string temporary = Path.GetTempFileName();
try
{
    foreach (string fault in new[] { "magic", "dimension", "truncated", "index", "nan" })
    {
        var broken = bytes.ToArray();
        if (fault == "magic") broken[0] = 0;
        if (fault == "dimension") BitConverter.GetBytes(int.MaxValue).CopyTo(broken,4);
        if (fault == "truncated") broken = broken[..^1];
        if (fault == "index") BitConverter.GetBytes(uint.MaxValue).CopyTo(broken,16+bake.VertexCount*8);
        if (fault == "nan") BitConverter.GetBytes(float.NaN).CopyTo(broken,16);
        File.WriteAllBytes(temporary,broken);
        try { _ = new PaperBake(temporary); throw new Exception("Accepted invalid " + fault); }
        catch (InvalidDataException) { Check(true, "Rejects " + fault + " asset before GPU allocation"); }
    }
}
finally { File.Delete(temporary); }
Console.WriteLine($"Flat UV error: {maxUvError}; initial extents: {first}; final extents: {final}");
var last = DiscardMotion.At(0);
Check(last == new DiscardPose(0, 0, 1), "Handoff starts flat, stationary and fully opaque");
for (int i = 1; i <= 1000; i++)
{
    var pose = DiscardMotion.At(i / 1000f);
    if (pose.Deformation < last.Deformation || pose.Throw < last.Throw || pose.Opacity > last.Opacity ||
        pose.Deformation > 1 || pose.Throw > 1 || pose.Opacity < 0) throw new Exception("Motion reverses or overshoots");
    if (pose.Throw > 0 && pose.Deformation != 1) throw new Exception("Throw starts before crumple completes");
    if (pose.Deformation - last.Deformation > .003 || pose.Throw - last.Throw > .009 || last.Opacity - pose.Opacity > .012)
        throw new Exception("Discontinuous motion");
    last = pose;
}
Check(last == new DiscardPose(1, 1, 0), "Continuous discard finishes compact, translated and invisible");
Check(DiscardMotion.At(.72f) == new DiscardPose(1, 0, 1), "Compact paper holds briefly before release");
float TimeAt(float deformation, bool baseline)
{
    float low = 0, high = 1;
    for (int i = 0; i < 24; i++)
    {
        float p = (low + high) / 2, t = Math.Clamp(p / .70f, 0, 1);
        float d = baseline ? t * t * (3 - 2 * t) : DiscardMotion.At(p).Deformation;
        if (d < deformation) low = p; else high = p;
    }
    return high * (float)DiscardMotion.DurationMs;
}
float collapseBefore = TimeAt(.85f, true) - TimeAt(.35f, true);
float collapseAfter = TimeAt(.85f, false) - TimeAt(.35f, false);
Check(collapseAfter > collapseBefore * 1.15f && TimeAt(.2f, false) < TimeAt(.2f, true),
    "Main collapse gains at least 15% reading time; initial buckle arrives earlier");
Check(DiscardMotion.DurationMs == 760 && DiscardMotion.At(.70f) == new DiscardPose(1, 0, 1),
    "Timing redistribution preserves total duration and compact arrival");
Console.WriteLine($"Nominal collapse (deformation .35-.85): {collapseBefore:F2} -> {collapseAfter:F2}ms");

foreach (var name in FxVariation.Families)
{
    var family = new PaperBake(Path.Combine(Path.GetDirectoryName(path)!, name + ".nfx"));
    Check(family.UVs.SequenceEqual(bake.UVs) && family.Indices.SequenceEqual(bake.Indices) && family.FrameCount == bake.FrameCount,
        name + ": compatible topology, UVs and frames");
    for (int orientation = 0; orientation < 4; orientation++)
    for (int id = 0; id < family.VertexCount; id++)
    {
        int x = id % 25, y = id / 25;
        int mx = (orientation & 1) != 0 ? 24 - x : x, my = (orientation & 2) != 0 ? 24 - y : y;
        var p = family.Samples[(my * 25 + mx) * 2];
        p.X *= (orientation & 1) != 0 ? -1 : 1; p.Y *= (orientation & 2) != 0 ? -1 : 1;
        if (Vector2.Distance(new(p.X + .5f, p.Y + .5f), family.UVs[id]) > 1e-6) throw new Exception("Mirrored ink handoff");
    }
    Check(true, name + ": all geometry reflections preserve the original flat UV appearance");
    var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
    float maxStep = 0, maxEdgeRatio = 0;
    for (int f = 0; f < family.FrameCount; f++)
    {
        Vector3 Position(int id) { var p = family.Samples[(f * family.VertexCount + id) * 2]; return new(p.X, p.Y, p.Z); }
        for (int v = 0; v < family.VertexCount; v++)
        {
            var n = family.Samples[(f * family.VertexCount + v) * 2 + 1];
            if (Math.Abs(new Vector3(n.X,n.Y,n.Z).Length()-1) > .001) throw new Exception("Non-unit normal");
            if (f == family.FrameCount - 1) { min = Vector3.Min(min, Position(v)); max = Vector3.Max(max, Position(v)); }
            if (f > 0) { var p = family.Samples[((f-1)*family.VertexCount+v)*2]; maxStep = Math.Max(maxStep, Vector3.Distance(Position(v), new(p.X,p.Y,p.Z))); }
        }
        for(int t=0;t<family.Indices.Length;t+=3) for(int e=0;e<3;e++)
        {
            int a=(int)family.Indices[t+e], b=(int)family.Indices[t+(e+1)%3];
            maxEdgeRatio=Math.Max(maxEdgeRatio,Vector3.Distance(Position(a),Position(b))/Vector2.Distance(family.UVs[a],family.UVs[b]));
        }
    }
    var extent=max-min;
    Check(extent.X < .52 && extent.Y < .52 && extent.Z > .20 && extent.Z < .52, name+": compact non-planar final bounds");
    Check(maxStep < .15 && maxEdgeRatio < 2, name+": bounded inter-frame motion and no stretched spikes");
    Console.WriteLine($"{name}: final {extent}, max frame step {maxStep}, max edge stretch {maxEdgeRatio}");
}
Check(FxVariation.GoldenSeeds.Select(s => (FxVariation.FromSeed(s).Family, FxVariation.FromSeed(s).Orientation)).Distinct().Count() == 12,
    "Golden suite covers every family/orientation pair");
Check(FxVariation.FromSeed(0) == new FxVariation(0, 0, 0, .9892125f, .060517795f, 1, .26991993f, .5380957f, -.2699675f),
    "Version 1 seed reference vector is stable");
Check(FxVariation.FromSeed(uint.MaxValue).Family == 0 && FxVariation.FromSeed(uint.MaxValue).Orientation == 1,
    "Full uint seed range, including wrap boundary");
var fixturePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "../../../fx-golden-seeds.json"));
using (var fixture = System.Text.Json.JsonDocument.Parse(File.ReadAllText(fixturePath)))
    Check(fixture.RootElement.GetProperty("seeds").EnumerateArray().Select(x => x.GetUInt32()).SequenceEqual(FxVariation.GoldenSeeds),
        "Native regression and UI capture use the same golden seed fixture");
for (int t=0;t<bake.Indices.Length;t+=3)
{
    Vector2 a=bake.UVs[bake.Indices[t]], b=bake.UVs[bake.Indices[t+1]], c=bake.UVs[bake.Indices[t+2]];
    if ((b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X) <= 0) throw new Exception("Inverted rest panel");
}
Check(true, "Every irregular rest panel has consistent orientation");
for (uint seed = 0; seed < 10000; seed++)
{
    var v = FxVariation.FromSeed(seed);
    if (v != FxVariation.FromSeed(seed) || v.DurationScale < .95f || v.DurationScale > 1.05f || v.Hold < .035f || v.Hold > .065f ||
        Math.Abs(v.Direction) != 1 || v.Travel < .24f || v.Travel > .30f || v.Rotation < .40f || v.Rotation > .58f || v.Lift < -.44f || v.Lift > -.26f)
        throw new Exception("Variation escaped bounds");
    var previous = DiscardMotion.At(0, v.Hold);
    for (int t = 1; t <= 100; t++)
    {
        var pose = DiscardMotion.At(t / 100f, v.Hold);
        if (pose.Deformation < previous.Deformation || pose.Throw < previous.Throw || pose.Opacity > previous.Opacity ||
            (pose.Throw > 0 && pose.Deformation != 1)) throw new Exception("Seed timeline invalid");
        previous = pose;
    }
}
Check(true, "10,000 exact seeds: bounded variation and coherent gather/hold/release timeline");
// Replay after unrelated seeds and a fresh asset read. Include the actual
// selected/reflected prepared path, normals, original ink UVs and elapsed-time
// poses, rather than checking only that FromSeed immediately equals itself.
string ReplayFingerprint(uint seed)
{
    var v = FxVariation.FromSeed(seed);
    var prepared = new PaperBake(Path.Combine(Path.GetDirectoryName(path)!, v.FamilyName + ".nfx"));
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);
    writer.Write(System.Text.Json.JsonSerializer.Serialize(v));
    for (int ms = 0; ms <= 800; ms++)
    {
        var pose = DiscardMotion.At((float)(ms / (DiscardMotion.DurationMs * v.DurationScale)), v.Hold);
        writer.Write(pose.Deformation); writer.Write(pose.Throw); writer.Write(pose.Opacity);
    }
    int side = (int)Math.Sqrt(prepared.VertexCount);
    int sx = (v.Orientation & 1) == 0 ? 1 : -1, sy = (v.Orientation & 2) == 0 ? 1 : -1;
    for (int frame = 0; frame < prepared.FrameCount; frame++)
    for (int id = 0; id < prepared.VertexCount; id++)
    {
        int x = id % side, y = id / side;
        int reflected = (sy < 0 ? side - 1 - y : y) * side + (sx < 0 ? side - 1 - x : x);
        writer.Write(prepared.UVs[id].X); writer.Write(prepared.UVs[id].Y);
        for (int component = 0; component < 2; component++)
        {
            var sample = prepared.Samples[(frame * prepared.VertexCount + reflected) * 2 + component];
            writer.Write(sample.X * sx); writer.Write(sample.Y * sy); writer.Write(sample.Z);
        }
    }
    writer.Flush();
    return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream.ToArray()));
}
var replaySeeds = FxVariation.GoldenSeeds.Append(uint.MaxValue).ToArray();
var fingerprints = replaySeeds.ToDictionary(s => s, ReplayFingerprint);
foreach (uint seed in replaySeeds.Reverse())
{
    _ = ReplayFingerprint(unchecked(seed + 101));
    if (ReplayFingerprint(seed) != fingerprints[seed]) throw new Exception("Replay trajectory depends on previous seed or asset load");
}
Check(fingerprints.Values.Distinct().Count() == replaySeeds.Length,
    "Golden seeds and uint max replay byte-exact timing, throw and reflected prepared paths after unrelated seeds and fresh asset loads");
if (args is ["--replay-manifest", var manifest])
    File.WriteAllText(manifest, System.Text.Json.JsonSerializer.Serialize(fingerprints, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(FxVariation.GoldenSeeds.Select(FxVariation.FromSeed)));
