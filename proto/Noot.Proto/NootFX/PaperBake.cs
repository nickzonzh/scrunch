using System.Numerics;
using System.Runtime.InteropServices;

namespace Noot_Proto.NootFX;

// Prepared, versioned, little-endian data. No source-format decoders at runtime.
internal sealed class PaperBake
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
        if (reader.ReadUInt32() != 0x3158464e) throw new InvalidDataException("Unknown NootFX asset version");
        VertexCount = reader.ReadInt32(); FrameCount = reader.ReadInt32();
        int indexCount = reader.ReadInt32();
        if (VertexCount is < 3 or > 65536 || FrameCount is < 2 or > 256 || indexCount is < 3 or > 400000 || indexCount % 3 != 0)
            throw new InvalidDataException("Invalid NootFX asset dimensions");
        long expected = 16L + VertexCount * 8L + indexCount * 4L + VertexCount * FrameCount * 32L;
        if (stream.Length != expected) throw new InvalidDataException("Truncated or oversized NootFX asset");
        UVs = new Vector2[VertexCount]; Indices = new uint[indexCount]; Samples = new Vector4[VertexCount * FrameCount * 2];
        stream.ReadExactly(MemoryMarshal.AsBytes(UVs.AsSpan()));
        stream.ReadExactly(MemoryMarshal.AsBytes(Indices.AsSpan()));
        stream.ReadExactly(MemoryMarshal.AsBytes(Samples.AsSpan()));
        if (Indices.Any(i => i >= VertexCount) || UVs.Any(v => !float.IsFinite(v.X) || !float.IsFinite(v.Y)) ||
            Samples.Any(v => !float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z) || !float.IsFinite(v.W)))
            throw new InvalidDataException("Invalid NootFX asset values");
    }
}
