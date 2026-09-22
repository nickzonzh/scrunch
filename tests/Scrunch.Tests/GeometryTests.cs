using System.Numerics;
using Xunit;

namespace Scrunch.Tests;

// Exhaustive sweeps over the supported drag and discard poses. The paper mesh is
// shared with the GPU path, so a singular patch is a visible crack, not a rounding
// detail: every corner must map back onto itself through the projective matrix.
// The sweeps run millions of patches; failures report through Assert.Fail so the
// happy path formats no messages.
public sealed class GeometryTests
{
    public static TheoryData<string> Feels
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var feel in PaperFeel.All) data.Add(feel.Name);
            return data;
        }
    }

    private static PaperFeel Feel(string name) => PaperFeel.All.Single(f => f.Name == name);

    private static readonly float[] Widths = [220f, 300f, 440f];
    private static readonly float[] Heights = [180f, 320f, 440f];
    private static readonly float[] Bends = [-0.1f, 0f, 0.65f, 0.9f];
    private static readonly float[] Twists = [-1f, 0f, 1f];
    private static readonly float[] Peels = [0f, 0.25f, 0.5f, 0.75f, 1f];

    [Theory]
    [MemberData(nameof(Feels))]
    public void TheAdhesiveEdgeStaysFixedUnderEveryDragPose(string feelName)
    {
        var feel = Feel(feelName);
        foreach (float width in Widths)
        foreach (float height in Heights)
        foreach (float bend in Bends)
        foreach (float twist in Twists)
        foreach (float peel in Peels)
        {
            var anchor = PaperGeometry.Point(0.5f, 0, width, height, feel, bend, twist, peel);
            if (Vector3.Distance(anchor, new Vector3(width / 2, 0, 0)) > 0.001f)
                Assert.Fail($"Adhesive moved: {feelName}, {width}x{height}, bend {bend}, twist {twist}, peel {peel}");
        }
    }

    [Theory]
    [MemberData(nameof(Feels))]
    public void DragPosesStayInsideThePaddedWindowAndMapEveryPatchCorner(string feelName)
    {
        var feel = Feel(feelName);
        foreach (float width in Widths)
        foreach (float height in Heights)
        foreach (float bend in Bends)
        foreach (float twist in Twists)
        foreach (float peel in Peels)
        for (int row = 0; row < PaperGeometry.Rows; row++)
        for (int col = 0; col < PaperGeometry.Columns; col++)
        {
            Vector2 P(float u, float v) => PaperGeometry.Project(
                PaperGeometry.Point(u, v, width, height, feel, bend, twist, peel), width, height);
            float u = col / (float)PaperGeometry.Columns, v = row / (float)PaperGeometry.Rows;
            float w = width / PaperGeometry.Columns, h = height / PaperGeometry.Rows;
            Span<Vector2> corners =
            [
                P(u, v), P(u + 1f / PaperGeometry.Columns, v),
                P(u + 1f / PaperGeometry.Columns, v + 1f / PaperGeometry.Rows), P(u, v + 1f / PaperGeometry.Rows)
            ];
            Span<Vector2> local = [Vector2.Zero, new(w, 0), new(w, h), new(0, h)];
            var matrix = PaperGeometry.Quad(corners[0], corners[1], corners[2], corners[3], w, h);
            for (int i = 0; i < 4; i++)
            {
                if (peel == 0 && (corners[i].X < -PaperGeometry.Padding || corners[i].Y < -PaperGeometry.Padding ||
                    corners[i].X > width + PaperGeometry.Padding || corners[i].Y > height + PaperGeometry.Padding))
                    Assert.Fail($"A drag pose escapes the padded window: {feelName}, {width}x{height}, bend {bend}, twist {twist}");
                var point = Vector4.Transform(new Vector4(local[i], 0, 1), matrix);
                var mapped = new Vector2(point.X / point.W, point.Y / point.W);
                float error = Vector2.Distance(mapped, corners[i]);
                if (!float.IsFinite(error) || error > 0.1f)
                    Assert.Fail($"Mesh seam: {error}, {feelName}, bend {bend}, peel {peel}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Feels))]
    public void CrumpleFramesKeepPositiveDepthAndMatchingCorners(string feelName)
    {
        var feel = Feel(feelName);
        foreach (float width in Widths)
        foreach (float height in Heights)
        foreach (float bend in new[] { 0f, 0.65f })
        for (int frame = 0; frame <= 100; frame++)
        {
            float amount = frame / 100f;
            for (int row = 0; row < PaperGeometry.Rows; row++)
            for (int col = 0; col < PaperGeometry.Columns; col++)
            {
                Vector2 P(float u, float v) => PaperGeometry.Project(
                    PaperGeometry.DiscardPoint(u, v, width, height, feel, bend, 0, 0, amount), width, height);
                float u = col / (float)PaperGeometry.Columns, v = row / (float)PaperGeometry.Rows;
                float w = width / PaperGeometry.Columns, h = height / PaperGeometry.Rows;
                Span<Vector2> corners =
                [
                    P(u, v), P(u + 1f / PaperGeometry.Columns, v),
                    P(u + 1f / PaperGeometry.Columns, v + 1f / PaperGeometry.Rows), P(u, v + 1f / PaperGeometry.Rows)
                ];
                Span<Vector2> local = [Vector2.Zero, new(w, 0), new(w, h), new(0, h)];
                var matrix = PaperGeometry.Quad(corners[0], corners[1], corners[2], corners[3], w, h);
                for (int i = 0; i < 4; i++)
                {
                    var point = Vector4.Transform(new Vector4(local[i], 0, 1), matrix);
                    var mapped = new Vector2(point.X / point.W, point.Y / point.W);
                    if (point.W <= 0 || !float.IsFinite(mapped.X) || !float.IsFinite(mapped.Y) ||
                        Vector2.Distance(mapped, corners[i]) > 0.1f)
                        Assert.Fail($"Crumple singularity: {feelName}, {width}x{height}, frame {frame}, tile {col},{row}");
                }
            }
        }
    }

    [Fact]
    public void DiscardEasingKeepsItsEndpoints()
    {
        Assert.Equal(0f, PaperGeometry.DiscardEase(0));
        Assert.Equal(1f, PaperGeometry.DiscardEase(1));
    }
}
