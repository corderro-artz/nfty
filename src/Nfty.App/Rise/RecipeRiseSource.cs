using System.Globalization;
using Nfty.App.Services;
using Nfty.Core.Diagnostics;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using Nfty.Core.Stats;
using Nfty.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.App.Rise;

/// <summary>The layer of a rolled recipe the page is painting on: one variant, and the surface over its own stored picture.</summary>
/// <param name="IngredientId">The layer's id.</param>
/// <param name="VariantId">The rolled variant's id.</param>
/// <param name="Name">Layer and variant, e.g. <c>Lock / Gold</c>.</param>
/// <param name="Surface">The depth surface a stroke edits.</param>
public sealed record RiseLayerTarget(string IngredientId, string VariantId, string Name, DepthSurface Surface);

/// <summary>
/// One recipe, rolled: the asset the Recipe pane's hero shows for the same seed, risen with the
/// recipe's COMBINED depth (each pixel reaches as far to the front as any solid layer there, and as far to the back).
/// </summary>
/// <remarks>
/// The roll is <see cref="Generator.GenerateStreaming"/> with one asset and no uniqueness
/// enforcement (the hero's own call), so the art here and the hero are one roll, not two that agree.
///
/// <para>With a session the source WRITES THROUGH: depth belongs to the layers' own variants, so a stroke edits
/// the one layer the page is targeting (a surface over that variant's stored picture), and the union the
/// page shows is recombined from every present layer's current map. Nothing about the union is saved;
/// <see cref="SaveAsync"/> writes each dirty variant's own map into the book in one persist.</para>
/// </remarks>
public sealed class RecipeRiseSource : RiseSource
{
    private LoadedCookBook _book;
    private LoadedRecipe _recipe;
    private readonly IImageBridge _bridge;
    private readonly ICookBookSession? _session;
    private readonly Func<bool>? _isEditing;
    private readonly RiseItem _item;
    private readonly RiseItem[] _items;
    private readonly Dictionary<(string Ing, string Variant), VariantEdit> _edits = new();
    private readonly TargetedHistory<(string Ing, string Variant), DepthPixel> _history = new();
    private readonly RecipeDepthEditor _editor;
    private List<RiseLayerRow> _layers = new();
    private List<RollPick> _picks = new();
    // The combined stack of the roll, kept between strokes: a stroke recombines only the rectangle it touched.
    private StackRise? _rise;
    private string? _refusal;
    private RiseLayerTarget? _target;
    private Image<Rgba32>? _below, _targetArt, _above;
    // ONE write at a time on this book: a second save joins, a reroll or retarget is refused, never queued.
    //
    // This flag is deliberately NOT shared with the Explorer's reorder flag. The Rise page is pushed over the
    // Explorer (INavigationService.To), so while it is on screen no tree drag, Alt+Up chord or Add/Delete can be
    // raised: the only other writers of the book are the ones already in flight when Rise opened, and those are
    // met by the guards in SaveCoreAsync (the live book is re-read at save time and every touched variant's
    // picture and depth must still be what the edit began from). Sharing a flag would also make a reorder that
    // is still persisting refuse a save with no sentence to say why.
    private bool _writeBusy;

    /// <summary>Rolls the recipe once.</summary>
    /// <param name="book">The book the recipe belongs to; borrowed.</param>
    /// <param name="recipe">The recipe to roll; borrowed.</param>
    /// <param name="seed">The seed of the roll.</param>
    /// <param name="bridge">Makes the thumbnails.</param>
    /// <param name="session">The open book, which Save writes through; null keeps the source read-only.</param>
    /// <param name="isEditing">Whether the edit lock is open; null means nothing locks the book.</param>
    public RecipeRiseSource(LoadedCookBook book, LoadedRecipe recipe, int seed, IImageBridge bridge,
        ICookBookSession? session = null, Func<bool>? isEditing = null)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(bridge);
        _book = book;
        _recipe = recipe;
        _bridge = bridge;
        _session = session;
        _isEditing = isEditing;
        _editor = new RecipeDepthEditor(this);
        Seed = seed;

        var rolled = Roll();
        Problem = rolled.Problem;
        _refusal = rolled.Refusal;
        _layers = rolled.Rows;
        TakePicks(rolled);
        _item = new RiseItem("roll", recipe.Manifest.Name, CaptionFor(seed), BaseNameFor(seed),
            bridge.ToBitmap(rolled.Art), ownsThumbnail: true);
        _item.Attach(Subject(rolled));
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

