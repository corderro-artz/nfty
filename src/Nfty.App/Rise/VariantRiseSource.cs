using System.Globalization;
using Nfty.App.Services;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.App.Rise;

/// <summary>
/// The variants of one layer, risen as their STORED pixels (a Dynamic or Static variant is its
/// grayscale value-map, exactly what <c>nfty model x.igt --variant v</c> writes). The only source
/// that can save: depth goes back into the CookBook through <see cref="CookBookPersistence"/>, or into
/// the loose <c>.igt</c> the layer came from.
/// </summary>
public sealed class VariantRiseSource : RiseSource
{
    private readonly string _recipeId;
    private readonly string _recipeName;
    private readonly string _ingredientId;
    private readonly string _ingredientName;
    private readonly string _kindWord;
    private readonly string _layerFact;
    private readonly ICookBookSession _session;
    private readonly Func<bool>? _isEditing;
    private readonly string? _looseSavePath;
    private bool _isTwoDOnly;
    private readonly List<RiseItem> _items = new();
    private readonly RiseItem _initial;
    private LoadedIngredient _looseLive;
    private Task? _inFlight;
    private Task<VoxelReadyOutcome?>? _voxelInFlight;
    // ONE write at a time on this layer's destination, whichever kind (depth save, voxel-ready apply,
    // undo): a second is refused, never queued, as every reorder shares one flag. The page gates its
    // buttons the same way, but a disabled button is a label, so the source holds the line itself.
    private bool _writeBusy;
    private VoxelReadyResult? _voxelResult;
    private readonly IImageBridge _bridge;

    /// <summary>Builds the source for one layer of a recipe.</summary>
    /// <param name="recipe">The recipe that holds the layer.</param>
    /// <param name="ingredient">The layer.</param>
    /// <param name="selectedVariantId">The variant that opens on the canvas.</param>
    /// <param name="bridge">Makes the row thumbnails.</param>
    /// <param name="session">The open book, which Save writes through.</param>
    /// <param name="isEditing">Whether the edit lock is open; null means nothing locks this layer.</param>
    /// <param name="looseSavePath">The <c>.igt</c> to save to when the layer is loose; null for a book layer.</param>
    public VariantRiseSource(LoadedRecipe recipe, LoadedIngredient ingredient, string selectedVariantId,
        IImageBridge bridge, ICookBookSession session, Func<bool>? isEditing = null, string? looseSavePath = null)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(ingredient);
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(session);
        _recipeId = recipe.Manifest.Id;
        _recipeName = recipe.Manifest.Name;
        _ingredientId = ingredient.Manifest.Id;
        _ingredientName = ingredient.Manifest.Name;
        _session = session;
        _bridge = bridge;
        _isEditing = isEditing;
        _looseSavePath = looseSavePath;
        _looseLive = ingredient;
        _kindWord = ingredient.Manifest.Kind.ToString().ToLowerInvariant();
        _layerFact = looseSavePath is not null
            ? "loose ingredient"
            : $"layer {LayerDepth.DepthOf(recipe.Manifest, _ingredientId)} of {LayerDepth.Count(recipe.Manifest)} in {_recipeName}";
        _isTwoDOnly = looseSavePath is not null
            ? Opacity.HasPartialAlpha(ingredient)
            : session.Current is { } book && Opacity.HasPartialAlpha(book);

