using System.Numerics;
using Scrunch;

int checkedPatches = 0;
float maximumError = 0;
foreach (var feel in PaperFeel.All)
foreach (float width in new[] { 220f, 300f, 440f })
foreach (float height in new[] { 180f, 320f, 440f })
foreach (float bend in new[] { -0.1f, 0f, 0.65f, 0.9f })
foreach (float twist in new[] { -1f, 0f, 1f })
foreach (float peel in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
{
    // The adhesive edge stays fixed under every supported deformation.
    var anchor = PaperGeometry.Point(0.5f, 0, width, height, feel, bend, twist, peel);
    if (Vector3.Distance(anchor, new Vector3(width / 2, 0, 0)) > 0.001f) throw new Exception("Adhesive moved");
    for (int row = 0; row < PaperGeometry.Rows; row++)
    for (int col = 0; col < PaperGeometry.Columns; col++)
    {
        Vector2 P(float u, float v) => PaperGeometry.Project(
            PaperGeometry.Point(u, v, width, height, feel, bend, twist, peel), width, height);
        float u = col / (float)PaperGeometry.Columns, v = row / (float)PaperGeometry.Rows, w = width / PaperGeometry.Columns, h = height / PaperGeometry.Rows;
        var corners = new[] { P(u, v), P(u + 1f / PaperGeometry.Columns, v), P(u + 1f / PaperGeometry.Columns, v + 1f / PaperGeometry.Rows), P(u, v + 1f / PaperGeometry.Rows) };
        if (peel == 0 && corners.Any(p => p.X < -PaperGeometry.Padding || p.Y < -PaperGeometry.Padding || p.X > width + PaperGeometry.Padding || p.Y > height + PaperGeometry.Padding))
            throw new Exception("A drag pose escapes the padded window");
        var local = new[] { Vector2.Zero, new Vector2(w, 0), new Vector2(w, h), new Vector2(0, h) };
        var matrix = PaperGeometry.Quad(corners[0], corners[1], corners[2], corners[3], w, h);
        for (int i = 0; i < 4; i++)
        {
            var point = Vector4.Transform(new Vector4(local[i], 0, 1), matrix);
            var mapped = new Vector2(point.X / point.W, point.Y / point.W);
            float error = Vector2.Distance(mapped, corners[i]);
            if (!float.IsFinite(error) || error > 0.1f) throw new Exception($"Mesh seam: {error}, {feel.Name}, bend {bend}, peel {peel}");
            maximumError = Math.Max(maximumError, error);
        }
        checkedPatches++;
    }
}
Console.WriteLine($"PASS: {checkedPatches:N0} projected patches; fixed adhesive; finite corner mappings; maximum seam error {maximumError:F6} DIP.");

int discardPatches = 0;
foreach (var feel in PaperFeel.All)
foreach (float width in new[] { 220f, 300f, 440f })
foreach (float height in new[] { 180f, 320f, 440f })
foreach (float bend in new[] { 0f, 0.65f })
for (int frame = 0; frame <= 100; frame++)
{
    float amount = frame / 100f;
    for (int row = 0; row < PaperGeometry.Rows; row++)
    for (int col = 0; col < PaperGeometry.Columns; col++)
    {
        Vector2 P(float u, float v) => PaperGeometry.Project(PaperGeometry.DiscardPoint(u, v, width, height, feel, bend, 0, 0, amount), width, height);
        float u = col / (float)PaperGeometry.Columns, v = row / (float)PaperGeometry.Rows, w = width / PaperGeometry.Columns, h = height / PaperGeometry.Rows;
        var corners = new[] { P(u, v), P(u + 1f / PaperGeometry.Columns, v), P(u + 1f / PaperGeometry.Columns, v + 1f / PaperGeometry.Rows), P(u, v + 1f / PaperGeometry.Rows) };
        var matrix = PaperGeometry.Quad(corners[0], corners[1], corners[2], corners[3], w, h);
        var local = new[] { Vector2.Zero, new Vector2(w, 0), new Vector2(w, h), new Vector2(0, h) };
        for (int i = 0; i < 4; i++)
        {
            var point = Vector4.Transform(new Vector4(local[i], 0, 1), matrix);
            var mapped = new Vector2(point.X / point.W, point.Y / point.W);
            if (point.W <= 0 || !float.IsFinite(mapped.X) || !float.IsFinite(mapped.Y) || Vector2.Distance(mapped, corners[i]) > 0.1f)
                throw new Exception($"Crumple singularity: {feel.Name}, {width}x{height}, frame {frame}, tile {col},{row}");
        }
        discardPatches++;
    }
}
if (PaperGeometry.DiscardEase(0) != 0 || PaperGeometry.DiscardEase(1) != 1) throw new Exception("Discard easing endpoints");
Console.WriteLine($"PASS: {discardPatches:N0} crumple patches; positive projective denominators and matching corners throughout contraction.");

