using System.Globalization;
using Nfty.App.Services;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using Nfty.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.App.Rise;

/// <summary>
/// One recipe, rolled: the asset the Recipe pane's hero shows for the same seed, risen with the
/// recipe's COMBINED depth (each pixel takes the depth of the topmost solid layer). Read-only — the
/// depth belongs to the layers' own variants, so changing it means opening one.
/// </summary>
/// <remarks>
/// The roll is <see cref="Generator.GenerateStreaming"/> with one asset and no uniqueness
/// enforcement (the hero's own call), so the art here and the hero are one roll, not two that agree.
/// </remarks>
public sealed class RecipeRiseSource : RiseSource
{
    private readonly LoadedCookBook _book;
    private readonly LoadedRecipe _recipe;
    private readonly IImageBridge _bridge;
    private readonly RiseItem _item;
    private readonly RiseItem[] _items;
    private List<RiseLayerRow> _layers = new();
    private string? _refusal;

    /// <summary>Rolls the recipe once.</summary>
    /// <param name="book">The book the recipe belongs to; borrowed.</param>
    /// <param name="recipe">The recipe to roll; borrowed.</param>
    /// <param name="seed">The seed of the roll.</param>
    /// <param name="bridge">Makes the thumbnails.</param>
    public RecipeRiseSource(LoadedCookBook book, LoadedRecipe recipe, int seed, IImageBridge bridge)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(bridge);
        _book = book;
        _recipe = recipe;
        _bridge = bridge;
        Seed = seed;