    // ---------------------------------------------------------------- editability

    /// <inheritdoc />
    public override bool IsEditable => _session is not null && Problem is null && _refusal is null;

    /// <inheritdoc />
    public override bool IsLocked => _isEditing is not null && !_isEditing();

    /// <inheritdoc />
    public override string? ReadOnlyPill => IsEditable ? null : "combined depth · read-only";

    /// <inheritdoc />
    public override string RailFootNote => IsEditable
        ? "Click a layer, or its pixel, to paint its own depth."
        : "Layers are read-only here.";

    /// <inheritdoc />
    public override bool RailFootIsLock => !IsEditable;

    /// <inheritdoc />
    public override string SaveTargetName => "CookBook";

    /// <inheritdoc />
    public override string DepthNote => Problem is { } p
        ? $"This book cannot cook, so there is no roll to rise: {p}"
        : IsEditable
            ? (IsLocked
                ? "The CookBook is read-only. Try depth freely here — Save needs the lock opened in the Explorer."
                : "Depth is painted on one layer's own variant; the stack shows their union. "
                  + "Left picks the layer that owns a pixel's front, right the one that owns its back.")
              + (_refusal is { } why2 ? $" {why2}" : "")
            : "A recipe shows its stack's combined depth: the front reaches as far as any layer, the back as far as any layer, "
              + "and each face takes the color of the layer that owns it. "
              + "Change a layer's depth on its own variant."
              + (_refusal is { } why ? $" {why} The stack has no combined depth, so it is paper." : "");

    /// <inheritdoc />
    public override string OpeningLine => Problem is not null
        ? $"“{_recipe.Manifest.Name}” cannot be rolled: {Problem}"
        : IsEditable
            ? $"Rolled with seed {Seed} — pick a layer to paint its depth; reroll from the toolstrip."
            : $"Rolled with seed {Seed} — reroll from the toolstrip. Depth is combined from the layers and read-only here.";

    /// <inheritdoc />
    public override RiseSubject Open(RiseItem item) =>
        item.Subject ?? throw new InvalidOperationException("The roll was not loaded.");

    // ---------------------------------------------------------------- dirty / save state

    /// <inheritdoc />
    public override int DirtyCount => _edits.Values.Count(e => e.IsDirty);

    /// <inheritdoc />
    public override bool IsDirty => DirtyCount > 0;

    /// <inheritdoc />
    // Keyed on the SESSION, not on the current roll: depth painted on a variant stays unsaved work after a reroll
    // into a roll that cannot rise (partial alpha, a book that cannot cook). The edited variants' own pictures
    // were checked when they were painted and are stale-guarded at save.
    public override bool CanSave => _session is not null && !IsLocked && DirtyCount > 0;

    /// <inheritdoc />
    public override IDepthEditor EditorFor(RiseSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        return _editor;
    }

    /// <summary>The layer the page paints on, or null.</summary>
    public RiseLayerTarget? Target => _target;

    /// <summary>The sentence for an undo or redo that touched a layer this roll does not contain, or null.</summary>
    public string? LastUndoNote { get; private set; }

    /// <summary>How far the target variant's edit reaches, or null without a target.</summary>
    public VariantReach? TargetReach => _target is { } t
        ? VariantReach.Of(_book, _recipe.Manifest.Id, t.IngredientId, t.VariantId)
        : null;

    /// <summary>What paints under the target, composited; null without a target. Owned here.</summary>
    public Image<Rgba32>? BelowArt => _below;

    /// <summary>The target layer's own colorized picture; null without a target. Owned here.</summary>
    public Image<Rgba32>? TargetArt => _targetArt;

    /// <summary>What paints over the target, composited; null without a target. Owned here.</summary>
    public Image<Rgba32>? AboveArt => _above;

    /// <summary>Whether the target layer is solid at a pixel (only a solid pixel takes depth).</summary>
    /// <param name="x">Column.</param>
    /// <param name="y">Row.</param>
    /// <returns>False without a target or out of bounds.</returns>
    public bool IsTargetSolid(int x, int y) => _target is { } t && t.Surface.IsSolid(x, y);

