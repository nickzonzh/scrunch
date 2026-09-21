using System.Numerics;
using Noot_Proto.NootFX;

string path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../Noot.Proto/Assets/NootFX/crumple.nfx"));
if (!File.Exists(path)) path = Path.GetFullPath("proto/Noot.Proto/Assets/NootFX/crumple.nfx");
var bake = new PaperBake(path);
void Check(bool success, string label) { if (!success) throw new Exception(label); Console.WriteLine("PASS: " + label); }
Check(bake.VertexCount == 3500 && bake.FrameCount == 38 && bake.Indices.Length == 20286, "Prepared topology and trimmed frame counts");
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
