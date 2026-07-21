using Nfty.Core.Generation;

namespace Nfty.Core.Tests;

public class RngTests
{
    [Fact]
    public void Same_seed_same_sequence()
    {
        var a = new SplitMix64Rng(SeedHash.ToUlong("vapor"));
        var b = new SplitMix64Rng(SeedHash.ToUlong("vapor"));
        for (int i = 0; i < 10; i++) Assert.Equal(a.NextDouble(), b.NextDouble());
    }

    [Fact]
    public void Different_seed_different_sequence()
    {
        var a = new SplitMix64Rng(SeedHash.ToUlong("vapor"));
        var b = new SplitMix64Rng(SeedHash.ToUlong("soft"));
        Assert.NotEqual(a.NextDouble(), b.NextDouble());
    }

    [Fact]
    public void Output_in_unit_interval()
    {
        var r = new SplitMix64Rng(1);
        for (int i = 0; i < 1000; i++) Assert.InRange(r.NextDouble(), 0.0, 1.0);
    }

    [Fact]
    public void Seed_hash_reads_a_fixed_byte_order()
    {
        // Locks the seed→ulong mapping to little-endian regardless of CPU. SeedHash reads the
        // SHA-256 with BinaryPrimitives.ReadUInt64LittleEndian precisely so the same seed produces
        // the same RNG stream — and thus the same collection — on any architecture. This value is
        // the first 8 bytes of SHA-256("vapor") read little-endian; if a refactor ever reintroduced
        // native-endian BitConverter, this assertion would fail on a big-endian build.
        Assert.Equal(781726080920091387UL, SeedHash.ToUlong("vapor"));
    }
}
