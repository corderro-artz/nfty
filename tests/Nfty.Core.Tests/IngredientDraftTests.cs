using Nfty.Core.Editing;
using Nfty.Core.Model;
using Xunit;

namespace Nfty.Core.Tests;

public class IngredientDraftTests
{
    [Fact]
    public void AddVariant_creates_a_canvas_sized_value_map()
    {
        var draft = new IngredientDraft("body", "Body", LayerKind.Dynamic, null,
            new Dimensions(8, 8), System.Array.Empty<VariantDraft>());
        var v = draft.AddVariant("slime", "Slime", 40);
        Assert.Single(draft.Variants);
        Assert.Equal(8, v.Map.Width);
        Assert.Equal(8, v.Map.Height);
        Assert.Equal(40, v.Weight);
    }
}
