using System.Numerics;
using System.Runtime.InteropServices;

namespace Scrunch.ScrunchFX;

// Prepared, versioned, little-endian data. No source-format decoders at runtime.
public sealed class PaperBake
{
    public int VertexCount { get; }
    public int FrameCount { get; }
    public Vector2[] UVs { get; }
    public uint[] Indices { get; }
    public Vector4[] Samples { get; }
    public PaperBake(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (reader.ReadUInt32() != 0x3258464e) throw new InvalidDataException("Unknown ScrunchFX asset version"); // 'NFX2'
        VertexCount = reader.ReadInt32(); FrameCount = reader.ReadInt32();
        int indexCount = reader.ReadInt32();
        if (VertexCount is < 3 or > 65536 || FrameCount is < 2 or > 256 || indexCount is < 3 or > 400000 || indexCount % 3 != 0)
            throw new InvalidDataException("Invalid ScrunchFX asset dimensions");
        long expected = 16L + VertexCount * 8L + indexCount * 4L + VertexCount * FrameCount * 12L;
        if (stream.Length != expected) throw new InvalidDataException("Truncated or oversized ScrunchFX asset");
        UVs = new Vector2[VertexCount]; Indices = new uint[indexCount]; Samples = new Vector4[VertexCount * FrameCount * 2];
        stream.ReadExactly(MemoryMarshal.AsBytes(UVs.AsSpan()));
        stream.ReadExactly(MemoryMarshal.AsBytes(Indices.AsSpan()));
        // Samples are six halves per point on disk (position xyz, normal xyz) and
        // expand into the float4 position/normal pairs the vertex shader indexes.
        // One frame of halves at a time keeps the transient buffer small.
        var frame = new Half[VertexCount * 6];
        var raw = MemoryMarshal.AsBytes(frame.AsSpan());
        for (int f = 0, s = 0; f < FrameCount; f++)
        {
            stream.ReadExactly(raw);
            for (int i = 0; i < frame.Length; i += 6, s += 2)
            {
                Samples[s] = new Vector4((float)frame[i], (float)frame[i + 1], (float)frame[i + 2], 0);
                Samples[s + 1] = new Vector4((float)frame[i + 3], (float)frame[i + 4], (float)frame[i + 5], 0);
            }
        }
        if (Indices.Any(i => i >= VertexCount) || UVs.Any(v => !float.IsFinite(v.X) || !float.IsFinite(v.Y)) ||
            Samples.Any(v => !float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z) || !float.IsFinite(v.W)))
            throw new InvalidDataException("Invalid ScrunchFX asset values");
    }
}
