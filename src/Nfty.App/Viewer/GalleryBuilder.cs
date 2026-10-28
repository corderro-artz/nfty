using System.Globalization;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Nfty.Core.Output;
using Nfty.Core.Publish;
using Nfty.Ticket;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Nfty.App.Viewer;

/// <summary>
/// Builds the galleries the viewer shows for each kind of archive. Every builder reads manifests
/// (or a Set's metadata) and decodes no picture: a tile's loader runs only when a view realizes it.
/// </summary>
public static class GalleryBuilder
{
    private static ThumbnailGate DefaultGate() => ThumbnailGate.For(Nfty.Core.Parallelism.Default);

    private static string Inv(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>A CookBook: one tile per variant of every ingredient of every recipe.</summary>
    /// <param name="path">The <c>.cbk</c>.</param>
    /// <param name="gate">The decode limit; null for the app default.</param>
    /// <returns>The gallery; the caller disposes it.</returns>
    public static ArchiveGalleryViewModel ForCookBook(string path, ThumbnailGate? gate = null)
    {
        gate ??= DefaultGate();
        var tree = ArchivePeek.CookBookTree(path);
        var items = new List<GalleryItem>();
        foreach (var recipe in tree.Recipes)
            AddRecipe(items, path, recipe, recipe.Manifest.Id, tree.Manifest.Name, gate);
        var facts = new List<ViewerFact>
        {
            new("COOKBOOK", tree.Manifest.Name),
            new("RECIPES", tree.Recipes.Count.ToString(CultureInfo.InvariantCulture)),
            new("CANVAS", $"{tree.Manifest.Canvas.Width} x {tree.Manifest.Canvas.Height}"),
            new("VARIANTS", items.Count.ToString(CultureInfo.InvariantCulture)),
        };
        return new ArchiveGalleryViewModel(tree.Manifest.Name, facts, items, gate);
    }

    /// <summary>A Recipe: one tile per variant of every ingredient.</summary>
    /// <param name="path">The <c>.rcp</c>.</param>
    /// <param name="gate">The decode limit; null for the app default.</param>
    /// <returns>The gallery; the caller disposes it.</returns>
    public static ArchiveGalleryViewModel ForRecipe(string path, ThumbnailGate? gate = null)
    {
        gate ??= DefaultGate();
        var recipe = ArchivePeek.RecipeTree(path);
        var items = new List<GalleryItem>();
        AddRecipe(items, path, recipe, null, null, gate);
        var facts = new List<ViewerFact>
        {
            new("RECIPE", recipe.Manifest.Name),
            new("LAYERS", recipe.Manifest.LayerOrder.Count.ToString(CultureInfo.InvariantCulture)),
            new("VARIANTS", items.Count.ToString(CultureInfo.InvariantCulture)),
        };
        return new ArchiveGalleryViewModel(recipe.Manifest.Name, facts, items, gate);
    }

    /// <summary>An Ingredient: one tile per variant.</summary>
    /// <param name="path">The <c>.igt</c>.</param>
    /// <param name="gate">The decode limit; null for the app default.</param>
    /// <returns>The gallery; the caller disposes it.</returns>
    public static ArchiveGalleryViewModel ForIngredient(string path, ThumbnailGate? gate = null)
    {
        gate ??= DefaultGate();
        var ing = ArchivePeek.Ingredient(path);
        var items = new List<GalleryItem>();
        AddIngredient(items, path, null, null, ing, ing.Name, gate);
        var facts = new List<ViewerFact>
        {
            new("LAYER", ing.Name),
            new("KIND", ing.Kind.ToString()),
            new("VARIANTS", ing.Variants.Count.ToString(CultureInfo.InvariantCulture)),
        };
        return new ArchiveGalleryViewModel(ing.Name, facts, items, gate);
    }

    private static void AddRecipe(List<GalleryItem> items, string path, PeekedRecipe recipe, string? recipeId,
        string? bookName, ThumbnailGate gate)
    {
        // Paint order where the recipe names the layer; an ingredient it does not name goes last.
        var order = recipe.Manifest.LayerOrder.ToList();
        foreach (var ing in recipe.Ingredients.OrderBy(i =>
                 { var at = order.IndexOf(i.Id); return at < 0 ? int.MaxValue : at; }))
            AddIngredient(items, path, recipeId, ing.Id, ing, recipe.Manifest.Name, gate);
    }

    private static void AddIngredient(List<GalleryItem> items, string path, string? recipeId,
        string? ingredientId, IngredientManifest ing, string context, ThumbnailGate gate)
    {
        foreach (var v in ing.Variants)
        {
            var variant = v;
            string caption = recipeId is null && ingredientId is null
                ? variant.Name
                : $"{ing.Name} / {variant.Name}";   // the recipe is the rail's and the tooltip's business
            items.Add(new GalleryItem(caption,
                () => ArchivePeek.VariantPng(path, recipeId, ingredientId, variant.Id),
                () => Task.Run<ViewerSubject>(() =>
                {
                    using var art = Image.Load<Rgba32>(ArchivePeek.VariantPng(path, recipeId, ingredientId, variant.Id));
                    var facts = new List<ViewerFact>
                    {
                        new("VARIANT", variant.Name),
                        new("LAYER", ing.Name),
                        new("KIND", ing.Kind.ToString()),
                        new("WEIGHT", Inv(variant.Weight)),
                        new("SIZE", $"{art.Width} x {art.Height}"),
                        new("ARCHIVE", Path.GetFileName(path)),
                    };
                    if (ing.Kind != LayerKind.Custom)
                        facts.Add(new("SHOWN AS", "its stored gray value-map"));
                    return new SnapshotSubject($"{ing.Name} / {variant.Name}", facts, art, null);
                }), gate));
        }
    }

    /// <summary>A cooked Set: one tile per asset in Set numbering.</summary>
    /// <param name="set">The Set; the gallery takes ownership and disposes it.</param>
    /// <param name="gate">The decode limit; null for the app default.</param>
    /// <param name="bookCandidates">CookBook paths the app can name, for a tile's model.</param>
    /// <returns>The gallery. Over a sealed Set every tile's viewer refuses export.</returns>
    public static ArchiveGalleryViewModel ForSet(LoadedSet set, ThumbnailGate? gate = null,
        Func<IEnumerable<string>>? bookCandidates = null)
    {
        gate ??= DefaultGate();
        var items = new List<GalleryItem>();
        // By the NUMBER, never the file name: the stem stops padding at 9,999 and 10000 sorts
        // before 9999 as text.
        foreach (var it in set.Items.OrderBy(i => i.Number))
        {
            var item = it;
            items.Add(new GalleryItem("#" + SetLayout.Stem(item.Number),
                () => File.ReadAllBytes(item.ImagePath),
                async () => await ModelSubjects.ForSetAssetAsync(set, item, bookCandidates), gate));
        }
        bool sealedSet = set is SealedSet;
        var facts = new List<ViewerFact>
        {
            new("COLLECTION", set.Manifest.Name),
            new("ASSETS", set.Manifest.Count.ToString(CultureInfo.InvariantCulture)),
            new("SEED", set.Manifest.Seed),
        };
        if (sealedSet) facts.Add(new("SEALED", "view only"));
        return new ArchiveGalleryViewModel(set.Manifest.Name, facts, items, gate, [set], allowExport: !sealedSet);
    }

    /// <summary>A Menu: one tile per art cell, cropped by its rect.</summary>
    /// <param name="path">The <c>.mnu</c>.</param>
    /// <param name="gate">The decode limit; null for the app default.</param>
    /// <returns>The gallery; the caller disposes it.</returns>
    public static ArchiveGalleryViewModel ForMenu(string path, ThumbnailGate? gate = null)
    {
        gate ??= DefaultGate();
        var menu = MenuArchive.Open(path);
        try
        {
            var cells = new MenuCells(menu);
            var items = new List<GalleryItem>();
            foreach (var c in menu.Book.Art.Cells)
            {
                string key = c.Key;
                items.Add(new GalleryItem(key, () => cells.Png(key),
                    () => Task.Run<ViewerSubject>(() =>
                    {
                        using var art = Image.Load<Rgba32>(cells.Png(key));
                        var facts = new List<ViewerFact>
                        {
                            new("CELL", key),
                            new("SIZE", $"{art.Width} x {art.Height}"),
                            new("ARCHIVE", Path.GetFileName(path)),
                        };
                        return new SnapshotSubject(key, facts, art, null);
                    }), gate));
            }
            var facts = new List<ViewerFact>
            {
                new("MENU", Path.GetFileNameWithoutExtension(path)),
                new("ART", menu.Book.Art.Mode.ToString()),
                new("CELLS", items.Count.ToString(CultureInfo.InvariantCulture)),
                new("RECIPES", menu.Book.Recipes.Count.ToString(CultureInfo.InvariantCulture)),
            };
            return new ArchiveGalleryViewModel(Path.GetFileNameWithoutExtension(path), facts, items, gate,
                [cells, menu]);
        }
        catch { menu.Dispose(); throw; }
    }

    /// <summary>A Kitchen: one tile per archive its folder holds; a tile opens that archive's gallery.</summary>
    /// <param name="path">The <c>.ktn</c>.</param>
    /// <param name="openPath">Opens an archive by path (the opener's own routing); null result means
    /// the opener already said why it could not.</param>
    /// <param name="gate">The decode limit; null for the app default.</param>
    /// <returns>The gallery; the caller disposes it.</returns>
    public static ArchiveGalleryViewModel ForKitchen(string path,
        Func<string, Task<ViewerSubject?>> openPath, ThumbnailGate? gate = null)
    {
        gate ??= DefaultGate();
        var k = Kitchen.Open(path);
        var items = new List<GalleryItem>();
        foreach (var p in k.CookBooks.Concat(k.Recipes).Concat(k.Ingredients))
        {
            string archive = p;
            items.Add(new GalleryItem(Path.GetFileName(archive), null,
                async () => await openPath(archive) ?? throw new OperationCanceledException(), gate));
        }
        var facts = new List<ViewerFact>
        {
            new("KITCHEN", k.Manifest.Name),
            new("COOKBOOKS", k.CookBooks.Count.ToString(CultureInfo.InvariantCulture)),
            new("RECIPES", k.Recipes.Count.ToString(CultureInfo.InvariantCulture)),
            new("INGREDIENTS", k.Ingredients.Count.ToString(CultureInfo.InvariantCulture)),
        };
        return new ArchiveGalleryViewModel(k.Manifest.Name, facts, items, gate);
    }

    /// <summary>Serializes reads of one Menu (a zip is not thread-safe) and keeps the decoded atlas, so
    /// forty tiles cost one decode of the atlas rather than forty.</summary>
    private sealed class MenuCells(MenuArchive menu) : IDisposable
    {
        private readonly object _sync = new();
        private Image<Rgba32>? _atlas;

        public byte[] Png(string key)
        {
            lock (_sync)
            {
                var cell = menu.Cell(key);
                if (menu.Book.Art.Mode != MenuArtMode.Atlas) return menu.ArtBytes(key);
                _atlas ??= Image.Load<Rgba32>(menu.ArtBytes(key));
                using var crop = _atlas.Clone(c => c.Crop(new Rectangle(cell.X, cell.Y, cell.Width, cell.Height)));
                using var ms = new MemoryStream();
                crop.SaveAsPng(ms);
                return ms.ToArray();
            }
        }

        public void Dispose()
        {
            lock (_sync) { _atlas?.Dispose(); _atlas = null; }
        }
    }
}
