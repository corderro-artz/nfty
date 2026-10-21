using System.Globalization;
using Nfty.App.Services;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Imaging;
using Nfty.Core.Model;

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
    private readonly bool _bookIsTwoDOnly;
    private readonly List<RiseItem> _items = new();
    private readonly RiseItem _initial;
    private LoadedIngredient _looseLive;
    private bool _saving;

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
        _isEditing = isEditing;
        _looseSavePath = looseSavePath;
        _looseLive = ingredient;
        _kindWord = ingredient.Manifest.Kind.ToString().ToLowerInvariant();
        _layerFact = looseSavePath is not null
            ? "loose ingredient"
            : $"layer {LayerDepth.DepthOf(recipe.Manifest, _ingredientId)} of {LayerDepth.Count(recipe.Manifest)} in {_recipeName}";
        _bookIsTwoDOnly = looseSavePath is not null
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
    public override bool BookIsTwoDOnly => _bookIsTwoDOnly;

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
        : $"Rising “{Title}” — left raises the front, right the back, Shift lowers. Save depth writes it into the CookBook.";

    /// <inheritdoc />
    public override RiseSubject Open(RiseItem item) =>
        item.Subject ?? throw new InvalidOperationException($"Variant '{item.Id}' was not loaded.");

    /// <summary>Writes every changed variant's depth, and only those, into the layer's own archive.</summary>
    /// <returns>A task that completes when the archive is written and the subjects are marked saved.</returns>
    /// <exception cref="InvalidOperationException">The layer no longer validates, or the book is gone.</exception>
    public override async Task SaveAsync()
    {
        // THE LOCK IS CHECKED HERE, NOT ONLY WHERE THE BUTTON IS ENABLED: a disabled button is a label.
        if (IsLocked) return;
        if (_saving) return;   // two writers to one destination collide; the caller's second press waits for the first
        _saving = true;
        try
        {
            bool loose = _looseSavePath is not null;
            var live = loose ? _looseLive : LiveIngredient();

            // Start from the live ingredient's depths so every variant we did not change keeps exactly
            // the bytes it had; replace only the maps of variants whose painting differs from stored.
            var depths = new Dictionary<string, DepthMap>(live.Depths, StringComparer.Ordinal);
            var ids = live.Manifest.Variants.Select(v => v.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var item in _items)
            {
                if (item.Subject is not { IsDirty: true } s || !ids.Contains(item.Id)) continue;
                if (s.Depth.ToDepthMap() is { } map) depths[item.Id] = map;
                else depths.Remove(item.Id);
            }

            var next = new LoadedIngredient { Manifest = live.Manifest, VariantImages = live.VariantImages, Depths = depths };
            var problems = Validator.ValidateIngredient(next);
            if (problems.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, problems));

            if (loose)
            {
                var path = _looseSavePath!;
                var tmp = $"{path}.{Guid.NewGuid():N}.tmp";
                try
                {
                    await IngredientArchive.WriteAsync(tmp, next.Manifest, next.VariantImages, depths);
                    File.Move(tmp, path, overwrite: true);
                }
                finally
                {
                    if (File.Exists(tmp)) { try { File.Delete(tmp); } catch { /* best effort */ } }
                }
                _looseLive = next;   // images shared with the previous object; nothing disposed
            }
            else
            {
                var book2 = CookBookEdits.UpsertIngredient(_session.Current!, _recipeId, next);
                var book3 = await CookBookPersistence.PersistAsync(_session, book2);
                Saved?.Invoke(book3);
            }

            foreach (var item in _items)
            {
                item.Subject?.MarkSaved();
                item.RefreshDepth();
            }
            RaiseChanged();
        }
        finally { _saving = false; }
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
