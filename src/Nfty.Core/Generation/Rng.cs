using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Nfty.Core.Generation;

/// <summary>The single source of randomness a run consumes. An interface so a test can feed a
/// fixed sequence, and so the generator cannot reach for <c>Random</c> by accident.</summary>
public interface IRng
{
    /// <summary>The next sample.</summary>
    /// <returns>A value in <c>[0,1)</c>.</returns>
    double NextDouble();
}

/// <summary>SplitMix64. Small, fast, and — the reason it is here — fully specified, so the same
/// seed yields the same sequence on every platform and runtime.</summary>
public sealed class SplitMix64Rng : IRng
{
    private ulong _state;
    /// <summary>Seeds the generator.</summary>
    /// <param name="seed">The starting state, from <see cref="SeedHash.ToUlong"/>.</param>
    public SplitMix64Rng(ulong seed) => _state = seed;

    /// <inheritdoc />
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

/// <summary>Turns a user's seed string into RNG state.</summary>
public static class SeedHash
{
    /// <summary>Hashes a seed string to 64 bits.</summary>
    /// <param name="seed">The user's seed.</param>
    /// <returns>The RNG's starting state. Read LITTLE-ENDIAN from the SHA-256 rather than with
    /// native-endian <c>BitConverter</c>, so a big-endian CPU seeds identically — and little-endian
    /// specifically, so the rule matches every Set already generated.</returns>
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
