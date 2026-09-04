using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.App.ViewModels;

/// <summary>
/// One row of the recipe's layer stack.
///
/// <para>A mutable observable object rather than a record, because reordering RENUMBERS every row it
/// passes and the rows themselves are the selection's identity: <see cref="RecipeDetailViewModel"/>
/// re-seats the same instances rather than projecting new ones, so the row the keyboard is moving
/// survives its own move. Building fresh rows would leave <c>SelectedLayer</c> pointing at an object
/// no longer in the table, and the second Alt+Up would have nothing to act on.</para>
/// </summary>
public partial class LayerRow : ObservableObject
{
    /// <summary>Its position in layerOrder, 1-based, as the table numbers it — the layer's depth,
    /// counting from the bottom of the stack (1 paints first, furthest back).</summary>
    [ObservableProperty] private int _index;

    /// <summary>Whether this is the row a keyboard reorder acts on; drives the row's accent wash.</summary>
    [ObservableProperty] private bool _isSelected;

    /// <summary>Builds a row.</summary>
    /// <param name="index">Its 1-based depth.</param>
    /// <param name="id">The ingredient's id.</param>
    /// <param name="layer">Its display name.</param>
    /// <param name="kind">The layer kind, as a lower-case word.</param>
    /// <param name="variantCount">How many variants it offers.</param>
    public LayerRow(int index, string id, string layer, string kind, int variantCount)
    {
        _index = index;
        Id = id;
        Layer = layer;
        Kind = kind;
        VariantCount = variantCount;
    }

    /// <summary>The ingredient's id.</summary>
    public string Id { get; }
    /// <summary>Its display name.</summary>
    public string Layer { get; }
    /// <summary>The layer kind, as a lower-case word.</summary>
    public string Kind { get; }
    /// <summary>How many variants it offers.</summary>
    public int VariantCount { get; }

    /// <summary>Whether this layer rolls its color per asset.</summary>
    public bool IsDynamic => Kind == "Dynamic";
    /// <summary>Whether this layer applies one fixed color.</summary>
    public bool IsStatic => Kind == "Static";
    /// <summary>Whether this layer composites as-is.</summary>
    public bool IsCustom => Kind == "Custom";
}
/// <summary>One side of a rule, named the way the panel reads it.</summary>
/// <param name="Ingredient">The layer's display name.</param>
/// <param name="Variant">The variant's display name.</param>
public record RuleTargetRow(string Ingredient, string Variant);
/// <summary>One incompatibility rule as the Rules panel shows it.</summary>
/// <param name="IsExclude">True for an exclude rule, false for a require — which picks the glyph.</param>
/// <param name="When">The trigger.</param>
/// <param name="Targets">What it forbids or requires.</param>
public record RuleRow(bool IsExclude, RuleTargetRow When, IReadOnlyList<RuleTargetRow> Targets);

public partial class RecipeDetailViewModel : ViewModelBase, IDisposable
{
    private readonly Action<string> _openIngredient;
    private readonly IImageBridge _bridge;

    /// <summary>Reorders one layer and returns the SAVED graph, or null when the move was refused
    /// (editing locked, no file to write to) or failed — in which case the reason has already been
    /// reported. Null for a detail pane with no owner to persist through, which is what leaves
    /// <see cref="CanReorder"/> permanently false in a fixture.</summary>
    private readonly Func<string, int, Task<LoadedCookBook?>>? _moveLayer;

    // Not readonly: a reorder hands back a NEW graph (Core's edits are pure), and this pane re-projects
    // itself onto it rather than being rebuilt — see Rebind.
    private LoadedRecipe _recipe;
    private LoadedCookBook _book;

    [ObservableProperty] private int _rollSeed = 1;
    [ObservableProperty] private Bitmap _hero;