    /// <summary>Makes the layer that owns a pixel's front (or back) the painted layer. Paints nothing.</summary>
    /// <param name="x">Column.</param>
    /// <param name="y">Row.</param>
    /// <param name="side">Which face's owner to pick.</param>
    /// <returns>False on a clear pixel, out of bounds, while saving, or when the source is not editable.</returns>
    public bool TryPick(int x, int y, DepthSide side)
    {
        if (!IsEditable || _writeBusy) return false;
        int w = _book.Manifest.Canvas.Width;
        if (x < 0 || y < 0 || x >= w || y >= _book.Manifest.Canvas.Height) return false;
        int owner = _rise is null ? -1 : side == DepthSide.Front ? _rise.FrontOwner(x, y) : _rise.BackOwner(x, y);
        if (owner < 0 || owner >= _picks.Count) return false;
        SetTargetTo(_picks[owner]);
        return true;
    }

    /// <summary>Makes a layer the painted one, or drops the target.</summary>
    /// <param name="ingredientId">The layer, or null to drop the target.</param>
    /// <returns>False for an absent, unknown or not-editable layer, or while saving.</returns>
    public bool SetTarget(string? ingredientId)
    {
        if (_writeBusy) return false;
        if (ingredientId is null)
        {
            if (_target is null) return true;
            _target = null;
            AfterTargetChanged();
            return true;
        }
        if (!IsEditable) return false;
        var pick = _picks.FirstOrDefault(p => string.Equals(p.IngredientId, ingredientId, StringComparison.Ordinal));
        if (pick is null) return false;
        SetTargetTo(pick);
        return true;
    }

    /// <summary>Moves the target through the layers present in the roll.</summary>
    /// <param name="delta">+1 toward the top of the stack (PageUp), -1 toward the bottom. With no target, +1
    /// starts at the bottom layer and -1 at the top.</param>
    /// <returns>False when nothing moved: not editable, saving, the end of the stack, or no layer.</returns>
    public bool StepTarget(int delta)
    {
        if (!IsEditable || _writeBusy || _picks.Count == 0 || delta == 0) return false;
        int current = _target is { } t
            ? _picks.FindIndex(p => p.IngredientId == t.IngredientId && p.VariantId == t.VariantId)
            : -1;
        int next = current < 0 ? (delta > 0 ? 0 : _picks.Count - 1) : current + Math.Sign(delta);
        if (next < 0 || next >= _picks.Count || next == current) return false;
        SetTargetTo(_picks[next]);
        return true;
    }

    private void SetTargetTo(RollPick pick)
    {
        var edit = EditFor(pick.IngredientId, pick.VariantId);
        _target = new RiseLayerTarget(pick.IngredientId, pick.VariantId,
            $"{edit.IngredientName} / {edit.VariantName}", edit.Surface);
        AfterTargetChanged();
    }

    private void AfterTargetChanged()
    {
        foreach (var row in _layers)
            row.IsTarget = _target is { } t && row.VariantId is not null
                && row.IngredientId == t.IngredientId && row.VariantId == t.VariantId;
        var old = RebuildDimArt();
        RaiseChanged();
        foreach (var image in old) image.Dispose();
    }

    // ---------------------------------------------------------------- reroll

    /// <summary>Moves to the next seed and rolls again, replacing the item's subject and the layer rows. Painted
    /// depth stays on its variant: a variant that is in the new roll shows it, one that is not keeps it until saved.
    /// Refused while a save runs.</summary>
    public void Reroll()
    {
        if (_writeBusy) return;
        Seed++;
        var rolled = Roll();
        Problem = rolled.Problem;
        _refusal = rolled.Refusal;

        var oldRows = _layers;
        var oldThumb = _item.Thumbnail;
        var oldPicks = _picks;
        var oldRise = _rise;
        _layers = rolled.Rows;
        TakePicks(rolled);
        _item.Thumbnail = _bridge.ToBitmap(rolled.Art);
        _item.Relabel(CaptionFor(Seed), BaseNameFor(Seed));
        var oldSubject = _item.Replace(Subject(rolled));

        // The target survives a roll that still holds its exact variant; otherwise it is dropped (its depth is kept).
        if (_target is { } t && !_picks.Any(p => p.IngredientId == t.IngredientId && p.VariantId == t.VariantId))
            _target = null;
        if (!IsEditable) _target = null;
        foreach (var row in _layers)
            row.IsTarget = _target is { } tt && row.VariantId is not null
                && row.IngredientId == tt.IngredientId && row.VariantId == tt.VariantId;
        var oldDim = RebuildDimArt();
        LastUndoNote = null;
        RaiseChanged();

        // The old picture, thumbnails and layer rows go only AFTER the page has been told: until it
        // has, its bindings (the 3D view's texture, the layer rail) still read them, and anything
        // that paints synchronously inside the change would be reading disposed images.
        oldSubject?.Dispose();
        oldRise?.Dispose();
        oldThumb?.Dispose();
        foreach (var r in oldRows) r.Dispose();
        foreach (var p in oldPicks) p.Dispose();
        foreach (var image in oldDim) image.Dispose();
    }

