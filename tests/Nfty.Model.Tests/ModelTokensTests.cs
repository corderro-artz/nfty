using Nfty.Core.Output;

namespace Nfty.Model.Tests;

public class ModelTokensTests
{
    [Fact]
    public void Tokens_round_trip_every_format_and_pivot()
    {
        Assert.Equal(new[] { "glb", "gltf", "gltf-embedded" }, ModelTokens.FormatNames);
        Assert.Equal(new[] { "bottom-center", "center", "min-corner" }, ModelTokens.PivotNames);

        foreach (var format in Enum.GetValues<ModelFormat>())
            Assert.Equal(format, ModelTokens.FormatOf(ModelTokens.NameOf(format)));
        foreach (var pivot in Enum.GetValues<Pivot>())
            Assert.Equal(pivot, ModelTokens.PivotOf(ModelTokens.NameOf(pivot)));
        Assert.Equal(ModelTokens.FormatNames.Count, Enum.GetValues<ModelFormat>().Length);
        Assert.Equal(ModelTokens.PivotNames.Count, Enum.GetValues<Pivot>().Length);

        var options = new RiseOptions { Format = ModelFormat.GltfEmbedded, Pivot = Pivot.MinCorner, VoxelSize = 0.25f };
        var record = ModelTokens.Record(options);
        Assert.Equal(new SetModels("gltf-embedded", "min-corner", 0.25f), record);
        Assert.Equal(options, ModelTokens.OptionsOf(record));
    }

    [Fact]
    public void An_unknown_token_is_refused_not_guessed()
    {
        Assert.Throws<ArgumentException>(() => ModelTokens.FormatOf("obj"));
        Assert.Throws<ArgumentException>(() => ModelTokens.FormatOf("GLB"));
        Assert.Throws<ArgumentException>(() => ModelTokens.PivotOf("middle"));
    }
}
