using Nfty.Core.Model;
using Nfty.Ticket;

namespace Nfty.Core.Formats;

/// <summary>
/// Projects a loaded or peeked CookBook into the image-free <see cref="MenuBook"/> the roll works on.
/// </summary>
/// <remarks>
/// Cheap: it copies manifest records that are already in memory and opens no image. That is what lets
/// <c>Generator</c> roll through <c>Ticketer</c> without a second walk of its own.
/// </remarks>
public static class MenuProjection
{
    /// <summary>Projects a decoded book. The book keeps ownership of every image in it.</summary>
    /// <param name="book">The loaded book.</param>
    /// <returns>Its image-free form.</returns>
    public static MenuBook From(LoadedCookBook book)
    {
        ArgumentNullException.ThrowIfNull(book);
        return From(PeekedCookBook.Of(book), book.SourceSha256);
    }

    /// <summary>Projects a manifest-only book.</summary>
    /// <param name="book">The peeked book.</param>
    /// <returns>Its image-free form.</returns>
    public static MenuBook From(PeekedCookBook book) => From(book, null);

    private static MenuBook From(PeekedCookBook book, string? sha)
    {
        ArgumentNullException.ThrowIfNull(book);

        var recipes = book.Recipes.Select(r =>
        {
            var byId = r.Ingredients.ToDictionary(i => i.Id, StringComparer.Ordinal);
            var layers = r.Manifest.LayerOrder
                .Select(id => byId[id])
                .Select(i => new MenuLayer(i.Id, i.Name, i.Kind, i.Colorization, i.Variants))
                .ToList();

            return new MenuRecipe(
                r.Manifest.Id,
                r.Manifest.Name,
                book.Manifest.RecipeWeights.GetValueOrDefault(r.Manifest.Id),
                r.Manifest.LayerOrder,
                layers,
                r.Manifest.Rules,
                r.Manifest.AbsentPercent ?? new Dictionary<string, double>(StringComparer.Ordinal));
        }).ToList();

        return new MenuBook(
            MenuSchema.Current, sha, book.Manifest.Canvas,
            book.Manifest.Collection.Name, book.Manifest.Collection.Symbol,
            recipes,
            // Art is filled in by a later task's MenuWriter; a projection for rolling alone needs none.
            new MenuArt(MenuArtMode.Loose, 0, 0, []));
    }
}