        double total = ingredient.Manifest.Variants.Sum(v => v.Weight);
        RiseItem? initial = null;
        foreach (var v in ingredient.Manifest.Variants)
        {
            var art = ingredient.VariantImages[v.Id].Clone();
            var thumb = bridge.ToBitmap(art);
            double pct = total > 0 ? Math.Round(v.Weight / total * 100, MidpointRounding.AwayFromZero) : 0;
            var caption = string.Create(CultureInfo.InvariantCulture, $"weight {v.Weight:0.##} · {pct:0}%");
            var item = new RiseItem(v.Id, v.Name, caption, $"{_ingredientName}-{v.Name}", thumb, ownsThumbnail: true);
            item.Attach(new RiseSubject(art, ingredient.Depths.GetValueOrDefault(v.Id), ownsArt: true));
            _items.Add(item);
            if (v.Id == selectedVariantId) initial = item;
        }
        _initial = initial ?? throw new ArgumentException(
            $"Ingredient '{_ingredientId}' has no variant '{selectedVariantId}'.", nameof(selectedVariantId));
        _initial.IsSelected = true;
    }

    /// <summary>Raised after a book save, with the book the session now holds.</summary>
    public event Action<LoadedCookBook>? Saved;

    /// <summary>Raised after a loose <c>.igt</c> save, with the ingredient now on disk. A loose save has no
    /// book to hand out, so this is how the editor that launched the page learns the new depth.</summary>
    public event Action<LoadedIngredient>? LooseSaved;

    /// <inheritdoc />
    public override RiseSourceKind Kind => RiseSourceKind.Variant;

    /// <inheritdoc />
    public override string Kicker => $"{_ingredientName.ToUpperInvariant()} · VARIANTS";

    /// <inheritdoc />
    public override string Title => $"{_ingredientName} / {_initial.Name}";

    /// <inheritdoc />
    public override IReadOnlyList<RiseItem> Items => _items;

    /// <inheritdoc />
    public override RiseItem InitialItem => _initial;

    /// <inheritdoc />
    public override IReadOnlyList<string> Facts => new[] { _kindWord, _layerFact };

    /// <inheritdoc />
    public override bool IsTwoDOnly => _isTwoDOnly;

    /// <inheritdoc />
    public override string SaveTargetName => _looseSavePath is { } p ? Path.GetFileName(p) : "CookBook";

    /// <inheritdoc />
    public override bool IsEditable => true;

    /// <inheritdoc />
    public override bool IsLocked => _isEditing is not null && !_isEditing();

    /// <inheritdoc />
    public override bool CanBatch => true;

    /// <inheritdoc />
    public override string BatchStem => $"{_ingredientName}-models";

    /// <inheritdoc />
    public override string DepthNote => IsLocked
        ? "The CookBook is read-only. Try depth freely here — Save needs the lock opened in the Explorer."
        : "Thickness is in voxels: 1 + front + back. A flat sprite is paper, one voxel thick.";

    /// <inheritdoc />
    public override string OpeningLine => IsLocked
        ? $"“{_ingredientName}” is read-only — try depth freely, but unlock the CookBook to save it."
        : $"Rising “{Title}” — left raises the front, right the back, {KeyHints.LowerDepth} lowers. Save depth writes it into the CookBook.";

    /// <inheritdoc />
    public override RiseSubject Open(RiseItem item) =>
        item.Subject ?? throw new InvalidOperationException($"Variant '{item.Id}' was not loaded.");

    /// <summary>Writes every changed variant's depth, and only those, into the layer's own archive.</summary>
    /// <returns>A task that completes when the archive is written and the subjects are marked saved. A
    /// second call while one is running returns that same task.</returns>
    /// <exception cref="InvalidOperationException">The layer no longer validates, or the book is gone.</exception>
    public override Task SaveAsync()
    {
        // THE LOCK IS CHECKED HERE, NOT ONLY WHERE THE BUTTON IS ENABLED: a disabled button is a label.
        if (IsLocked) return Task.CompletedTask;
        // One writer per destination. A second caller must not be told "saved" while the first is still
        // writing, or about to fail, so it gets the first's task and its outcome.
        if (_inFlight is { } running) return running;
        // A voxel-ready write is running on the same destination: refused, not queued.
        if (_writeBusy) return Task.CompletedTask;
        // Published BEFORE the work starts: a save that runs partly synchronously (or re-enters from a
        // handler) must already be visible as in flight.
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _writeBusy = true;
        _inFlight = done.Task;
        _ = RunSaveAsync(done);
        return done.Task;
    }

    private async Task RunSaveAsync(TaskCompletionSource done)
    {
        Exception? failure = null;
        try { await SaveCoreAsync(); }
        catch (Exception ex) { failure = ex; }
        _inFlight = null;
        _writeBusy = false;
        if (failure is null) done.SetResult(); else done.SetException(failure);
    }

    private async Task SaveCoreAsync()
    {
        bool loose = _looseSavePath is not null;
        var live = loose ? _looseLive : LiveIngredient();

        // Start from the live ingredient's depths so every variant we did not change keeps exactly
        // the bytes it had; replace only the maps of variants whose painting differs from stored.
        var depths = new Dictionary<string, DepthMap>(live.Depths, StringComparer.Ordinal);
        var ids = live.Manifest.Variants.Select(v => v.Id).ToHashSet(StringComparer.Ordinal);
        // The snapshot is kept per subject: what is WRITTEN is what gets marked saved afterwards, so a
        // stroke painted while the write awaits the disk stays dirty.
        var written = new List<(RiseSubject Subject, DepthMap? Map)>();
        foreach (var item in _items)
        {
            if (item.Subject is not { IsDirty: true } s || !ids.Contains(item.Id)) continue;
            var map = s.Depth.ToDepthMap();
            if (map is not null) depths[item.Id] = map;
            else depths.Remove(item.Id);
            written.Add((s, map));
        }

        // STALE-ART GUARD. Depth is painted on the picture Rise opened; if the layer's pixels have since
        // changed (another route rewrote the variant), the depth no longer describes them. Writing it
        // would pair old-picture depth with new-picture art, so the WHOLE save is refused, nothing written.
        // A variant deleted since Rise opened it, with depth still painted, is refused too: dropping its
        // depth silently would report a save that lost work.
        var gone = _items
            .Where(i => i.Subject is { IsDirty: true } && !ids.Contains(i.Id))
            .Select(i => i.Name)
            .ToList();
        if (gone.Count > 0)
            throw new InvalidOperationException(
                $"{string.Join(", ", gone)} no longer exists in the layer; its painted depth was not saved. Reopen Rise on the current layer.");
        var stale = _items
            .Where(i => i.Subject is { IsDirty: true } && ids.Contains(i.Id))
            .Where(i => !live.VariantImages.TryGetValue(i.Id, out var current) || !SamePixels(i.Subject!.Art, current))
            .Select(i => i.Name)
            .ToList();
        if (stale.Count > 0)
            throw new InvalidOperationException(
                $"The picture of {string.Join(", ", stale)} changed after Rise opened it; its depth was painted on the old picture and was not saved. Reopen Rise on the new picture.");

        LoadedCookBook? persisted = null;
        LoadedIngredient? looseNext = null;
        var next = new LoadedIngredient { Manifest = live.Manifest, VariantImages = live.VariantImages, Depths = depths };
        var problems = Validator.ValidateIngredient(next);
        if (problems.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, problems));

        if (loose)
        {
            await WriteLooseAsync(next);
            _looseLive = next;   // images shared with the previous object; nothing disposed
            looseNext = next;
        }
        else
        {
            var book2 = CookBookEdits.UpsertIngredient(_session.Current!, _recipeId, next);
            var book3 = await CookBookPersistence.PersistAsync(_session, book2);
            persisted = book3;
        }

        // Marked BEFORE anyone is told: the write is done, so a handler that throws must not leave
        // the subjects looking unsaved after a good write.
        foreach (var (subject, map) in written) subject.MarkSaved(map);
        foreach (var item in _items) item.RefreshDepth();
        if (persisted is not null) Saved?.Invoke(persisted);
        if (looseNext is not null) LooseSaved?.Invoke(looseNext);
        RaiseChanged();
    }

    private async Task WriteLooseAsync(LoadedIngredient next)
    {
        var path = _looseSavePath!;
        var tmp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await IngredientArchive.WriteAsync(tmp, next.Manifest, next.VariantImages, next.Depths);
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp)) { try { File.Delete(tmp); } catch { /* best effort */ } }
        }
    }

    // ---------------------------------------------------------------- voxel-ready

    // The graph voxel-ready works over: the open book, or for a loose layer the one-recipe wrapper the
    // editor itself uses, so Core's book-level command serves both.
    private LoadedCookBook? ScopeBook() =>
        _looseSavePath is not null ? LooseWorkspace.WrapIngredient(_looseLive) : _session.Current;

    private bool Ours(VoxelReadyChange c) =>
        c.IngredientId == _ingredientId && (_looseSavePath is not null || c.RecipeId == _recipeId);

    /// <inheritdoc />
    public override bool CanMakeVoxelReady => _isTwoDOnly;

    /// <inheritdoc />
    public override bool CanUndoVoxelReady => _voxelResult is not null;

    /// <inheritdoc />
    public override string? VoxelReadyNote => _voxelResult is { } r
        ? string.Create(CultureInfo.InvariantCulture,
            $"Voxel-ready: {r.Changes.Count} {(r.Changes.Count == 1 ? "variant" : "variants")} snapped")
        : null;

    /// <inheritdoc />
    public override IReadOnlyList<VoxelReadyChange> PlanVoxelReady() =>
        ScopeBook() is { } book ? VoxelReady.Plan(book) : Array.Empty<VoxelReadyChange>();

    /// <inheritdoc />
    public override (Image<Rgba32> Before, Image<Rgba32> After) PreviewVoxelReady(VoxelReadyChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var book = ScopeBook() ?? throw new InvalidOperationException("The CookBook is no longer open.");
        var image = book.Recipes.First(r => r.Manifest.Id == change.RecipeId)
            .Ingredients.First(i => i.Manifest.Id == change.IngredientId).VariantImages[change.VariantId];
        return (image.Clone(), VoxelReady.Snap(image));
    }

    /// <inheritdoc />
    public override Task<VoxelReadyOutcome?> MakeVoxelReadyAsync()
    {
        // THE LOCK IS CHECKED HERE, NOT ONLY WHERE THE BUTTON IS ENABLED: a disabled button is a label.
        // Unsaved depth is refused too: reseeding the items would throw it away unasked.
        if (IsLocked || IsDirty) return Task.FromResult<VoxelReadyOutcome?>(null);
        if (_voxelInFlight is { } running) return running;
        if (_writeBusy) return Task.FromResult<VoxelReadyOutcome?>(null);   // a write is running: refused
        _writeBusy = true;
        var run = ApplyVoxelReadyAsync();
        // A run that finished without ever awaiting has already cleared its flags; keeping it would stick.
        if (!run.IsCompleted) _voxelInFlight = run;
        return run;
    }

    private async Task<VoxelReadyOutcome?> ApplyVoxelReadyAsync()
    {
        try
        {
            var book = ScopeBook() ?? throw new InvalidOperationException("The CookBook is no longer open.");
            var result = VoxelReady.Apply(book);
            if (result.Changes.Count == 0) { result.Dispose(); return null; }

            LoadedCookBook? persisted = null;
            LoadedIngredient ours;
            try
            {
                ours = await WriteGraphAsync(result.Book, v => persisted = v);
            }
            catch
            {
                result.Dispose();
                throw;
            }

            _voxelResult?.Dispose();
            _voxelResult = result;
            AfterVoxelWrite(result.Changes, ours, persisted);
            return new VoxelReadyOutcome(result.Changes.Count, VoxelReadyNote!);
        }
        finally { _voxelInFlight = null; _writeBusy = false; }
    }

    /// <inheritdoc />
    public override async Task UndoVoxelReadyAsync()
    {
        if (IsLocked || IsDirty || _writeBusy || _voxelResult is not { } applied) return;
        _writeBusy = true;
        try
        {
            var book = ScopeBook() ?? throw new InvalidOperationException("The CookBook is no longer open.");
            // Refuses (InvalidOperationException naming the variants) when any snapped variant was edited since.
            var restored = VoxelReady.Undo(book, applied);
            LoadedCookBook? persisted = null;
            var ours = await WriteGraphAsync(restored, v => persisted = v);
            _voxelResult = null;
            AfterVoxelWrite(applied.Changes, ours, persisted);
            applied.Dispose();
        }
        finally { _writeBusy = false; }
    }

    // Writes the graph where this layer lives and returns this layer's ingredient as written.
    private async Task<LoadedIngredient> WriteGraphAsync(LoadedCookBook graph, Action<LoadedCookBook> persistedBook)
    {
        if (_looseSavePath is not null)
        {
            var next = graph.Recipes[0].Ingredients[0];
            var problems = Validator.ValidateIngredient(next);
            if (problems.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, problems));
            await WriteLooseAsync(next);
            _looseLive = next;
            return next;
        }
        var persisted = await CookBookPersistence.PersistAsync(_session, graph);
        persistedBook(persisted);
        return persisted.Recipes.First(r => r.Manifest.Id == _recipeId)
            .Ingredients.First(i => i.Manifest.Id == _ingredientId);
    }

    private void AfterVoxelWrite(IReadOnlyList<VoxelReadyChange> changes, LoadedIngredient ours,
        LoadedCookBook? persisted)
    {
        _isTwoDOnly = _looseSavePath is not null
            ? Opacity.HasPartialAlpha(ours)
            : persisted is not null && Opacity.HasPartialAlpha(persisted);

        // Every affected row of this layer takes the written pixels and its stored depth. The old
        // pictures and thumbnails go only AFTER the page has been told, as a reroll does: until then its
        // bindings still read them.
        var oldSubjects = new List<RiseSubject>();
        var oldThumbs = new List<Avalonia.Media.Imaging.Bitmap>();
        foreach (var c in changes.Where(Ours))
        {
            var item = _items.FirstOrDefault(i => i.Id == c.VariantId);
            if (item is null || !ours.VariantImages.TryGetValue(c.VariantId, out var written)) continue;
            var art = written.Clone();
            if (item.Thumbnail is { } oldThumb) oldThumbs.Add(oldThumb);
            item.Thumbnail = _bridge.ToBitmap(art);
            var old = item.Replace(new RiseSubject(art, ours.Depths.GetValueOrDefault(c.VariantId), ownsArt: true));
            if (old is not null) oldSubjects.Add(old);
        }
        RaiseChanged();
        if (persisted is not null) Saved?.Invoke(persisted);
        if (_looseSavePath is not null) LooseSaved?.Invoke(ours);
        foreach (var s in oldSubjects) s.Dispose();
        foreach (var t in oldThumbs) t.Dispose();
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        base.Dispose();
        _voxelResult?.Dispose();
        _voxelResult = null;
    }

    private static bool SamePixels(Image<Rgba32> a, Image<Rgba32> b)
    {
        if (a.Width != b.Width || a.Height != b.Height) return false;
        bool same = true;
        a.ProcessPixelRows(b, (ra, rb) =>
        {
            for (int y = 0; y < ra.Height && same; y++)
                if (!ra.GetRowSpan(y).SequenceEqual(rb.GetRowSpan(y))) same = false;
        });
        return same;
    }

    private LoadedIngredient LiveIngredient()
    {
        var book = _session.Current ?? throw new InvalidOperationException("The CookBook is no longer open.");
        var recipe = book.Recipes.FirstOrDefault(r => r.Manifest.Id == _recipeId)
            ?? throw new InvalidOperationException($"Recipe '{_recipeId}' is no longer in the CookBook.");
        return recipe.Ingredients.FirstOrDefault(i => i.Manifest.Id == _ingredientId)
            ?? throw new InvalidOperationException($"Layer '{_ingredientId}' is no longer in recipe '{_recipeId}'.");
    }
}
