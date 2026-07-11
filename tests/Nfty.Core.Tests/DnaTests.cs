using Nfty.Core.Generation;

namespace Nfty.Core.Tests;

public class DnaTests
{
    [Fact]
    public void Same_selection_same_dna()
    {
        var a = new[] { new LayerSelection("bg", "sunset", null, null, 5, 5) };
        var b = new[] { new LayerSelection("bg", "sunset", null, null, 5, 5) };
        Assert.Equal(Dna.Compute("cat", a), Dna.Compute("cat", b));
    }

    [Fact]
    public void Different_recipe_changes_dna()
    {
        var sel = new[] { new LayerSelection("bg", "sunset", null, null, 5, 5) };
        Assert.NotEqual(Dna.Compute("cat", sel), Dna.Compute("robot", sel));
    }

    [Fact]
    public void Layer_order_does_not_change_dna()
    {
        var a = new[]
        {
            new LayerSelection("bg", "sunset", null, null, 5, 5),
            new LayerSelection("body", "cat", null, null, 5, 5),
        };
        var b = new[]
        {
            new LayerSelection("body", "cat", null, null, 5, 5),
            new LayerSelection("bg", "sunset", null, null, 5, 5),
        };
        Assert.Equal(Dna.Compute("cat", a), Dna.Compute("cat", b));
    }

    [Fact]
    public void Colors_in_same_quant_bucket_share_dna()
    {
        var a = new[] { new LayerSelection("aura", "glow", 181.0, 0.71, 5, 5) };
        var b = new[] { new LayerSelection("aura", "glow", 184.0, 0.73, 5, 5) };
        Assert.Equal(Dna.Compute("cat", a), Dna.Compute("cat", b));
    }

    [Fact]
    public void Colors_in_different_buckets_differ()
    {
        var a = new[] { new LayerSelection("aura", "glow", 181.0, 0.71, 5, 5) };
        var b = new[] { new LayerSelection("aura", "glow", 200.0, 0.71, 5, 5) };
        Assert.NotEqual(Dna.Compute("cat", a), Dna.Compute("cat", b));
    }
}
