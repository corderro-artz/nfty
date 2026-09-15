using Nfty.Core.Model;

namespace Nfty.Core.Formats;

/// <summary>
/// A CookBook read as MANIFESTS ALONE: every weight, rule, absent-percent and variant name in the
/// tree, and not one decoded pixel.
/// </summary>
/// <remarks>
/// <para>This is <see cref="ArchivePeek"/>'s argument carried one level deeper. That type exists
/// because a workspace <em>listing</em> has no business decoding a book's art, and reads the outer
/// manifest alone. But a whole class of question is answered by the nested manifests and by nothing
/// else — how likely a selection was, what a rule forbids, which variants a layer carries — and
/// answering one of those through <see cref="CookBookArchive.Read"/> pulls every variant PNG in the
/// collection into memory to look at a few dozen numbers.</para>
///
/// <para>So this is the shape those questions take their input in. It is deliberately NOT
/// <see cref="IDisposable"/>: there is nothing here to own. <see cref="LoadedCookBook"/> remains the
/// engine's form — <c>Generator</c> and <c>Validator</c> take that and only that, because they draw
/// pixels — and a reader of this one must not assume the art is even present.</para>
///
/// <para><b>Recipe order here is ordinal, not the book's own
/// <see cref="CookBookManifest.RecipeOrder"/>.</b> That field is a listing preference and this is not
/// a listing; anything that shows a reader a book's recipes wants <see cref="CookBookArchive.Read"/>
/// or the manifest field itself.</para>
/// </remarks>
/// <param name="Manifest">The book's own manifest — canvas, collection, recipe weights.</param>
/// <param name="Recipes">Its recipes, ordinally by id.</param>
public record PeekedCookBook(CookBookManifest Manifest, IReadOnlyList<PeekedRecipe> Recipes)
{
    /// <summary>The same book, projected off one that is already open and decoded.</summary>
    /// <param name="book">A loaded book. Nothing is copied but the manifests, and the loaded book
    /// keeps ownership of every image in it.</param>
    /// <returns>Its manifest-only form.</returns>
    public static PeekedCookBook Of(LoadedCookBook book)
    {
        ArgumentNullException.ThrowIfNull(book);
        return new PeekedCookBook(book.Manifest, book.Recipes.Select(PeekedRecipe.Of).ToList());
    }
}

/// <summary>One recipe's manifests: its own, and one per ingredient nested inside it.</summary>
/// <param name="Manifest">The recipe manifest — layer order, rules, absent percents.</param>
/// <param name="Ingredients">Its ingredients' manifests, ordinally by id.</param>
public record PeekedRecipe(RecipeManifest Manifest, IReadOnlyList<IngredientManifest> Ingredients)
{
    /// <summary>The same recipe, projected off one that is already open and decoded.</summary>
    /// <param name="recipe">A loaded recipe.</param>
    /// <returns>Its manifest-only form.</returns>
    public static PeekedRecipe Of(LoadedRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        return new PeekedRecipe(recipe.Manifest, recipe.Ingredients.Select(i => i.Manifest).ToList());
    }
}