        var rolled = Roll();
        Problem = rolled.Problem;
        _refusal = rolled.Refusal;
        _layers = rolled.Rows;
        _item = new RiseItem("roll", recipe.Manifest.Name, CaptionFor(seed), BaseNameFor(seed),
            bridge.ToBitmap(rolled.Art), ownsThumbnail: true);
        _item.Attach(new RiseSubject(rolled.Art, rolled.Depth, ownsArt: true, rolled.Faces));
        _item.IsSelected = true;
        _items = new[] { _item };
    }

    /// <summary>The seed of the current roll.</summary>
    public int Seed { get; private set; }

    /// <summary>Why the book cannot cook (the generator's own message), or null when it can.</summary>
    public string? Problem { get; private set; }

    /// <summary>One row per layer of the recipe, bottom first.</summary>
    public IReadOnlyList<RiseLayerRow> Layers => _layers;

    /// <inheritdoc />
    public override RiseSourceKind Kind => RiseSourceKind.Recipe;

    /// <inheritdoc />
    public override string Kicker => $"{_recipe.Manifest.Name.ToUpperInvariant()} · LAYERS";

    /// <inheritdoc />
    public override string Title => _recipe.Manifest.Name;

    /// <inheritdoc />
    public override IReadOnlyList<RiseItem> Items => _items;

    /// <inheritdoc />
    public override RiseItem InitialItem => _item;

    /// <inheritdoc />
    public override IReadOnlyList<string> Facts
    {
        get
        {
            var c = _book.Manifest.Canvas;
            return new[]
            {
                string.Create(CultureInfo.InvariantCulture, $"seed {Seed}"),
                string.Create(CultureInfo.InvariantCulture, $"canvas {c.Width} × {c.Height}"),
                "1 → paints first, furthest back",
            };
        }
    }

    /// <inheritdoc />
    public override string BatchStem => $"{_recipe.Manifest.Name}-models";

    /// <inheritdoc />
    public override string? ReadOnlyPill => "combined depth · read-only";

    /// <inheritdoc />
    public override string RailFootNote => "Layers are read-only here.";

    /// <inheritdoc />
    public override string DepthNote => Problem is { } p
        ? $"This book cannot cook, so there is no roll to rise: {p}"
        : "A recipe shows its stack's combined depth: each pixel takes the depth of the topmost solid layer. "
          + "Change a layer's depth on its own variant."
          + (_refusal is { } why ? $" {why} The stack has no combined depth, so it is paper." : "");

    /// <inheritdoc />
    public override string OpeningLine => Problem is null
        ? $"Rolled with seed {Seed} — reroll from the toolstrip. Depth is combined from the layers and read-only here."
        : $"“{_recipe.Manifest.Name}” cannot be rolled: {Problem}";

    /// <inheritdoc />
    public override RiseSubject Open(RiseItem item) =>
        item.Subject ?? throw new InvalidOperationException("The roll was not loaded.");

    /// <summary>Moves to the next seed and rolls again, replacing the item's subject and the layer rows.</summary>
    public void Reroll()
    {
        Seed++;
        var rolled = Roll();
        Problem = rolled.Problem;
        _refusal = rolled.Refusal;

        var oldRows = _layers;
        var oldThumb = _item.Thumbnail;
        _layers = rolled.Rows;
        _item.Thumbnail = _bridge.ToBitmap(rolled.Art);
        _item.Relabel(CaptionFor(Seed), BaseNameFor(Seed));
        var oldSubject = _item.Replace(new RiseSubject(rolled.Art, rolled.Depth, ownsArt: true, rolled.Faces));
        RaiseChanged();

        // The old picture, thumbnails and layer rows go only AFTER the page has been told: until it
        // has, its bindings (the 3D view's texture, the layer rail) still read them, and anything
        // that paints synchronously inside the change would be reading disposed images.
        oldSubject?.Dispose();
        oldThumb?.Dispose();
        foreach (var r in oldRows) r.Dispose();
    }

    private string CaptionFor(int seed) => string.Create(CultureInfo.InvariantCulture, $"seed {seed}");

    private string BaseNameFor(int seed) => string.Create(CultureInfo.InvariantCulture, $"{_recipe.Manifest.Name}-{seed}");

    private sealed record Rolled(Image<Rgba32> Art, DepthMap? Depth, string? Problem, List<RiseLayerRow> Rows,
        string? Refusal = null, StackFaces? Faces = null);

    private Rolled Roll()
    {
        GeneratedAsset? asset = null;
        Image<Rgba32>? art = null;
        StackFaces? faces = null;
        var rows = new List<RiseLayerRow>();
        try
        {
            asset = Generator.GenerateStreaming(_book, new GenerateOptions(1,
                Seed.ToString(CultureInfo.InvariantCulture), _recipe.Manifest.Id, EnforceUniqueDna: false)).First();
            art = asset.Image.Clone();
            DepthMap? depth = null;
            string? refusal = null;
            try
            {
                using var rise = StackDepth.ForAsset(_recipe, asset, $"the {_recipe.Manifest.Name} recipe");
                depth = rise?.Depth;
                faces = rise?.Faces.Clone();    // the rise is let go below; the subject keeps its own copy
            }
            catch (PartialAlphaException ex) { refusal = ex.Message; }   // which layer keeps the stack 2D
            FillRows(asset, rows);
            return new Rolled(art, depth, null, rows, refusal, faces);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // An invalid book (or no roll at all) is a state to show, not a crash: a clear canvas, the
            // reason. Whatever was built before the failure is given back, not leaked.
            art?.Dispose();
            faces?.Dispose();
            foreach (var r in rows) r.Dispose();
            var c = _book.Manifest.Canvas;
            return new Rolled(new Image<Rgba32>(c.Width, c.Height), null, ex.Message, new List<RiseLayerRow>());
        }
        finally
        {
            asset?.Dispose();
        }
    }

    private void FillRows(GeneratedAsset asset, List<RiseLayerRow> rows)
    {
        var byId = new Dictionary<string, LoadedIngredient>(StringComparer.Ordinal);
        foreach (var ing in _recipe.Ingredients) byId[ing.Manifest.Id] = ing;
        var picked = asset.Traits.ToDictionary(t => t.IngredientId, t => t, StringComparer.Ordinal);

        int depth = 0;
        foreach (var id in _recipe.Manifest.LayerOrder)
        {
            depth++;
            var ing = byId[id];
            if (!picked.TryGetValue(id, out var trait))
            {
                rows.Add(new RiseLayerRow(depth, ing.Manifest.Name, "left out of this roll", ing.Manifest.Kind,
                    "", isAbsent: true, thumbnail: null));
                continue;
            }

            var roll = asset.ColorRolls.FirstOrDefault(r => r.LayerId == id);
            using var rendered = roll is { H: { } h, S: { } s }
                ? VariantPreview.Render(ing, trait.VariantId, new RolledColor(h, s))
                : ing.VariantImages[trait.VariantId].Clone();   // Custom, or a roll that recorded no color
            var own = new DepthSurface(ing.VariantImages[trait.VariantId], ing.Depths.GetValueOrDefault(trait.VariantId));
            rows.Add(new RiseLayerRow(depth, ing.Manifest.Name, trait.VariantName, ing.Manifest.Kind,
                RiseItem.ChipFor(own), isAbsent: false, thumbnail: _bridge.ToBitmap(rendered)));
        }
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        base.Dispose();
        foreach (var r in _layers) r.Dispose();
        _layers = new List<RiseLayerRow>();
    }
}