    /// <summary>The subject of a roll; if building its atlas throws, the roll's art and faces are given back.</summary>
    private static RiseSubject Subject(Rolled rolled)
    {
        try { return new RiseSubject(rolled.Art, rolled.Depth, ownsArt: true, rolled.Faces); }
        catch
        {
            rolled.Art.Dispose();
            rolled.Faces?.Dispose();
            throw;
        }
    }

    private string CaptionFor(int seed) => string.Create(CultureInfo.InvariantCulture, $"seed {seed}");

    private string BaseNameFor(int seed) => string.Create(CultureInfo.InvariantCulture, $"{_recipe.Manifest.Name}-{seed}");

    // ---------------------------------------------------------------- the roll

    /// <summary>One present layer of the roll: its rolled variant and that variant's picture as generation colors it.</summary>
    private sealed class RollPick(string ingredientId, string variantId, Image<Rgba32> layer) : IDisposable
    {
        public string IngredientId { get; } = ingredientId;
        public string VariantId { get; } = variantId;
        public Image<Rgba32> Layer { get; } = layer;
        public void Dispose() => Layer.Dispose();
    }

    private sealed record Rolled(Image<Rgba32> Art, DepthMap? Depth, string? Problem, List<RiseLayerRow> Rows,
        string? Refusal = null, StackFaces? Faces = null, List<RollPick>? Picks = null, StackRise? Rise = null);

    private void TakePicks(Rolled rolled)
    {
        _picks = rolled.Picks ?? new List<RollPick>();
        _rise = rolled.Rise;
        // The roll was combined from the edits as they stand, so whatever they changed before it is already in.
        foreach (var edit in _edits.Values) edit.Surface.TakeDirty();
    }

    private Rolled Roll()
    {
        GeneratedAsset? asset = null;
        Image<Rgba32>? art = null;
        StackFaces? faces = null;
        List<RollPick>? picks = null;
        var rows = new List<RiseLayerRow>();
        try
        {
            asset = Generator.GenerateStreaming(_book, new GenerateOptions(1,
                Seed.ToString(CultureInfo.InvariantCulture), _recipe.Manifest.Id, EnforceUniqueDna: false)).First();
            art = asset.Image.Clone();
            picks = BuildPicks(asset);
            DepthMap? depth = null;
            string? refusal = null;
            StackRise? kept = null;
            if (picks.Count > 0)
            {
                try
                {
                    kept = StackDepth.Combine(PairsOf(picks), $"the {_recipe.Manifest.Name} recipe", asset.Image);
                    depth = kept.Depth;
                    faces = kept.Faces.Clone();    // the page keeps the rise for its strokes; the subject has its own copy
                }
                catch (PartialAlphaException ex)   // which layer keeps the stack 2D
                {
                    refusal = ex.Message;
                    foreach (var p in picks) p.Dispose();
                    picks = new List<RollPick>();
                }
            }
            FillRows(asset, picks, rows, pickable: _session is not null && refusal is null);
            return new Rolled(art, depth, null, rows, refusal, faces, picks, kept);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // An invalid book (or no roll at all) is a state to show, not a crash: a clear canvas, the
            // reason. Whatever was built before the failure is given back, not leaked.
            art?.Dispose();
            faces?.Dispose();
            if (picks is not null) foreach (var p in picks) p.Dispose();
            foreach (var r in rows) r.Dispose();
            var c = _book.Manifest.Canvas;
            return new Rolled(new Image<Rgba32>(c.Width, c.Height), null, ex.Message, new List<RiseLayerRow>());
        }
        finally
        {
            asset?.Dispose();
        }
    }

