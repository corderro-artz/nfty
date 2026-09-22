using Nfty.Core.Model;
using Nfty.Ticket;

namespace Nfty.Ticket.Tests;

public class MenuBookTests
{
    /// <summary>
    /// The cell key is scoped by RECIPE, and that is load-bearing. A .cbk nests a separate .igt
    /// inside each .rcp, so two recipes can each carry an ingredient called "bg" holding entirely
    /// different variants. A key scoped any shallower collides silently and one recipe draws the
    /// other's art.
    /// </summary>
    [Fact]
    public void Two_recipes_with_the_same_layer_id_get_different_cell_keys()
    {
        Assert.NotEqual(
            MenuBook.CellKey("cat", "bg", "a"),
            MenuBook.CellKey("dog", "bg", "a"));
    }

    [Fact]
    public void A_layer_with_no_absent_percent_is_always_present()
    {
        var r = Recipe(absent: new Dictionary<string, double>());
        Assert.Equal(0, r.AbsentPercentOf("bg"));
    }

    [Fact]
    public void A_layers_absent_percent_is_what_the_recipe_recorded()
    {
        var r = Recipe(absent: new Dictionary<string, double> { ["bg"] = 25 });
        Assert.Equal(25, r.AbsentPercentOf("bg"));
    }

    private static MenuRecipe Recipe(IReadOnlyDictionary<string, double> absent) =>
        new("cat", "Cat", 1, ["bg"],
            [new MenuLayer("bg", "Background", LayerKind.Custom, null, [new Variant("a", "A", 1)])],
            [], absent);
}
