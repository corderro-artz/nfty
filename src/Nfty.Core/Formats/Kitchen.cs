using Nfty.Core.Model;

namespace Nfty.Core.Formats;

/// <summary>What a Kitchen holds, as PATHS rather than loaded graphs.
///
/// Deliberately not <c>LoadedCookBook</c>/<c>LoadedRecipe</c>/<c>LoadedIngredient</c>: reading an
/// archive eagerly decodes every PNG inside it, so materialising a whole folder just to list it
/// would pull the entire workspace into memory — the opposite of what a listing is for. Callers open
/// what the user actually picks.
///
/// Every list is sorted with <see cref="StringComparer.Ordinal"/>. Nothing here reaches an output
/// file, but a workspace listing that reorders itself by machine locale is its own small bug, and
/// the ordinal rule is the one this codebase keeps everywhere else.</summary>
public sealed record KitchenContents(
    KitchenManifest Manifest,
    string Directory,
    IReadOnlyList<string> CookBooks,
    IReadOnlyList<string> Recipes,
    IReadOnlyList<string> Ingredients)
{
    /// <summary>Nothing has been put in this Kitchen yet — a fresh workspace, not a broken one.</summary>
    public bool IsEmpty => CookBooks.Count == 0 && Recipes.Count == 0 && Ingredients.Count == 0;

    public int ItemCount => CookBooks.Count + Recipes.Count + Ingredients.Count;
}

/// <summary>
/// The Kitchen workspace: a <c>.ktn</c> file that names the folder it sits in.
///
/// Membership is discovered by scanning that folder, never recorded in the manifest. A recorded list
/// goes stale the moment a file is renamed, moved or deleted outside the app, and the Kitchen then
/// describes a workspace that no longer exists. Scanning makes the filesystem the single source of
/// truth and means moving a file in or out needs no reconciliation.
/// </summary>
public static class Kitchen
{
    public const string Extension = ".ktn";

    /// <summary>Creates a Kitchen: writes <paramref name="path"/> and ensures its folder exists.
    /// The folder IS the workspace, so a .ktn without one is not a meaningful state.</summary>
    public static void Create(string path, KitchenManifest manifest)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
        KitchenArchive.Write(path, manifest);
    }

    /// <summary>Opens a Kitchen and lists what its folder holds.
    ///
    /// Only the immediate directory is scanned. Recursing would make a Kitchen opened at a high
    /// level swallow everything beneath it, and nested Kitchens are explicitly out of scope — a
    /// workspace inside a workspace doubles every path rule and buys nothing here.</summary>
    public static KitchenContents Open(string path)
    {
        var manifest = KitchenArchive.Read(path);
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))
            ?? throw new InvalidDataException($"Kitchen '{path}' has no containing folder.");

        return new KitchenContents(manifest, dir,
            Scan(dir, Archives.CookBookExtension),
            Scan(dir, Archives.RecipeExtension),
            Scan(dir, Archives.IngredientExtension));
    }

    public static async Task<KitchenContents> OpenAsync(string path,
        CancellationToken cancellationToken = default)
    {
        var manifest = await KitchenArchive.ReadAsync(path, cancellationToken);
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))
            ?? throw new InvalidDataException($"Kitchen '{path}' has no containing folder.");

        return new KitchenContents(manifest, dir,
            Scan(dir, Archives.CookBookExtension),
            Scan(dir, Archives.RecipeExtension),
            Scan(dir, Archives.IngredientExtension));
    }

    /// <summary>The .ktn a folder is a workspace for, or null when it is just a folder. A folder
    /// holding more than one .ktn is ambiguous rather than "the first one wins" — picking silently
    /// would give two Kitchens the same contents and neither would be wrong.</summary>
    public static string? FindIn(string directory)
    {
        if (!System.IO.Directory.Exists(directory)) return null;
        var found = System.IO.Directory.GetFiles(directory, "*" + Extension)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
        return found.Count == 1 ? found[0] : null;
    }

    private static IReadOnlyList<string> Scan(string dir, string extension) =>
        System.IO.Directory.GetFiles(dir, "*" + extension)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
}