    /// <summary>Whether the layer table offers reordering — the Explorer's edit lock, pushed in.
    /// Observable rather than a <c>Func&lt;bool&gt;</c> (the pattern the ingredient detail uses for
    /// the same lock) because the lock flips while this pane is open and the grips have to ghost and
    /// un-ghost with it, without the pane being rebuilt.</summary>
    [ObservableProperty] private bool _canReorder;

    /// <summary>The row a keyboard reorder moves. Selection is deliberately separate from clicking a
    /// row, which navigates to that ingredient and takes this whole pane with it.</summary>
    [ObservableProperty] private LayerRow? _selectedLayer;

    partial void OnSelectedLayerChanged(LayerRow? oldValue, LayerRow? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) newValue.IsSelected = true;
    }

    /// <summary>The recipe's display name.</summary>
    public string Name { get; }
    /// <summary>Its layer stack, in composite order. Observable and reordered in place.</summary>
    public ObservableCollection<LayerRow> Layers { get; }
    /// <summary>Its incompatibility rules.</summary>
    public IReadOnlyList<RuleRow> Rules { get; }

    /// <summary>The hero's factor arithmetic (mockup .rfactors): one kind-tinted chip per layer,
    /// multiplied together to reach <see cref="TotalText"/>.</summary>
    public IReadOnlyList<FactorChip> Factors { get; private set; }

    /// <summary>Product of the layers' variant counts - the combinations this recipe's art alone can
    /// make, before color. Deliberately NOT UniqueSpace.Count: that folds in each dynamic layer's
    /// quantized color buckets, and this line exists to explain the chips beside it, which are
    /// variant counts. The color-inclusive figure is the cookbook detail's business.</summary>
    public string TotalText { get; }
    /// <summary>"N layers", pluralised.</summary>
    public string LayerCountText { get; }
    /// <summary>"N variants", pluralised.</summary>
    public string VariantCountText { get; }
    /// <summary>"N rules", pluralised.</summary>
    public string RuleCountText { get; }

    /// <summary>Builds the Recipe detail pane.</summary>
    /// <param name="recipe">The recipe to describe.</param>
    /// <param name="book">Its owning book, for the canvas and a sample roll.</param>
    /// <param name="bridge">Converts an ImageSharp frame to an Avalonia bitmap.</param>
    /// <param name="openIngredient">Selects a layer in the tree when a rule row is clicked.</param>
    /// <param name="moveLayer">Moves a layer to a depth and saves, returning the saved graph — null
    /// (the default) for a pane with nothing to persist through, which disables reordering outright.</param>
    /// <param name="canReorder">Whether editing is unlocked right now.</param>
    public RecipeDetailViewModel(LoadedRecipe recipe, LoadedCookBook book, IImageBridge bridge,
        Action<string> openIngredient,
        Func<string, int, Task<LoadedCookBook?>>? moveLayer = null, bool canReorder = false)
    {
        _recipe = recipe; _book = book; _bridge = bridge; _openIngredient = openIngredient;
        _moveLayer = moveLayer;
        _canReorder = canReorder;
        Name = recipe.Manifest.Name;

        // Numbered from LayerDepth, NOT from the filtered position. The two agree on every legal book
        // and diverge on one that names a layer it does not carry — and there the position is a lie: it
        // was fed straight back into LayerDepth.MoveTo as a depth, so a move landed somewhere the user
        // had not pointed at. Taking the real depth makes LayerRow.Index a depth by construction, which
        // is what lets every path below just use it.
        var ingById = recipe.Ingredients.ToDictionary(i => i.Manifest.Id, StringComparer.Ordinal);
        Layers = new ObservableCollection<LayerRow>(
            LayerDepth.Ordered(recipe.Manifest)
                .Where(l => ingById.ContainsKey(l.IngredientId))
                .Select(l => new LayerRow(l.Depth, l.IngredientId, ingById[l.IngredientId].Manifest.Name,
                    ingById[l.IngredientId].Manifest.Kind.ToString(),
                    ingById[l.IngredientId].Manifest.Variants.Count)));

        Rules = recipe.Manifest.Rules.Select(r => MapRule(r, recipe)).ToList();

        var ordered = Ordered();
        Factors = BuildFactors(ordered);
        // long, not int: a dozen 5-variant layers already overflows int.
        long total = ordered.Aggregate(1L, (acc, ing) => acc * Math.Max(1, ing.Manifest.Variants.Count));
        TotalText = total.ToString("N0");
        LayerCountText = Layers.Count == 1 ? "1 layer" : $"{Layers.Count} layers";
        int variants = ordered.Sum(i => i.Manifest.Variants.Count);
        VariantCountText = variants == 1 ? "1 variant" : $"{variants} variants";
        RuleCountText = Rules.Count == 1 ? "1 rule" : $"{Rules.Count} rules";

        _hero = BuildHero();
    }

    /// <summary>This recipe's ingredients in paint order, skipping any the layer order names but the
    /// recipe does not carry (a broken book must still open — see Validator's report-never-throw rule).</summary>
    private List<LoadedIngredient> Ordered()
    {
        var ingById = _recipe.Ingredients.ToDictionary(i => i.Manifest.Id, StringComparer.Ordinal);
        return _recipe.Manifest.LayerOrder.Where(ingById.ContainsKey).Select(id => ingById[id]).ToList();
    }

    private static IReadOnlyList<FactorChip> BuildFactors(IReadOnlyList<LoadedIngredient> ordered) => ordered
        .Select((ing, idx) => new FactorChip(ing.Manifest.Name, ing.Manifest.Variants.Count,
                                             ing.Manifest.Kind, ShowTimes: idx > 0))
        .ToList();

    // ---- reorder --------------------------------------------------------------------------------

    /// <summary>Moves the selected row by <paramref name="rows"/> positions <b>in the table</b>: -1 is
    /// one row up, +1 one row down. Table direction, not stack direction — the table lists depth
    /// ascending down the page, so moving a row UP moves it towards #1, which paints FIRST and sits
    /// furthest back. That inversion is why the table carries a permanent hint saying so.</summary>
    /// <param name="rows">How many rows to move, signed.</param>
    /// <returns>True when the stack actually changed and was saved.</returns>
    public Task<bool> MoveSelectedRowsAsync(int rows) =>
        SelectedLayer is { } row ? MoveToDepthAsync(row, row.Index + rows) : Task.FromResult(false);

    /// <summary>
    /// Drops a dragged row into a <b>slot</b> — a gap between the visible rows, 0 above the first and
    /// <c>Layers.Count</c> below the last — and moves it to whatever depth that lands on.
    ///
    /// <para>The translation lives here rather than in the view because it needs the rows and their
    /// depths, and those are only the same number on a book that carries every layer it stacks. The
    /// row that currently occupies the destination is what names the depth: land where it is, and the
    /// list re-seats around it exactly as <c>LayerDepth.MoveTo</c> defines.</para>
    /// </summary>
    /// <param name="row">The dragged layer.</param>
    /// <param name="slot">The gap it was dropped into.</param>
    /// <returns>True when the stack actually changed and was saved.</returns>
    public Task<bool> MoveToSlotAsync(LayerRow row, int slot)
    {
        ArgumentNullException.ThrowIfNull(row);
        int from = Layers.IndexOf(row);
        if (from < 0 || Layers.Count == 0) return Task.FromResult(false);

        // A slot below the dragged row is one place higher once that row is lifted out of the list.
        int to = Math.Clamp(slot > from ? slot - 1 : slot, 0, Layers.Count - 1);
        return to == from ? Task.FromResult(false) : MoveToDepthAsync(row, Layers[to].Index);
    }

    /// <summary>Moves one layer to a 1-based depth and saves the book.</summary>
    /// <param name="row">The layer to move.</param>
    /// <param name="depth">Where to put it. Clamped to the stack, mirroring <c>LayerDepth.MoveTo</c>,
    /// so nudging the top or bottom layer off the end is a no-op rather than an error — and, because
    /// the clamp is applied before anything is written, not a pointless save either.</param>
    /// <returns>True when the stack actually changed and was saved.</returns>
    public async Task<bool> MoveToDepthAsync(LayerRow row, int depth)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (_moveLayer is null || !CanReorder) return false;
        // Clamped against the STACK, not the visible rows: on a book that stacks a layer it does not
        // carry there are fewer rows than depths, and clamping to the row count would refuse the
        // bottom of the stack outright.
        int target = Math.Clamp(depth, 1, LayerDepth.Count(_recipe.Manifest));
        if (target == row.Index) return false;

        var book = await _moveLayer(row.Id, target);
        if (book is null) return false;   // refused or failed; the owner has already said why
        await RebindAsync(book);
        return true;
    }

    /// <summary>Re-projects this pane onto the saved graph after a reorder: the same rows, re-seated,
    /// so the row the selection points at is still the row in the table.
    ///
    /// <para>The rows and chips are re-seated synchronously — the drag and the keyboard both hold a
    /// selection and a grip focus that must land with the move, not a frame later. Only the hero,
    /// which is a whole generated asset, is awaited.</para></summary>
    private async Task RebindAsync(LoadedCookBook book)
    {
        // A reorder rewrites every PNG in the book, so the save can land seconds later — by which time
        // the user may have navigated away and the Explorer disposed this pane. Re-projecting a dead
        // pane disposed Hero a second time and built a fresh canvas-sized Bitmap nothing would ever
        // free. The reply to a request this pane made is simply no longer wanted.
        if (_disposed) return;

        _book = book;
        _recipe = book.Recipes.FirstOrDefault(r => r.Manifest.Id == _recipe.Manifest.Id) ?? _recipe;

        // Resolved once: Ordered() rebuilds a dictionary over the recipe's ingredients and re-walks
        // layerOrder on every call, and the rows and the factor chips must be projections of the SAME
        // ordered list — which is exactly the invariant the move-don't-rebuild loop below depends on.
        var ordered = Ordered();
        var order = ordered.Select(i => i.Manifest.Id).ToList();
        // Move, never rebuild. A reorder can neither drop nor duplicate an id (LayerDepth re-seats one
        // entry in a list), so every id below is already in Layers and this terminates.
        for (int target = 0; target < order.Count; target++)
        {
            int from = IndexOfRow(order[target]);
            if (from > target) Layers.Move(from, target);
        }
        // The same projection the constructor used, for the same reason: a row's number is its DEPTH,
        // which is only the row's position on a book that carries every layer it stacks.
        foreach (var row in Layers) row.Index = LayerDepth.DepthOf(_recipe.Manifest, row.Id);

        Factors = BuildFactors(ordered);
        OnPropertyChanged(nameof(Factors));

        // The hero genuinely changed: reordering moves which RNG draw reaches which layer, so the same
        // seed over a reordered book paints a different asset, not merely a restacked one.
        await SwapHeroAsync();
    }

    private int IndexOfRow(string id)
    {
        for (int i = 0; i < Layers.Count; i++)
            if (string.Equals(Layers[i].Id, id, StringComparison.Ordinal)) return i;
        return -1;
    }

    /// <summary>The one asset the hero shows: this recipe, at the current reroll seed.</summary>
    private GenerateOptions HeroOptions() => new(Count: 1, Seed: RollSeed.ToString(),
        RecipeId: _recipe.Manifest.Id, EnforceUniqueDna: false);

    /// <summary>
    /// Shown when the book isn't generatable yet — a freshly-added recipe with no layers (whose detail
    /// is selected the moment it is added), or any other recipe being empty, since Generator validates
    /// the whole book. A blank canvas-sized frame rather than a crashed detail view.
    /// </summary>
    private Bitmap BlankHero()
    {
        using var blank = new Image<Rgba32>(_book.Manifest.Canvas.Width, _book.Manifest.Canvas.Height);
        return _bridge.ToBitmap(blank);
    }

    /// <summary>The first hero, built synchronously because a constructor cannot await and the pane
    /// must have something to bind before it is shown. Every LATER hero goes through
    /// <see cref="SwapHeroAsync"/>, which does not block the UI thread.</summary>
    private Bitmap BuildHero()
    {
        try
        {
            using var asset = Generator.GenerateStreaming(_book, HeroOptions()).First();
            return _bridge.ToBitmap(asset.Image);
        }
        catch (Exception) { return BlankHero(); }
    }

    /// <summary>
    /// Replaces the hero with a freshly rolled one, generated <b>off the UI thread</b>.
    ///
    /// <para>Rolling and compositing a whole asset costs about 25 ms at 512x512 and 75 ms at
    /// 1000x1000, and it used to run synchronously — on every reorder keystroke and every Reroll
    /// click. <c>Generator.GenerateAsync</c> exists for precisely this ("it exists to keep a UI thread
    /// free"), and the await resumes on the UI thread, so the bitmap is still built there.</para>
    ///
    /// <para>The old hero stays on screen for the whole roll, so the pane never blinks to a
    /// placeholder and back. If the pane is disposed while the roll is in flight, the new image is
    /// dropped rather than assigned to a dead view.</para>
    /// </summary>
    private async Task SwapHeroAsync()
    {
        Bitmap next;
        try
        {
            using var set = await Generator.GenerateAsync(_book, HeroOptions());
            next = _disposed ? BlankHero() : _bridge.ToBitmap(set.Assets[0].Image);
        }
        catch (Exception) { next = BlankHero(); }

        if (_disposed) { next.Dispose(); return; }
        var old = Hero;
        Hero = next;
        old.Dispose();
    }

    private static RuleRow MapRule(IncompatibilityRule rule, LoadedRecipe recipe) => new(
        rule.Type == RuleType.Exclude,
        Target(rule.When.IngredientId, rule.When.VariantId, recipe),
        rule.Targets.Select(t => Target(t.IngredientId, t.VariantId, recipe)).ToList());

    /// <summary>Resolves a rule's stored IDs to the names the mockup's rule chips show. Rules
    /// reference ids because that is what survives a rename in the archive; a chip that prints the
    /// id is only correct for books whose ids happen to equal their names, which is true of the
    /// hand-authored test fixtures and not of real art. Falls back to the id when the reference
    /// dangles, since a rule pointing at a deleted layer should still be visible rather than blank —
    /// that is exactly the state the user needs to see in order to fix it.</summary>
    private static RuleTargetRow Target(string ingredientId, string variantId, LoadedRecipe recipe)
    {
        var ing = recipe.Ingredients.FirstOrDefault(i => i.Manifest.Id == ingredientId);
        var variant = ing?.Manifest.Variants.FirstOrDefault(v => v.Id == variantId);
        // The ingredient caption is uppercased here: the mockup's .rcl carries text-transform,
        // and Avalonia has none. The variant keeps its own casing - .rcv does not transform.
        return new RuleTargetRow(
            (ing?.Manifest.Name ?? ingredientId).ToUpperInvariant(),
            variant?.Name ?? variantId);
    }

    [RelayCommand]
    private async Task Reroll()
    {
        RollSeed++;
        await SwapHeroAsync();
    }

    [RelayCommand] private void OpenIngredient(string id) => _openIngredient(id);

    /// <summary>Frees the sample-roll bitmap. Idempotent, and it latches: a reorder's save can land
    /// after the Explorer has navigated away and disposed this pane, and the reply must not
    /// resurrect it.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Hero.Dispose();
    }

    /// <summary>Set once this pane has been navigated away from and freed. Checked by
    /// <see cref="RebindAsync"/> before it re-projects, and by <see cref="SwapHeroAsync"/> on BOTH
    /// sides of its await — a hero roll started while the pane was alive can finish after it is
    /// gone.</summary>
    private bool _disposed;
}