    private List<RollPick> BuildPicks(GeneratedAsset asset)
    {
        var byId = new Dictionary<string, LoadedIngredient>(StringComparer.Ordinal);
        foreach (var ing in _recipe.Ingredients) byId[ing.Manifest.Id] = ing;   // last wins, as StackRoll
        var picks = new List<RollPick>(asset.Traits.Count);
        try
        {
            foreach (var trait in asset.Traits)
            {
                var ing = byId[trait.IngredientId];
                var roll = asset.ColorRolls.FirstOrDefault(r => r.LayerId == trait.IngredientId);
                var layer = roll is { H: { } h, S: { } s }
                    ? VariantPreview.Render(ing, trait.VariantId, new RolledColor(h, s))
                    : ing.VariantImages[trait.VariantId].Clone();   // Custom, or a roll that recorded no color
                picks.Add(new RollPick(trait.IngredientId, trait.VariantId, layer));
            }
            return picks;
        }
        catch
        {
            foreach (var p in picks) p.Dispose();
            throw;
        }
    }

    // Each layer paired with its CURRENT map: the dirty variant's surface, else what the book stores.
    private List<(Image<Rgba32>, DepthMap?)> PairsOf(IReadOnlyList<RollPick> picks) =>
        picks.Select(p => (p.Layer, MapFor(p))).ToList();

    private DepthMap? MapFor(RollPick p)
    {
        if (_edits.TryGetValue((p.IngredientId, p.VariantId), out var edit)) return edit.Surface.ToDepthMap();
        return _recipe.Ingredients.FirstOrDefault(i => i.Manifest.Id == p.IngredientId)?
            .Depths.GetValueOrDefault(p.VariantId);
    }

    // Each layer paired with a reader of its CURRENT depth: the dirty variant's surface, else what the book stores.
    private List<(Image<Rgba32> Layer, Func<int, int, DepthPixel>? Depth)> ReadersOf(IReadOnlyList<RollPick> picks) =>
        picks.Select(p =>
        {
            if (_edits.TryGetValue((p.IngredientId, p.VariantId), out var edit))
                return (p.Layer, (Func<int, int, DepthPixel>?)edit.Surface.Get);
            var map = _recipe.Ingredients.FirstOrDefault(i => i.Manifest.Id == p.IngredientId)?
                .Depths.GetValueOrDefault(p.VariantId);
            return (p.Layer, map is null ? null : (x, y) => new DepthPixel(map.Front(x, y), map.Back(x, y)));
        }).ToList();

    private void FillRows(GeneratedAsset asset, IReadOnlyList<RollPick> picks, List<RiseLayerRow> rows, bool pickable)
    {
        var byId = new Dictionary<string, LoadedIngredient>(StringComparer.Ordinal);
        foreach (var ing in _recipe.Ingredients) byId[ing.Manifest.Id] = ing;
        var traits = asset.Traits.ToDictionary(t => t.IngredientId, t => t, StringComparer.Ordinal);
        var layers = picks.ToDictionary(p => p.IngredientId, p => p, StringComparer.Ordinal);

        int depth = 0;
        foreach (var id in _recipe.Manifest.LayerOrder)
        {
            depth++;
            var ing = byId[id];
            if (!traits.TryGetValue(id, out var trait))
            {
                rows.Add(new RiseLayerRow(depth, ing.Manifest.Name, "left out of this roll", ing.Manifest.Kind,
                    "", isAbsent: true, thumbnail: null, ingredientId: id));
                continue;
            }

            // A variant painted in an earlier roll keeps its edit: the chip shows what the layer holds NOW.
            var own = _edits.TryGetValue((id, trait.VariantId), out var painted)
                ? painted.Surface
                : new DepthSurface(ing.VariantImages[trait.VariantId], ing.Depths.GetValueOrDefault(trait.VariantId));
            var thumb = layers.TryGetValue(id, out var pick) ? _bridge.ToBitmap(pick.Layer) : null;
            rows.Add(new RiseLayerRow(depth, ing.Manifest.Name, trait.VariantName, ing.Manifest.Kind,
                RiseItem.ChipFor(own), isAbsent: false, thumbnail: thumb,
                ingredientId: id, variantId: trait.VariantId, isPickable: pickable));
        }
    }

    // ---------------------------------------------------------------- recombine

    /// <summary>Rewrites every present row's depth chip from the variant's CURRENT map (the painted surface when
    /// there is one, else what the book stores). Rows are records of the roll; only this text moves.</summary>
    private void RefreshRowDepth()
    {
        foreach (var row in _layers)
        {
            if (row.VariantId is not { } variantId) continue;
            if (_edits.TryGetValue((row.IngredientId, variantId), out var edit))
                row.DepthText = RiseItem.ChipFor(edit.Surface);
        }
    }

