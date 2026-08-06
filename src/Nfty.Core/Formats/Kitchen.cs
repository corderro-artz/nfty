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

    /// <summary>Total items across all three kinds.</summary>
    public int ItemCount => CookBooks.Count + Recipes.Count + Ingredients.Count;
}

/// <summary>Why <see cref="Kitchen.TryFindIn"/> did or did not find a workspace.</summary>
public enum KitchenLookup
{
    /// <summary>Exactly one <c>.ktn</c>: the folder is a workspace.</summary>
    Found,

    /// <summary>No <c>.ktn</c>, or no such folder — an ordinary directory, which is not an error.</summary>
    NotAWorkspace,

    /// <summary>More than one <c>.ktn</c>. Only the user can say which was meant, so this is
    /// reported rather than resolved by picking one.</summary>
    Ambiguous,
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
    /// <summary>The workspace file extension.</summary>
    public const string Extension = ".ktn";

    /// <summary>Creates a Kitchen: writes <paramref name="path"/> and ensures its folder exists.
    /// The folder IS the workspace, so a .ktn without one is not a meaningful state.</summary>
    /// <param name="path">Where to write the <c>.ktn</c>; its folder becomes the workspace.</param>
    /// <param name="manifest">The workspace's identity.</param>
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
    /// <param name="path">Path to the <c>.ktn</c>.</param>
    /// <returns>The manifest plus what the folder holds, as paths.</returns>
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

    /// <summary>Opens a Kitchen and lists what its folder holds.</summary>
    /// <param name="path">Path to the <c>.ktn</c>.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The manifest plus what the folder holds, as paths.</returns>
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

    /// <summary>The .ktn a folder is a workspace for, or null when it is not one. Convenience over
    /// <see cref="TryFindIn"/> for callers that genuinely do not care <em>why</em> — most do, since
    /// "this folder is not a workspace" and "this folder holds two of them" need different things
    /// said to the user.</summary>
    /// <param name="directory">The folder to look in.</param>
    /// <returns>The path to the single <c>.ktn</c>, or null.</returns>
    public static string? FindIn(string directory) =>
        TryFindIn(directory) is (KitchenLookup.Found, var path) ? path : null;

    /// <summary>
    /// Looks for the <c>.ktn</c> a folder is a workspace for, and says which of the three outcomes
    /// it is.
    ///
    /// <para>A folder holding more than one <c>.ktn</c> is <see cref="KitchenLookup.Ambiguous"/>
    /// rather than "the first one wins": picking silently would give two Kitchens the same contents
    /// and neither would be wrong. That is a state only the user can resolve, so a caller has to be
    /// able to tell them about it — which a bare null cannot.</para>
    /// </summary>
    /// <param name="directory">The folder to look in.</param>
    /// <returns>The outcome, and the path when — and only when — it is
    /// <see cref="KitchenLookup.Found"/>.</returns>
    public static (KitchenLookup Outcome, string? Path) TryFindIn(string directory)
    {
        if (!System.IO.Directory.Exists(directory)) return (KitchenLookup.NotAWorkspace, null);

        var found = System.IO.Directory.GetFiles(directory, "*" + Extension)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        return found.Count switch
        {
            1 => (KitchenLookup.Found, found[0]),
            0 => (KitchenLookup.NotAWorkspace, null),
            _ => (KitchenLookup.Ambiguous, null),
        };
    }

    private static IReadOnlyList<string> Scan(string dir, string extension) =>
        System.IO.Directory.GetFiles(dir, "*" + extension)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
}
