using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Nfty.Core.Generation;

public interface IRng
{
    double NextDouble();
}

public sealed class SplitMix64Rng : IRng
{
    private ulong _state;
    public SplitMix64Rng(ulong seed) => _state = seed;

    public double NextDouble()
    {
        _state += 0x9E3779B97F4A7C15UL;
        ulong z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        return (z >> 11) * (1.0 / (1UL << 53)); // [0,1)
    }
}

public static class SeedHash
{
    public static ulong ToUlong(string seed)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));

        // Read the first 8 bytes in a FIXED byte order, not the machine's. BitConverter.ToUInt64
        // reads in native endianness, so the same seed would seed the RNG differently — and thus
        // generate a different collection — on a big-endian CPU than a little-endian one, silently
        // breaking the same-seed-same-output contract across architectures. Little-endian is chosen
        // because it equals what BitConverter produced on the little-endian machines every existing
        // Set was generated on, so this hardening changes no output that has ever been produced.
        return BinaryPrimitives.ReadUInt64LittleEndian(hash);
    }
}
