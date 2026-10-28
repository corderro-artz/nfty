using System.Globalization;
using Nfty.App.Imaging;
using Nfty.Core.Formats;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.App.Viewer;

/// <summary>
/// Viewer subjects for the parts of an open CookBook: one layer's first variant, and one rolled
/// recipe. Snapshots: the viewer owns copies, so the book may be saved and disposed under it.
/// </summary>
public static class BookSubjects
{
    /// <summary>The seed of the recipe roll a viewer shows; fixed so asking twice shows the same asset.</summary>
    public const string RollSeed = "viewer";

    /// <summary>A layer, as generation would render its first variant.</summary>
    /// <param name="ingredient">The layer; borrowed.</param>
    /// <returns>A snapshot the caller owns.</returns>
    public static SnapshotSubject ForIngredient(LoadedIngredient ingredient)
    {
        ArgumentNullException.ThrowIfNull(ingredient);
        var first = ingredient.Manifest.Variants[0];
        using var art = VariantImagery.RenderImage(ingredient, first.Id);
        var facts = new List<ViewerFact>
        {
            new("LAYER", ingredient.Manifest.Name),
            new("KIND", ingredient.Manifest.Kind.ToString().ToUpperInvariant()),
            new("VARIANT", first.Name),
            new("VARIANTS", ingredient.Manifest.Variants.Count.ToString(CultureInfo.InvariantCulture)),
            new("SIZE", string.Create(CultureInfo.InvariantCulture, $"{art.Width} x {art.Height}")),
        };
        return new SnapshotSubject($"{ingredient.Manifest.Name} / {first.Name}", facts, art, null);
    }

    /// <summary>A recipe rolled once, the way generation would roll an asset.</summary>
    /// <param name="recipe">The recipe; borrowed.</param>
    /// <returns>A snapshot the caller owns.</returns>
    /// <exception cref="InvalidOperationException">The recipe cannot be rolled (no layers, or its
    /// rules reject every attempt); the message says why.</exception>
    public static SnapshotSubject ForRecipe(LoadedRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        var any = recipe.Ingredients.SelectMany(i => i.VariantImages.Values).FirstOrDefault()
            ?? throw new InvalidOperationException($"{recipe.Manifest.Name} has no pictures to roll.");
        var layers = StackRoll.ForRecipe(recipe, StackRoll.RngFor(RollSeed));
        using var art = StackPreview.Render(new Dimensions(any.Width, any.Height), layers);
        var facts = new List<ViewerFact>
        {
            new("RECIPE", recipe.Manifest.Name),
            new("LAYERS", string.Create(CultureInfo.InvariantCulture,
                $"{layers.Count} of {recipe.Manifest.LayerOrder.Count}")),
            new("SIZE", string.Create(CultureInfo.InvariantCulture, $"{art.Width} x {art.Height}")),
        };
        return new SnapshotSubject(recipe.Manifest.Name, facts, art, null);
    }
}
