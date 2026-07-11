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
        return BitConverter.ToUInt64(hash, 0);
    }
}
