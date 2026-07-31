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

    private static IngredientDraft VariantDraftFixture()
    {
        var canvas = new Dimensions(4, 4);
        var v = new VariantDraft("a", "A", 2, ValueMap.ForCanvas(canvas));
        v.Map.Set(1, 1, 150, 255);
        return new IngredientDraft("ing", "Ing", LayerKind.Dynamic, null, canvas, new[] { v });
    }

    [Fact]
    public void DuplicateVariant_copies_pixels_weight_with_a_new_id()
    {
        var d = VariantDraftFixture();
        var copy = d.DuplicateVariant("a", "b", "A copy");
        Assert.Equal(2, d.Variants.Count);
        Assert.Equal("b", copy.Id);
        Assert.Equal("A copy", copy.Name);
        Assert.Equal(2, copy.Weight);
        Assert.Equal(150, copy.Map.GetValue(1, 1));            // pixels copied
        copy.Map.Set(1, 1, 9, 9);
        Assert.Equal(150, d.Variants[0].Map.GetValue(1, 1));   // independent from source
    }

    [Fact]
    public void DuplicateVariant_rejects_a_duplicate_or_missing_id()
    {
        var d = VariantDraftFixture();
        Assert.Throws<System.InvalidOperationException>(() => d.DuplicateVariant("a", "a", "x"));   // newId exists
        Assert.Throws<System.InvalidOperationException>(() => d.DuplicateVariant("nope", "b", "x")); // source absent
    }

    [Fact]
    public void RemoveVariant_removes_by_id_and_rejects_absent()
    {
        var d = VariantDraftFixture();
        d.DuplicateVariant("a", "b", "B");
        d.RemoveVariant("a");
        Assert.Equal(new[] { "b" }, d.Variants.Select(v => v.Id));
        Assert.Throws<System.InvalidOperationException>(() => d.RemoveVariant("nope"));
    }
}
