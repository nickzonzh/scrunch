using System;
using System.Numerics;

namespace Scrunch;

// Pure geometry, independent of WinUI. Heights are in device-independent pixels.
public sealed record PaperFeel(string Name, float Curl, float Flex, float Damping, float Frequency, float Twist)
{
    public static readonly PaperFeel[] All =
    {
        new("Stationery", 2.5f, 0.30f, 16f, 22f, 0.25f),
        new("Playful paper", 5f, 0.58f, 12f, 24f, 0.55f),
        new("Animated", 9f, 0.95f, 9f, 23f, 1f)
    };
}

public static class PaperGeometry
{
    public const int Columns = 12;
    public const int Rows = 24;
    public const float Padding = 80;
    // A continuous corrugated bundle, using the same UVs as the written sheet.
    // Deliberately bounded: no self-collision solver or replacement ball texture.
    public static Vector3 CrumplePoint(float u, float v, float width, float height, float amount)
    {
        float t = Math.Clamp(amount, 0, 1);
        float x = u * 2 - 1, y = v * 2 - 1;
        float radius = 32;
        var flat = new Vector3(u * width, v * height, 0);
        float Triangle(float s) => 1 - 4 * MathF.Abs(s - MathF.Floor(s) - 0.5f);
        float ridges = (Triangle(u * 2.7f + v * 1.8f + 0.13f) +
            Triangle(u * 1.3f - v * 3.1f + 0.37f) * 0.55f) / 1.55f;
        // Two oblique families of creases, with a little irregularity at the rim.
        var bundle = new Vector3(width / 2 + radius * x * MathF.Sqrt(1 - y * y * 0.38f) * (1 + 0.01f * MathF.Sin(v * 9)),
            height / 2 + radius * y * MathF.Sqrt(1 - x * x * 0.38f) * (1 + 0.01f * MathF.Sin(u * 11 + 1)),
            20 * (1 - (x * x + y * y) * 0.5f) + ridges * 2);
        // The two axes gather at different rates instead of uniformly scaling text.
        float gatherX = t + 0.12f * MathF.Sin(t * MathF.PI);
        float gatherY = t - 0.08f * MathF.Sin(t * MathF.PI);
        var point = new Vector3(float.Lerp(flat.X, bundle.X, gatherX),
            float.Lerp(flat.Y, bundle.Y, gatherY), bundle.Z * t);
        point.Z += MathF.Sin(t * MathF.PI) * (ridges * 10 + 14 * (1 - x * x));
        return point;
    }

    public static Vector3 DiscardPoint(float u, float v, float width, float height, PaperFeel feel,
        float bend, float twist, float peel, float amount) => CrumplePoint(u, v, width, height, amount) +
        (Point(u, v, width, height, feel, bend, twist, peel) - new Vector3(u * width, v * height, 0)) * (1 - amount);

    // Standard strong ease-out, cubic-bezier(0.23, 1, 0.32, 1).
    public static float DiscardEase(float progress)
        => Bezier(progress, 0.23f, 1, 0.32f, 1);

    // Strong ease-in-out gives the gather a readable middle before the throw.
    public static float CrumpleEase(float progress) => Bezier(progress, 0.77f, 0, 0.175f, 1);

    private static float Bezier(float progress, float x1, float y1, float x2, float y2)
    {
        float x = Math.Clamp(progress, 0, 1), low = 0, high = 1;
        for (int i = 0; i < 16; i++)
        {
            float t = (low + high) / 2, s = 1 - t;
            float sample = 3 * s * s * t * x1 + 3 * s * t * t * x2 + t * t * t;
            if (sample < x) low = t; else high = t;
        }
        float solved = (low + high) / 2, remainder = 1 - solved;
        return x == 0 ? 0 : x == 1 ? 1 : 3 * remainder * remainder * solved * y1 + 3 * remainder * solved * solved * y2 + solved * solved * solved;
    }

    public static Vector3 Point(float u, float v, float width, float height,
        PaperFeel feel, float bend, float twist, float peel)
    {
        // A cylindrical bend preserves length along the sheet. The adhesive stays flat.
        float t = Math.Max(0, (v - 0.08f) / 0.92f);
        float length = height * 0.92f;
        float angle = bend * feel.Flex + peel * (1.3f + feel.Flex * 1.3f);
        float y = height * 0.08f + length * t;
        float z = 0;
        if (Math.Abs(angle) > 0.001f)
        {
            y = height * 0.08f + length * MathF.Sin(angle * t) / angle;
            z = length * (1 - MathF.Cos(angle * t)) / angle;
        }
        if (v < 0.08f) y = v * height;
        z += feel.Curl * MathF.Pow(v, 4) * (0.25f + 0.75f * u * u);
        z += twist * feel.Twist * (u - 0.5f) * t * 28;
        return new Vector3(u * width, y, z);
    }

    public static Vector2 Project(Vector3 p, float width, float height)
    {
        float perspective = 1200 / (1200 - p.Z);
        return new Vector2((p.X - width / 2) * perspective + width / 2,
            (p.Y - height / 2 - p.Z * 0.28f) * perspective + height / 2);
    }

    // Exact projective mapping of a unit square onto a quad. Shared mesh vertices
    // prevent cracks; tiny sprite overdraw covers rasterisation seams.
    public static Matrix4x4 Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float width, float height)
    {
        float dx1 = b.X - c.X, dx2 = d.X - c.X, dx3 = a.X - b.X + c.X - d.X;
        float dy1 = b.Y - c.Y, dy2 = d.Y - c.Y, dy3 = a.Y - b.Y + c.Y - d.Y;
        float den = dx1 * dy2 - dx2 * dy1;
        float g = 0, h = 0;
        if (Math.Abs(den) > 0.00001f)
        {
            g = (dx3 * dy2 - dx2 * dy3) / den;
            h = (dx1 * dy3 - dx3 * dy1) / den;
        }
        return new Matrix4x4(
            (b.X - a.X + g * b.X) / width, (b.Y - a.Y + g * b.Y) / width, 0, g / width,
            (d.X - a.X + h * d.X) / height, (d.Y - a.Y + h * d.Y) / height, 0, h / height,
            0, 0, 1, 0, a.X, a.Y, 0, 1);
    }
}
