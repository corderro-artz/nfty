using System.Buffers.Binary;

namespace Nfty.Model.Gltf;

/// <summary>
/// The GLB container: a 12-byte header and two length-prefixed chunks, all little-endian. The JSON
/// chunk is padded with spaces and the BIN chunk with zeros, each to four bytes, as the spec requires.
/// </summary>
internal static class GlbContainer
{
    /// <summary>"glTF".</summary>
    public const uint Magic = 0x46546C67;

    /// <summary>The container version.</summary>
    public const uint Version = 2;

    /// <summary>"JSON".</summary>
    public const uint JsonChunk = 0x4E4F534A;

    /// <summary>"BIN\0".</summary>
    public const uint BinChunk = 0x004E4942;

    /// <summary>Frames a document and its buffer.</summary>
    public static byte[] Frame(byte[] json, byte[] bin)
    {
        int jsonLength = Pad4(json.Length);
        int binLength = Pad4(bin.Length);
        int total = checked(12 + 8 + jsonLength + 8 + binLength);
        var glb = new byte[total];
        var span = glb.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(span, Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], Version);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], (uint)total);

        BinaryPrimitives.WriteUInt32LittleEndian(span[12..], (uint)jsonLength);
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..], JsonChunk);
        json.CopyTo(span[20..]);
        span.Slice(20 + json.Length, jsonLength - json.Length).Fill(0x20);

        int binHeader = 20 + jsonLength;
        BinaryPrimitives.WriteUInt32LittleEndian(span[binHeader..], (uint)binLength);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(binHeader + 4)..], BinChunk);
        bin.CopyTo(span[(binHeader + 8)..]);   // the tail past bin.Length is already zero
        return glb;
    }

    /// <summary>Rounds up to a multiple of four.</summary>
    public static int Pad4(int n) => checked((n + 3) & ~3);
}
