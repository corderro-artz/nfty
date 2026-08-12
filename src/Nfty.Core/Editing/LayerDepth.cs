using Nfty.Core.Model;

namespace Nfty.Core.Editing;

/// <summary>
/// Depth, as a projection of <see cref="RecipeManifest.LayerOrder"/>: 1-based, dense, and
/// bottom-first — <b>depth 1 is the layer that paints first</b>, and everything above paints over it.
///
/// <para>There is no stored depth field, deliberately. An integer <c>z</c> on the manifest would make
/// two layers at the same depth <i>expressible</i>, which is the very collision depth was asked for to
/// prevent; it would then need a <c>Validator</c> rule, a tie-break for every archive already on disk,
/// and an absent-default on every read path for every <c>schemaVersion: 1</c> archive in existence. A
/// list makes the illegal state unrepresentable rather than merely invalid. So <c>layerOrder</c> stays
/// authoritative — it is already what <c>Generator</c> walks and what <c>Compositor</c> draws — and
/// depth is that same field read a second way. Nothing here can drift out of agreement with paint
/// order, because there is no second structure to drift.</para>
///
/// <para>Consequences worth stating: no manifest changes and <see cref="Schema.Current"/> does not
/// move; and inserting or moving a layer <i>renumbers</i> the ones it passes, because the numbering is
/// positional.</para>
///
/// <para>Pure: every operation takes a manifest and returns a new one via <c>with</c>. It never
/// mutates, never touches an image, and never looks at the recipe's ingredient collection — which is
/// what lets both front-ends share it. Reordering preserves the exact set of ids, so the strict
/// bijection between <c>layerOrder</c> and the ingredients that <c>Validator</c> enforces survives
/// every move: a move can neither drop an id nor duplicate one.</para>
/// </summary>
public static class LayerDepth
{
    /// <summary>How many layers the recipe stacks — the largest legal depth.</summary>
    /// <param name="recipe">The recipe to measure.</param>
    /// <returns>The number of entries in the layer order; zero for a recipe with no layers.</returns>
    public static int Count(RecipeManifest recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        return recipe.LayerOrder.Count;
    }

    /// <summary>The 1-based depth of one layer, counting from the bottom of the stack.</summary>
    /// <param name="recipe">The recipe that owns the layer.</param>
    /// <param name="ingredientId">The layer to locate.</param>
    /// <returns>1 for the bottom layer, <see cref="Count"/> for the top one.</returns>
    /// <exception cref="KeyNotFoundException">The recipe's layer order does not name that
    /// ingredient.</exception>
    public static int DepthOf(RecipeManifest recipe, string ingredientId)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        return IndexOf(recipe, ingredientId) + 1;
    }

    /// <summary>The layer sitting at a given depth.</summary>
    /// <param name="recipe">The recipe to read.</param>
    /// <param name="depth">A 1-based depth, from 1 (bottom) to <see cref="Count"/> (top).</param>
    /// <returns>That layer's ingredient id.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The depth falls outside 1..<see cref="Count"/>.
    /// Reading throws where moving clamps: a caller asking "what is at depth 7?" of a five-layer stack
    /// has a bug, whereas a caller nudging the top layer upwards has a no-op.</exception>
    public static string At(RecipeManifest recipe, int depth)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        int count = recipe.LayerOrder.Count;
        if (depth < 1 || depth > count)
            throw new ArgumentOutOfRangeException(nameof(depth), depth, count == 0
                ? $"Recipe '{recipe.Id}' has no layers, so no depth is valid."
                : $"Depth must be between 1 and {count} in recipe '{recipe.Id}'.");
        return recipe.LayerOrder[depth - 1];
    }

    /// <summary>
    /// Moves one layer to a depth, shifting every layer it passes by one to make room.
    /// </summary>
    /// <param name="recipe">The recipe to reorder.</param>
    /// <param name="ingredientId">The layer to move.</param>
    /// <param name="depth">The 1-based depth to move it to. <b>Clamped</b> to 1..<see cref="Count"/>
    /// rather than rejected, so "move the top layer up" is a no-op the UI does not have to guard.</param>
    /// <returns>A NEW manifest whose layer order holds exactly the same ids, reordered.</returns>
    /// <exception cref="KeyNotFoundException">The recipe's layer order does not name that
    /// ingredient.</exception>
    public static RecipeManifest MoveTo(RecipeManifest recipe, string ingredientId, int depth)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        int from = IndexOf(recipe, ingredientId);

        // Remove-then-insert, never a swap: a move to depth 1 from depth 4 has to shift the three
        // layers it passes, and only re-seating the id in a list does that for free. It is also what
        // keeps the id set identical — nothing is written except the position of one entry.
        var order = new List<string>(recipe.LayerOrder);
        order.RemoveAt(from);
        order.Insert(Math.Clamp(depth, 1, recipe.LayerOrder.Count) - 1, ingredientId);
        return recipe with { LayerOrder = order };
    }

    /// <summary>
    /// Moves one layer by a relative number of steps: positive is up (towards the top of the stack),
    /// negative is down.
    /// </summary>
    /// <param name="recipe">The recipe to reorder.</param>
    /// <param name="ingredientId">The layer to move.</param>
    /// <param name="delta">Steps to move; +1 is one layer up, -1 one layer down. The resulting depth
    /// is clamped to 1..<see cref="Count"/>, so a nudge off either end is a no-op.</param>
    /// <returns>A NEW manifest whose layer order holds exactly the same ids, reordered.</returns>
    /// <exception cref="KeyNotFoundException">The recipe's layer order does not name that
    /// ingredient.</exception>
    public static RecipeManifest MoveBy(RecipeManifest recipe, string ingredientId, int delta)
    {
        ArgumentNullException.ThrowIfNull(recipe);

        // Widened before adding: an int delta near int.MaxValue would otherwise wrap to a negative
        // target and send a "move to the top" all the way to the bottom. Clamping here rather than
        // leaning on MoveTo's clamp is what makes that impossible instead of merely unlikely.
        long target = (long)DepthOf(recipe, ingredientId) + delta;
        int clamped = (int)Math.Clamp(target, 1, recipe.LayerOrder.Count);
        return MoveTo(recipe, ingredientId, clamped);
    }

    /// <summary>The whole stack, bottom-to-top, each layer paired with its depth — the shape a UI
    /// binds a layer table to.</summary>
    /// <param name="recipe">The recipe to read.</param>
    /// <returns>One entry per layer, in paint order, numbered densely from 1.</returns>
    public static IReadOnlyList<(int Depth, string IngredientId)> Ordered(RecipeManifest recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        return recipe.LayerOrder.Select((id, i) => (Depth: i + 1, IngredientId: id)).ToList();
    }

    /// <summary>The 0-based position of a layer, or the one exception every lookup here throws.</summary>
    private static int IndexOf(RecipeManifest recipe, string ingredientId)
    {
        // Ordinal, like every other id comparison that can reach an output file: a culture-sensitive
        // match could resolve two different ids to the same layer under some locale.
        for (int i = 0; i < recipe.LayerOrder.Count; i++)
            if (string.Equals(recipe.LayerOrder[i], ingredientId, StringComparison.Ordinal))
                return i;

        throw new KeyNotFoundException(
            $"No ingredient '{ingredientId}' in the layer order of recipe '{recipe.Id}'.");
    }
}
