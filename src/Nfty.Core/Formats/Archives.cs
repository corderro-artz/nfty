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
    /// <summary>The CookBook archive extension.</summary>
    public const string CookBookExtension = ".cbk";
    /// <summary>The Recipe archive extension.</summary>
    public const string RecipeExtension = ".rcp";
    /// <summary>The Ingredient archive extension.</summary>
    public const string IngredientExtension = ".igt";
    /// <summary>The Kitchen workspace extension.</summary>
    public const string KitchenExtension = ".ktn";

    /// <summary>
    /// The archive kind for <paramref name="path"/>. An unknown extension is an error rather
    /// than a guess — the same rule color specs follow.
    /// </summary>
    public static ArchiveKind KindOf(string path) =>
        TryKindOf(path, out var kind) ? kind
        : throw new NotSupportedException(Path.GetExtension(path) is { Length: > 0 } ext
            ? $"Unknown archive extension '{ext}'; {Expected}"
            : $"'{path}' has no extension; {Expected}");

    /// <summary>
    /// The archive kind for <paramref name="path"/>, without throwing when there is not one.
    ///
    /// <para>Exists so a caller that must not throw — a <c>System.CommandLine</c> validator, a GUI
    /// enabling a menu item — can ask the same question <see cref="KindOf"/> answers, rather than
    /// re-implementing the extension table beside it. There is one switch here and two ways in; a
    /// second copy of the mapping is how <c>preview</c> came to decide its form with a string compare
    /// while dispatching on <c>KindOf</c>, leaving a switch arm no input could reach.</para>
    /// </summary>
    /// <param name="path">The path to classify.</param>
    /// <param name="kind">The kind, when the extension names one.</param>
    /// <returns>True when the extension is one of the four.</returns>
    public static bool TryKindOf(string path, out ArchiveKind kind)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case CookBookExtension: kind = ArchiveKind.CookBook; return true;
            case RecipeExtension: kind = ArchiveKind.Recipe; return true;
            case IngredientExtension: kind = ArchiveKind.Ingredient; return true;
            case KitchenExtension: kind = ArchiveKind.Kitchen; return true;
            default: kind = default; return false;
        }
    }

    private static string Expected =>
        $"expected one of {CookBookExtension}, {RecipeExtension}, {IngredientExtension}, {KitchenExtension}.";
}
