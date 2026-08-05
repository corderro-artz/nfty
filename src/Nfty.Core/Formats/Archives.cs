namespace Nfty.Core.Formats;

/// <summary>Which domain archive a file holds, as declared by its extension.</summary>
public enum ArchiveKind
{
    /// <summary><c>.cbk</c> — an uncooked Set: the container of Recipes.</summary>
    CookBook,

    /// <summary><c>.rcp</c> — a full template for one type.</summary>
    Recipe,

    /// <summary><c>.igt</c> — one layer and its weighted variants.</summary>
    Ingredient,

    /// <summary><c>.ktn</c> — the top-level workspace naming the folder it sits in.</summary>
    Kitchen,
}

/// <summary>
/// Dispatch over the three archive types by extension, so callers that accept "any nfty file"
/// (the CLI's <c>inspect</c>, a GUI open dialog) resolve the kind in one place.
/// </summary>
public static class Archives
{
    public const string CookBookExtension = ".cbk";
    public const string RecipeExtension = ".rcp";
    public const string IngredientExtension = ".igt";
    public const string KitchenExtension = ".ktn";

    /// <summary>
    /// The archive kind for <paramref name="path"/>. An unknown extension is an error rather
    /// than a guess — the same rule colour specs follow.
    /// </summary>
    public static ArchiveKind KindOf(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            CookBookExtension => ArchiveKind.CookBook,
            RecipeExtension => ArchiveKind.Recipe,
            IngredientExtension => ArchiveKind.Ingredient,
            KitchenExtension => ArchiveKind.Kitchen,
            "" => throw new NotSupportedException($"'{path}' has no extension; {Expected}"),
            var other => throw new NotSupportedException(
                $"Unknown archive extension '{other}'; {Expected}"),
        };

    private static string Expected =>
        $"expected one of {CookBookExtension}, {RecipeExtension}, {IngredientExtension}, {KitchenExtension}.";
}
