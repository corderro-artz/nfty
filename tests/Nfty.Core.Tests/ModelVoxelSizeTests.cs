using Nfty.Core.Publish;

namespace Nfty.Core.Tests;

public class ModelVoxelSizeTests
{
    [Theory]
    [InlineData(0.0, false)]
    [InlineData(0.0009, false)]
    [InlineData(0.001, true)]
    [InlineData(1.0, true)]
    [InlineData(1000.0, true)]
    [InlineData(1000.1, false)]
    [InlineData(double.NaN, false)]
    [InlineData(double.PositiveInfinity, false)]
    [InlineData(double.NegativeInfinity, false)]
    public void Voxel_size_bounds_are_one_rule_everywhere(double value, bool ok)
    {
        string? problem = ModelVoxelSize.ProblemWith(value);
        Assert.Equal(ok, problem is null);
        if (!ok) Assert.StartsWith("Voxel size must be between 0.001 and 1000, but ", problem);
    }

    [Fact]
    public void The_message_is_invariant_culture()
    {
        var old = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        try { Assert.Equal("Voxel size must be between 0.001 and 1000, but 0.0005 was given.", ModelVoxelSize.ProblemWith(0.0005)); }
        finally { System.Globalization.CultureInfo.CurrentCulture = old; }
    }
}