    /// <summary>A whole combine of the roll from the layers' current maps, for a test to hold the incremental
    /// result against. The page itself never calls it.</summary>
    /// <returns>The rise; the caller disposes it. Null when the roll has no layer to combine.</returns>
    public StackRise? CombineFromScratch() => _picks.Count == 0 || _item.Subject is not { } subject
        ? null
        : StackDepth.Combine(PairsOf(_picks), $"the {_recipe.Manifest.Name} recipe", subject.Art);

    /// <summary>How many times a changed rectangle has been recombined since the page opened: one per stroke,
    /// undo or standing preview that changed a pixel, never two for the same change.</summary>
    public int RecombineCount { get; private set; }

    /// <summary>Brings the combined stack up to date with the layers' maps: only the rectangle the present
    /// layers' surfaces report changed (the brush footprint of a stroke, an undo, a standing preview), through
    /// the one stack rule, into the subject's depth, faces and texture in place. Nothing changed, nothing done.</summary>
    private void Recombine()
    {
        if (_item.Subject is not { } subject || _picks.Count == 0 || _rise is not { } rise) return;
        PixelRect? dirty = null;
        foreach (var p in _picks)
        {
            if (!_edits.TryGetValue((p.IngredientId, p.VariantId), out var edit)) continue;
            if (edit.Surface.TakeDirty() is not { } r) continue;
            dirty = dirty is not { } d ? r : Union(d, r);
        }
        if (dirty is not { } area) return;
        using var _ = Perf.Measure("Rise.Recombine");
        RecombineCount++;
        var rect = new Rectangle(area.X, area.Y, area.Width, area.Height);
        StackDepth.CombineInto(rise, ReadersOf(_picks), rect);
        subject.UpdateCombined(rise, rect);
    }

    private static PixelRect Union(PixelRect a, PixelRect b)
    {
        int x0 = Math.Min(a.X, b.X), y0 = Math.Min(a.Y, b.Y);
        int x1 = Math.Max(a.X + a.Width, b.X + b.Width), y1 = Math.Max(a.Y + a.Height, b.Y + b.Height);
        return new PixelRect(x0, y0, x1 - x0, y1 - y0);
    }

    private List<Image<Rgba32>> RebuildDimArt()
    {
        var old = new List<Image<Rgba32>>();
        if (_below is not null) old.Add(_below);
        if (_targetArt is not null) old.Add(_targetArt);
        if (_above is not null) old.Add(_above);
        _below = _targetArt = _above = null;
        if (_target is not { } t) return old;

        var present = _picks.Select(p => p.IngredientId).ToList();
        var split = StackPreview.Split(_recipe.Manifest, t.IngredientId, present);
        var canvas = _book.Manifest.Canvas;
        var layers = _picks.ToDictionary(p => p.IngredientId, p => p.Layer, StringComparer.Ordinal);
        _below = Compositor.Composite(canvas, split.Below.Select(id => layers[id]).ToList());
        _above = Compositor.Composite(canvas, split.Above.Select(id => layers[id]).ToList());
        _targetArt = layers[t.IngredientId].Clone();
        return old;
    }

    // ---------------------------------------------------------------- edits

    /// <summary>One variant's own painting: a surface over its STORED picture (stable across rerolls), what the
    /// book held when it began or was last saved, and a copy of that picture for the stale guard.</summary>
    private sealed class VariantEdit(DepthSurface surface, DepthMap? baseline, Image<Rgba32> art,
        string ingredientName, string variantName) : IDisposable
    {
        public DepthSurface Surface { get; } = surface;
        public DepthMap? Baseline { get; set; } = baseline;
        public Image<Rgba32> Art { get; } = art;
        public string IngredientName { get; } = ingredientName;
        public string VariantName { get; } = variantName;
        public bool IsDirty => !Surface.SameDepthAs(Baseline);
        public void Dispose() => Art.Dispose();
    }

