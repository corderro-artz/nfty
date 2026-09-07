namespace Nfty.Core.Formats;

/// <summary>Which archive a file holds, as declared by its extension.</summary>
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

    /// <summary>
    /// <c>.set</c> — a cooked Set: generated images plus their metadata, packed.
    /// </summary>
    /// <remarks>
    /// The one kind here that is READ but never authored — nothing creates a <c>.set</c> except
    /// <c>SetWriter</c>, and there is no <c>new set</c> command. It was left out of this enum for
    /// that reason, and the omission cost more than it saved: <c>inspect</c> dispatches on
    /// <see cref="Archives.KindOf"/> and so refused the one archive a person is most likely to be
    /// handed by somebody else, while the manual said it did not. Meanwhile the GUI's
    /// <c>OpenRecent</c> had grown its own extension compare to route a <c>.set</c> around this
    /// enum — a second copy of the mapping, which is exactly what <see cref="Archives.TryKindOf"/>
    /// exists to prevent.
    /// </remarks>
    Set,

    /// <summary>
    /// <c>.tin</c> — a sealed export: a whole Set encrypted under a passphrase, marked view-only.
    /// </summary>
    /// <remarks>
    /// Its own extension rather than a <c>.set</c> whose bytes happen to be ciphertext. A recipient
    /// double-clicking it, and any tool downstream that ingests <c>.set</c> files, both have to be
    /// told before they open it — a file that announces what it is only after failing to parse is a
    /// worse answer than one whose name says so. It also gives <c>inspect</c> somewhere to route: a
    /// sealed archive answers a different set of questions from an open one, and answers most of
    /// them only with a key.
    /// </remarks>
    Sealed,
}

/// <summary>
/// Dispatch over the archive types by extension, so callers that accept "any nfty file"
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
    /// <summary>The cooked Set archive extension.</summary>
    public const string SetExtension = ".set";
    /// <summary>The sealed export extension.</summary>
    public const string SealedExtension = ".tin";

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
    /// <returns>True when the extension names one of the kinds.</returns>
    public static bool TryKindOf(string path, out ArchiveKind kind)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case CookBookExtension: kind = ArchiveKind.CookBook; return true;
            case RecipeExtension: kind = ArchiveKind.Recipe; return true;
            case IngredientExtension: kind = ArchiveKind.Ingredient; return true;
            case KitchenExtension: kind = ArchiveKind.Kitchen; return true;
            case SetExtension: kind = ArchiveKind.Set; return true;
            case SealedExtension: kind = ArchiveKind.Sealed; return true;
            default: kind = default; return false;
        }
    }

    private static string Expected =>
        $"expected one of {CookBookExtension}, {RecipeExtension}, {IngredientExtension}, "
        + $"{KitchenExtension}, {SetExtension}, {SealedExtension}.";
}
