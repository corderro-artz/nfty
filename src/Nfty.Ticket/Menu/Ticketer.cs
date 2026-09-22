using Nfty.Core.Generation;
using Nfty.Core.Model;

namespace Nfty.Ticket;

/// <summary>
/// A Menu with its draw order and running weight totals already resolved.
/// </summary>
/// <remarks>
/// The same split <see cref="WeightedRoller.Prepare"/> makes, for the same reason: the ordering and
/// the totals are fixed properties of the book, and a server rolling thousands of drops a second
/// must not re-sort and re-sum on every one.
/// </remarks>
public sealed class PreparedMenu
{
    internal PreparedMenu(MenuBook book, WeightedRoller.WeightTable recipes,
                          IReadOnlyDictionary<string, RecipePlan> plans)
    {
        Book = book;
        Recipes = recipes;
        Plans = plans;
    }

    /// <summary>The book this was prepared from.</summary>
    public MenuBook Book { get; }

    internal WeightedRoller.WeightTable Recipes { get; }
    internal IReadOnlyDictionary<string, RecipePlan> Plans { get; }
}

internal sealed record RecipePlan(MenuRecipe Recipe, IReadOnlyList<LayerPlan> Layers);

internal sealed record LayerPlan(
    MenuLayer Layer,
    WeightedRoller.WeightTable Weights,
    double AbsentWeight,
    bool AlwaysAbsent);

/// <summary>
/// The roll: decides a whole asset and touches no pixels.
/// </summary>
/// <remarks>
/// <b>This is the only roll in the product.</b> <c>Generator</c> calls it and keeps only the render,
/// so there is exactly one definition of the order in which layers consume the RNG. Two copies is
/// how "the same seed produces the same collection" stops being true, and the difference would be
/// invisible until a server and a client disagreed about what had been dropped.
/// </remarks>
public static class Ticketer
{
    /// <summary>Resolves a book's draw order and totals, once.</summary>
    /// <param name="book">The Menu to prepare.</param>
    /// <returns>The prepared form, to hand <see cref="TryRoll"/> repeatedly.</returns>
    public static PreparedMenu Prepare(MenuBook book)
    {
        ArgumentNullException.ThrowIfNull(book);

        var recipeWeights = book.Recipes.ToDictionary(r => r.Id, r => r.Weight, StringComparer.Ordinal);
        var plans = new Dictionary<string, RecipePlan>(StringComparer.Ordinal);

        foreach (var recipe in book.Recipes)
        {
            var byId = recipe.Layers.ToDictionary(l => l.Id, StringComparer.Ordinal);
            var layers = new List<LayerPlan>(recipe.LayerOrder.Count);

            foreach (string id in recipe.LayerOrder)
            {
                var layer = byId[id];
                var table = WeightedRoller.Prepare(
                    layer.Variants.ToDictionary(v => v.Id, v => v.Weight, StringComparer.Ordinal));
                double percent = recipe.AbsentPercentOf(id);
                bool never = WeightedRoller.AlwaysAbsent(percent);

                layers.Add(new LayerPlan(layer, table,
                    // Zero for the overwhelmingly common always-present layer, and zero is what makes
                    // the roll below bit-identical to the one that shipped before optional layers.
                    never ? 0 : WeightedRoller.AbsentWeight(percent, layer.Variants.Sum(v => v.Weight)),
                    never));
            }

            plans[recipe.Id] = new RecipePlan(recipe, layers);
        }

        return new PreparedMenu(book, WeightedRoller.Prepare(recipeWeights), plans);
    }

    /// <summary>Rolls one asset.</summary>
    /// <param name="menu">The prepared Menu.</param>
    /// <param name="rng">The run's RNG.</param>
    /// <returns>The ticket, or <see langword="null"/> when the recipe's rules reject the selection —
    /// which is the caller's signal to reroll, exactly as <c>Generator</c>'s loop does.</returns>
    public static AssetTicket? TryRoll(PreparedMenu menu, IRng rng)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(rng);

        string recipeId = WeightedRoller.Roll(menu.Recipes, rng);
        var plan = menu.Plans[recipeId];

        var selection = new Dictionary<string, string>(StringComparer.Ordinal);
        var layers = new List<TicketLayer>(plan.Layers.Count);

        foreach (var lp in plan.Layers)
        {
            // A LAYER MAY BE LEFT OUT. It contributes no trait, no pixels and no DNA part — an
            // omission rather than a sentinel, because a PRESENT layer adds a part an absent one does
            // not. Nothing else in this iteration runs, so an absent Dynamic layer rolls no colour,
            // which is what stops two assets that both show nothing from having different DNA.
            string? variantId = lp.AlwaysAbsent
                ? null
                : WeightedRoller.Roll(lp.Weights, lp.AbsentWeight, rng);
            if (variantId is null) continue;

            selection[lp.Layer.Id] = variantId;

            switch (lp.Layer.Kind)
            {
                case LayerKind.Dynamic or LayerKind.Static:
                {
                    var c = lp.Layer.Colorization!;
                    // The ONLY difference between the kinds is where the colour comes from: dynamic
                    // rolls it per asset, static resolves its one fixed colour and consumes NO RNG.
                    var colour = lp.Layer.Kind == LayerKind.Dynamic
                        ? ColorRoller.Roll(c, rng)
                        : ColorRoller.FromFixed(c.Entries[0].Fixed!, c.Model);
                    layers.Add(new TicketLayer(lp.Layer.Id, variantId, colour.H, colour.S,
                        c.Model, lp.Layer.Kind, c.HueQuantize, c.SatQuantize));
                    break;
                }
                default: // Custom — composited as-is, never colorized.
                    layers.Add(new TicketLayer(lp.Layer.Id, variantId, null, null, null,
                        LayerKind.Custom, 0, 0));
                    break;
            }
        }

        if (!RulesEngine.IsLegal(selection, plan.Recipe.Rules)) return null;

        return new AssetTicket(AssetTicket.ComputeDna(recipeId, layers), recipeId, layers);
    }
}