    private VariantEdit EditFor(string ingredientId, string variantId)
    {
        if (_edits.TryGetValue((ingredientId, variantId), out var existing)) return existing;
        var ing = _recipe.Ingredients.FirstOrDefault(i => i.Manifest.Id == ingredientId)
            ?? throw new KeyNotFoundException($"Layer '{ingredientId}' is not in the recipe.");
        if (!ing.VariantImages.TryGetValue(variantId, out var image))
            throw new KeyNotFoundException($"Layer '{ingredientId}' has no variant '{variantId}'.");
        var surface = new DepthSurface(image, ing.Depths.GetValueOrDefault(variantId));
        var name = ing.Manifest.Variants.FirstOrDefault(v => v.Id == variantId)?.Name ?? variantId;
        // The baseline is the surface's own reading of the stored map (depth under clear pixels is dropped on
        // load), so a variant nobody touched is never dirty.
        var edit = new VariantEdit(surface, surface.ToDepthMap(), image.Clone(), ing.Manifest.Name, name);
        _edits[(ingredientId, variantId)] = edit;
        return edit;
    }

    private DepthSurface SurfaceOf((string Ing, string Variant) key) => EditFor(key.Ing, key.Variant).Surface;

    // Painting, undo and redo stay live WHILE A SAVE RUNS, on purpose (and so does the retarget an undo makes): SaveCoreAsync takes each variant's map
    // as a snapshot and marks THAT snapshot saved afterwards (edit.Baseline = what was written), so a stroke
    // painted during the write is simply still dirty when the write ends. Only changing WHICH layers are
    // present (reroll) or which variant is the target is refused, because those would swap the edits a
    // running save is iterating.
    private sealed class RecipeDepthEditor(RecipeRiseSource source) : IDepthEditor
    {
        public DepthSurface Surface => source._target?.Surface
            ?? (source._item.Subject ?? throw new InvalidOperationException("The roll was not loaded.")).Depth;

        public bool Do(IEditCommand<DepthPixel> cmd)
        {
            ArgumentNullException.ThrowIfNull(cmd);
            if (source._target is not { } t) return false;   // no layer picked: nothing to write to
            if (!source._history.Do((t.IngredientId, t.VariantId), cmd, t.Surface)) return false;
            source.LastUndoNote = null;
            source.Recombine();
            source.RefreshRowDepth();
            return true;
        }

        public bool CanUndo => source._history.CanUndo;

        public bool CanRedo => source._history.CanRedo;

        public void Undo()
        {
            if (!source._history.Undo(source.SurfaceOf, out var key)) return;
            source.AfterHistory(key);
        }

        public void Redo()
        {
            if (!source._history.Redo(source.SurfaceOf, out var key)) return;
            source.AfterHistory(key);
        }

        public void Sync() => source.Recombine();
    }

    // An undo or redo touched this variant: the union follows, and the page goes to the layer it touched.
    // A layer this roll does not hold is still restored (its depth is kept, unsaved), and the page says so.
    private void AfterHistory((string Ing, string Variant) key)
    {
        Recombine();
        var pick = _picks.FirstOrDefault(p => p.IngredientId == key.Ing && p.VariantId == key.Variant);
        string? note = null;
        if (pick is null)
        {
            var edit = EditFor(key.Ing, key.Variant);
            note = $"Changed {edit.IngredientName} / {edit.VariantName}, which this roll does not include. Reroll to see it.";
        }
        bool changedTarget = pick is not null && (_target is not { } t || t.IngredientId != key.Ing || t.VariantId != key.Variant);
        // The note is assigned BEFORE anything is raised: a retarget raises Changed itself, and a handler
        // reading LastUndoNote then would see the PREVIOUS undo's sentence under the new layer.
        bool changedNote = !string.Equals(note, LastUndoNote, StringComparison.Ordinal);
        LastUndoNote = note;
        RefreshRowDepth();
        if (changedTarget) SetTargetTo(pick!);   // raises Changed, once
        else if (changedNote) RaiseChanged();
    }

    // ---------------------------------------------------------------- save

    /// <inheritdoc />
    public override Task SaveAsync()
    {
        // THE LOCK IS CHECKED HERE, NOT ONLY WHERE THE BUTTON IS ENABLED: a disabled button is a label.
        if (_session is null) return base.SaveAsync();   // read-only: the base says so
        if (IsLocked) return Task.CompletedTask;
        // One writer per destination. A second caller must not be told "saved" while the first is still
        // writing, or about to fail, so it gets the first's task and its outcome.
        if (_writeBusy && SaveInFlight is null) return Task.CompletedTask;
        return SingleFlight(() => { _writeBusy = true; return SaveCoreAsync(); }, after: () => _writeBusy = false);
    }

    private async Task SaveCoreAsync()
    {
        var session = _session!;
        var book = session.Current ?? throw new InvalidOperationException("The CookBook is no longer open.");
        var recipe = book.Recipes.FirstOrDefault(r => r.Manifest.Id == _recipe.Manifest.Id)
            ?? throw new InvalidOperationException($"Recipe '{_recipe.Manifest.Id}' is no longer in the CookBook.");
        var dirty = _edits.Where(kv => kv.Value.IsDirty).ToList();
        if (dirty.Count == 0) return;

        // Every guard runs across ALL touched variants before any write: a partial save would leave the book
        // half-edited and the page unable to say which half.
        var gone = new List<string>();
        var stale = new List<string>();
        var restamped = new List<string>();
        foreach (var ((ingId, variantId), edit) in dirty)
        {
            var live = recipe.Ingredients.FirstOrDefault(i => i.Manifest.Id == ingId);
            if (live is null || !live.Manifest.Variants.Any(v => v.Id == variantId)) gone.Add(edit.VariantName);
            else if (!live.VariantImages.TryGetValue(variantId, out var current) || !PixelCompare.Same(edit.Art, current))
                stale.Add(edit.VariantName);
            // The picture is the same, so the live map reads through the same surface rule the baseline did
            // (depth under clear pixels dropped); a map that differs from the baseline was written by someone
            // else since this edit began, and saving would overwrite it with a result of the OLD map.
            else if (!new DepthSurface(current, live.Depths.GetValueOrDefault(variantId)).SameDepthAs(edit.Baseline))
                restamped.Add(edit.VariantName);
        }
        if (gone.Count > 0)
            throw new InvalidOperationException(
                $"{string.Join(", ", gone)} no longer exists in the recipe; its painted depth was not saved. Reopen Rise on the current layers.");
        if (stale.Count > 0)
            throw new InvalidOperationException(
                $"The picture of {string.Join(", ", stale)} changed after Rise opened it; its depth was painted on the old picture and was not saved. Reopen Rise on the new picture.");

        if (restamped.Count > 0)
            throw new InvalidOperationException(
                $"The depth of {string.Join(", ", restamped)} changed in the CookBook after Rise began painting it; saving would overwrite that change, so nothing was saved. Reopen Rise on the current depth.");

        // The snapshot is kept per variant: what is WRITTEN is what gets marked saved afterwards, so a stroke
        // painted while the write awaits the disk stays dirty.
        var written = new List<(VariantEdit Edit, DepthMap? Map)>();
        var book2 = book;
        foreach (var group in dirty.GroupBy(kv => kv.Key.Ing, StringComparer.Ordinal))
        {
            var live = recipe.Ingredients.First(i => i.Manifest.Id == group.Key);
            var depths = new Dictionary<string, DepthMap>(live.Depths, StringComparer.Ordinal);
            foreach (var kv in group)
            {
                var map = kv.Value.Surface.ToDepthMap();
                if (map is not null) depths[kv.Key.Variant] = map;
                else depths.Remove(kv.Key.Variant);
                written.Add((kv.Value, map));
            }
            var next = new LoadedIngredient { Manifest = live.Manifest, VariantImages = live.VariantImages, Depths = depths };
            var problems = Validator.ValidateIngredient(next);
            if (problems.Count > 0) throw new InvalidOperationException(Problems.Join(problems));
            book2 = CookBookEdits.UpsertIngredient(book2, _recipe.Manifest.Id, next);
        }

        var book3 = await CookBookPersistence.PersistAsync(session, book2);

        // Marked BEFORE anyone is told: the write is done, so a handler that throws must not leave the
        // variants looking unsaved after a good write. From here the source reads the book that was written.
        foreach (var (edit, map) in written) edit.Baseline = map;
        _book = book3;
        _recipe = book3.Recipe(_recipe.Manifest.Id);
        RefreshRowDepth();
        RaiseSaved(book3);
        RaiseChanged();
    }


    /// <inheritdoc />
    public override void Dispose()
    {
        base.Dispose();
        foreach (var r in _layers) r.Dispose();
        _layers = new List<RiseLayerRow>();
        foreach (var p in _picks) p.Dispose();
        _picks = new List<RollPick>();
        _below?.Dispose();
        _targetArt?.Dispose();
        _above?.Dispose();
        _below = _targetArt = _above = null;
        _rise?.Dispose();
        _rise = null;
        foreach (var e in _edits.Values) e.Dispose();
        _edits.Clear();
    }
}
