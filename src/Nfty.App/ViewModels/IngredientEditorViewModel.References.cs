using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Models;
using Nfty.App.Services;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.App.ViewModels;

/// <summary>
/// One switchable row of the Ingredient Editor's reference-layer panel: either a <b>sibling</b> in the
/// recipe being authored (which has a real depth) or a loose <c>.igt</c> <b>from the Kitchen</b> (which
/// has none, and is placed into a gap for this session alone).
///
/// <para>Every field that can appear or disappear is a value rather than a visibility flag — the
/// panel's hard rule is that a row is the same height active and inactive, so the markup binds
/// <see cref="TagOpacity"/>/<see cref="DetailOpacity"/> and never <c>IsVisible</c>. Anything that can
/// show already owns its box while absent.</para>
/// </summary>
public partial class ReferenceLayer : ObservableObject
{
    /// <summary>What identifies this row: a sibling's ingredient id, or a Kitchen file's full path.
    /// Paths and ids never collide, so one dictionary key serves both sections.</summary>
    public string Key { get; }

    /// <summary>True for a loose <c>.igt</c> discovered in the Kitchen — scratch, not part of the
    /// composition. The panel separates the two sections so this is never a guess.</summary>
    public bool IsKitchen { get; }

    /// <summary>The 1-based depth this layer paints at, or <c>—</c> for a Kitchen file, which has no
    /// canonical depth in this recipe.</summary>
    public string DepthText { get; }

    /// <summary>The row's tooltip — what the abbreviated gutter and count cells stand for.</summary>
    public string Tip { get; init; } = "";

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _detailText;

    /// <summary>
    /// The layer's kind, or null for a Kitchen file that has not been opened yet — the archive is only
    /// read when the reference is switched on, so its kind is genuinely unknown until then (the gutter
    /// cell is reserved regardless).
    ///
    /// <para>One stored value with four projections off it, the same shape <c>ExplorerNode</c> already
    /// uses. Storing the mark and the three predicates as four separate fields meant four things to
    /// keep in agreement about one fact.</para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KindMark))]
    [NotifyPropertyChangedFor(nameof(IsDynamic))]
    [NotifyPropertyChangedFor(nameof(IsStatic))]
    [NotifyPropertyChangedFor(nameof(IsCustom))]
    private LayerKind? _kind;

    /// <summary><c>D</c>/<c>S</c>/<c>C</c>, the tree's own kind mark; empty until the kind is known.</summary>
    public string KindMark => LayerKindMark.For(Kind) ?? "";
    /// <summary>Whether this layer rolls its color per asset.</summary>
    public bool IsDynamic => Kind == LayerKind.Dynamic;
    /// <summary>Whether this layer applies one fixed color.</summary>
    public bool IsStatic => Kind == LayerKind.Static;
    /// <summary>Whether this layer composites as-is.</summary>
    public bool IsCustom => Kind == LayerKind.Custom;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TagOpacity))]
    [NotifyPropertyChangedFor(nameof(DetailOpacity))]
    private bool _isOn;

    /// <summary>Whether this layer composites <b>over</b> the one being edited. Not derivable from the
    /// row's position on screen — rows 1 and 2 sit above row 3 in the list while painting beneath it —
    /// which is exactly why each row carries the word.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TagText))]
    private bool _isAbove;

    /// <summary>Which gap a Kitchen file sits in: <c>g</c> means "directly over depth <c>g</c>", and
    /// <c>0</c> means under everything. A GAP, not a depth — placing one can therefore never renumber a
    /// sibling, and nothing is written to the <c>.igt</c>.</summary>
    [ObservableProperty] private int _gap;
    [ObservableProperty] private string _placementText = "";
    [ObservableProperty] private bool _canStepUp;
    [ObservableProperty] private bool _canStepDown;

    /// <summary><c>OVER</c> or <c>UNDER</c>, the tag an active row shows. Uppercased here because the
    /// mockup's tag carries <c>text-transform</c> and Avalonia has none — the same treatment the rules
    /// panel's ingredient captions already get.</summary>
    public string TagText => IsAbove ? "OVER" : "UNDER";

    /// <summary>Ink for the over/under tag and (on a Kitchen row) the placement stepper. Opacity, never
    /// IsVisible: both already occupy their box while switched off, so switching one on reflows
    /// nothing.</summary>
    public double TagOpacity => IsOn ? 1 : 0;

    /// <summary>Ink for the at-rest detail (a sibling's variant count, a Kitchen file's file name),
    /// which shares its box with the tag/stepper above.</summary>
    public double DetailOpacity => IsOn ? 0 : 1;

    /// <summary>Builds a row.</summary>
    /// <param name="key">The sibling's ingredient id, or the Kitchen file's path.</param>
    /// <param name="name">What the row is called.</param>
    /// <param name="detailText">The at-rest caption: a variant count, or a file name.</param>
    /// <param name="depthText">The gutter numeral, or <c>—</c>.</param>
    /// <param name="isKitchen">True for a loose <c>.igt</c>.</param>
    public ReferenceLayer(string key, string name, string detailText, string depthText, bool isKitchen)
    {
        Key = key;
        _name = name;
        _detailText = detailText;
        DepthText = depthText;
        IsKitchen = isKitchen;
    }

    /// <summary>Fills in the kind once the layer's manifest is known — immediately for a sibling, and
    /// only on first switch-on for a Kitchen file, whose archive is not read until then.</summary>
    /// <param name="kind">The layer kind.</param>
    public void SetKind(LayerKind kind) => Kind = kind;
}

public partial class IngredientEditorViewModel
{
    /// <summary>How much ink an above-layer keeps when ghosted.
    ///
    /// <para>Not polish. At full strength a reference above the edited layer can hide the art being
    /// painted outright — the exploration's own third state draws a pair of sunglasses completely
    /// covering the eyes being drawn — so the above-stack is dimmed by default and the true composite is
    /// one toggle away. The below-stack is never dimmed: it is what the art sits ON, and dimming it
    /// would misreport the very alignment this feature exists to show.</para></summary>
    private const double GhostAlpha = 0.35;

    private readonly IKitchenSession? _kitchen;

    // Kitchen graphs opened for this session, keyed by path. Reading an .igt eagerly decodes every PNG
    // in it, so these are held until Dispose rather than re-read per repaint — and they are OURS to
    // free, exactly like _importedCustom.
    private readonly Dictionary<string, LoadedIngredient> _kitchenOpen = new(StringComparer.Ordinal);

    // The two pre-composited stacks. RebuildSurfaces() runs on every brush stroke and every slider
    // tick, so compositing N canvas-sized layers there would stutter at 1000x1000: these are built once
    // per reference-SELECTION change and a stroke then costs two DrawImage calls no matter how many
    // references are on. Null means "nothing on that side", which is the default state.
    private Image<Rgba32>? _belowStack;
    private Image<Rgba32>? _aboveStack;

    // Tracked separately because the two sides do not always go stale together: a selection change
    // moves both, but the ghost toggle can only affect what paints OVER the art.
    private bool _belowDirty = true;
    private bool _aboveDirty = true;

    /// <summary>How many times the two cached stacks have actually been rebuilt. The whole point of the
    /// cache is that this does NOT move on a brush stroke, so the claim is asserted rather than
    /// assumed.</summary>
    internal int StackRebuildCount { get; private set; }

    /// <summary>Depth of the layer being edited, or null when this recipe does not stack it — a
    /// malformed book, where there is no position to split the stack around.</summary>
    private int? _pinDepth;

    /// <summary>The recipe's ingredients by id, built once in <c>BuildReferences</c>. Ordinal, like
    /// every id comparison that can reach an output file.</summary>
    private readonly Dictionary<string, LoadedIngredient> _ingById = new(StringComparer.Ordinal);

    /// <summary>Layers of the recipe being authored, minus the one being edited. Empty on the loose
    /// (<c>.igt</c>) path, where the editor wraps the ingredient in a synthetic single-layer book.</summary>
    public ObservableCollection<ReferenceLayer> Siblings { get; } = new();

    /// <summary>Loose <c>.igt</c> files sitting in the open Kitchen. Scratch: placed in the preview
    /// session only, and never written back.</summary>
    public ObservableCollection<ReferenceLayer> KitchenLayers { get; } = new();

    /// <summary>Whether the stack can be split at all — false only for a recipe that does not stack the
    /// ingredient being edited.</summary>
    public bool ReferencesAvailable => _pinDepth is not null;

    /// <summary>Ghost the layers above the edited one (the default) or show the true full-opacity
    /// composite. Changing it re-bakes the cached above-stack, which is why it invalidates.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsTrueComposite))]
    private bool _ghostAbove = true;

    /// <summary>Backs the "Full" half of the ghost toggle.</summary>
    public bool ShowsTrueComposite => !GhostAbove;

    /// <summary>Why the reference stack could not be composited, or null. A repaint must never throw —
    /// the panel says what is wrong instead, the way the Validator reports rather than throws.</summary>
    [ObservableProperty] private string? _stackProblem;

    /// <summary>The edited layer's own name, for the pinned row.</summary>
    public string PinnedName => _ing.Manifest.Name;
    /// <summary>Its depth, which it is pinned at and cannot be removed from.</summary>
    public string PinnedDepthText => _pinDepth?.ToString() ?? "—";
    /// <summary>Its kind mark.</summary>
    public string PinnedKindMark => LayerKindMark.For(_ing.Manifest.Kind);
    /// <summary>Whether the edited layer rolls its color per asset.</summary>
    public bool IsPinnedDynamic => _ing.Manifest.Kind == LayerKind.Dynamic;
    /// <summary>Whether the edited layer applies one fixed color.</summary>
    public bool IsPinnedStatic => _ing.Manifest.Kind == LayerKind.Static;
    /// <summary>Whether the edited layer composites as-is.</summary>
    public bool IsPinnedCustom => _ing.Manifest.Kind == LayerKind.Custom;

    /// <summary>Which half of the rail is showing.</summary>
    public enum RailTab
    {
        /// <summary>Kind, ranges, quantize — everything that decides the layer's color.</summary>
        Colorize,

        /// <summary>The reference stack: what this art is being lined up against.</summary>
        References,
    }

    /// <summary>
    /// The rail is two panels behind one tab bar, not one long scroll.
    /// </summary>
    /// <remarks>
    /// Together they overflowed a 720px rail, and the half that lost was the references — the
    /// sibling list and the Kitchen section sat below the fold with nothing on screen to say they
    /// were there. They are also two different jobs: one sets what the layer looks like, the other
    /// sets what you are looking at it against, and neither is consulted while doing the other.
    /// </remarks>
    [ObservableProperty] private RailTab _activeRailTab = RailTab.Colorize;

    /// <summary>Whether the colorize half is showing.</summary>
    public bool IsColorizeTab => ActiveRailTab == RailTab.Colorize;
    /// <summary>Whether the reference half is showing.</summary>
    public bool IsReferencesTab => ActiveRailTab == RailTab.References;

    partial void OnActiveRailTabChanged(RailTab value)
    {
        OnPropertyChanged(nameof(IsColorizeTab));
        OnPropertyChanged(nameof(IsReferencesTab));
    }

    /// <summary>Shows the colorize half.</summary>
    [RelayCommand] private void ShowColorizeTab() => ActiveRailTab = RailTab.Colorize;
    /// <summary>Shows the reference half.</summary>
    [RelayCommand] private void ShowReferencesTab() => ActiveRailTab = RailTab.References;

    /// <summary>"N/M" — the same count as <see cref="ReferenceCountText"/>, short enough for the
    /// tab's badge. The tab has to carry it: the count is the one thing about the other half worth
    /// knowing without going there.</summary>
    public string ReferenceBadgeText
    {
        get
        {
            int on = Siblings.Count(r => r.IsOn) + KitchenLayers.Count(r => r.IsOn);
            return $"{on}/{Siblings.Count + KitchenLayers.Count}";
        }
    }

    /// <summary>"N / M on" — the panel header's count.</summary>
    public string ReferenceCountText
    {
        get
        {
            int on = Siblings.Count(r => r.IsOn) + KitchenLayers.Count(r => r.IsOn);
            return $"{on} / {Siblings.Count + KitchenLayers.Count} on";
        }
    }

    /// <summary>Whether this recipe has any sibling to line up against.</summary>
    public bool HasSiblings => Siblings.Count > 0;

    /// <summary>What the "In this recipe" section says when it is empty. A real state, not an error:
    /// a loose <c>.igt</c> belongs to no recipe at all, so it has no siblings by construction.</summary>
    public string NoSiblingsText => IsLooseEditor
        ? "A loose .igt belongs to no recipe, so it has no siblings — switch on a Kitchen layer below to line this art up against something."
        : "This recipe stacks no other layer.";

    /// <summary>Whether any loose <c>.igt</c> was found in the Kitchen.</summary>
    public bool HasKitchenLayers => KitchenLayers.Count > 0;

    /// <summary>What the "From the Kitchen" section says when it is empty — which of "no Kitchen is
    /// open" and "this Kitchen holds none" it is, since they need different things doing about them.</summary>
    public string NoKitchenText => _kitchen?.Current is null
        ? "No Kitchen is open. Open one to reach loose .igt files."
        : "This Kitchen holds no loose ingredients.";

    /// <summary>The open workspace's name, for the section's Kitchen chip.</summary>
    public string KitchenName => _kitchen?.Current?.Manifest.Name ?? "";

    /// <summary>Whether there is a Kitchen to name.</summary>
    public bool HasKitchen => _kitchen?.Current is not null;

    /// <summary>Whether this editor is saving straight back to a standalone <c>.igt</c>.</summary>
    private bool IsLooseEditor => _looseSavePath is not null;

    // ---- building the panel ----------------------------------------------------------------------

    private void BuildReferences()
    {
        var recipe = _recipe.Manifest;
        try { _pinDepth = LayerDepth.DepthOf(recipe, _ing.Manifest.Id); }
        catch (KeyNotFoundException) { _pinDepth = null; }
        if (_pinDepth is null) return;

        // Kept, not discarded. Every other lookup in this file went through its own linear scan of
        // _recipe.Ingredients — three spellings of one question, two of them re-scanning per layer
        // inside the stack rebuild.
        foreach (var i in _recipe.Ingredients) _ingById[i.Manifest.Id] = i;

        foreach (var (depth, id) in LayerDepth.Ordered(recipe))
        {
            if (string.Equals(id, _ing.Manifest.Id, StringComparison.Ordinal)) continue;
            if (!_ingById.TryGetValue(id, out var ing)) continue;   // a broken book must still open
            int n = ing.Manifest.Variants.Count;
            // The bare count, as variant B draws it — the tag it shares a box with is narrow, and a
            // spelled-out "4 variants" would set the width of a cell that must never change.
            var row = new ReferenceLayer(id, ing.Manifest.Name, n.ToString(),
                depth.ToString(), isKitchen: false)
            { Tip = n == 1 ? "1 variant" : $"{n} variants", IsAbove = depth > _pinDepth.Value };
            row.SetKind(ing.Manifest.Kind);
            Siblings.Add(row);
        }

        // Kitchen files are listed by PATH and read only when switched on. Kitchen.Open deliberately
        // hands back paths rather than loaded graphs, because reading an archive decodes every PNG in
        // it — materialising a whole workspace just to draw a list is the opposite of what a listing
        // is for. So the row starts with the file's own name and no kind mark, and adopts the
        // manifest's when the archive is actually opened.
        //
        // The file being edited is excluded when it is itself sitting in the open Kitchen, which is
        // exactly where the loose editor saves to. Left in, it would offer to composite the layer
        // against a stale copy of ITSELF - a reference that silently stops matching the canvas the
        // moment the first stroke lands.
        string? self = _looseSavePath is null ? null : Path.GetFullPath(_looseSavePath);

        foreach (var path in _kitchen?.Current?.Ingredients ?? Array.Empty<string>())
        {
            // Paths, unlike ids, are a filesystem concept rather than something that reaches an output
            // file, so this one comparison is deliberately case-insensitive: the ordinal rule exists to
            // keep two distinct IDS from colliding under a locale, and "AURA.IGT" and "aura.igt" in the
            // same folder are the same file on the platforms this ships on.
            if (self is not null &&
                string.Equals(Path.GetFullPath(path), self, StringComparison.OrdinalIgnoreCase)) continue;

            var row = new ReferenceLayer(path, Path.GetFileNameWithoutExtension(path),
                Path.GetFileName(path), "—", isKitchen: true) { Tip = path };
            SetPlacement(row, StackDepth);   // on top, matching the CLI's `--with <file>`
            KitchenLayers.Add(row);
        }
    }

    /// <summary>How many layers the recipe stacks — the largest gap a Kitchen file can sit in.</summary>
    private int StackDepth => LayerDepth.Count(_recipe.Manifest);

    /// <summary>The display name of the layer at a depth, for a gap label.</summary>
    private string NameAtDepth(int depth)
    {
        string id = LayerDepth.At(_recipe.Manifest, depth);
        // Falls back to the id: a recipe can stack a layer it does not carry, and a gap label naming
        // the phantom is more use than a blank one.
        return _ingById.TryGetValue(id, out var ing) ? ing.Manifest.Name : id;
    }

    /// <summary>Moves a Kitchen row into a gap and restates everything that reads off it. Nothing here
    /// touches the recipe: a gap is a position <i>between</i> layers, so no sibling is renumbered and
    /// no <c>.igt</c> is written.</summary>
    private void SetPlacement(ReferenceLayer row, int gap)
    {
        int max = StackDepth;
        row.Gap = Math.Clamp(gap, 0, max);
        row.PlacementText = max == 0 ? ""
            : row.Gap == 0 ? $"under {NameAtDepth(1)}"
            : $"over {NameAtDepth(row.Gap)}";
        row.IsAbove = _pinDepth is { } pin && row.Gap >= pin;
        row.CanStepUp = row.Gap < max;
        row.CanStepDown = row.Gap > 0;
    }

    // ---- the cached stacks -----------------------------------------------------------------------

    /// <summary>Marks BOTH cached stacks stale and frees them now rather than at the next repaint —
    /// a canvas-sized pair is not something to leave lying around waiting for a stroke.</summary>
    private void InvalidateStack()
    {
        _belowDirty = true;
        _aboveDirty = true;
        DisposeStackCaches();
    }

    /// <summary>Marks only the above-stack stale. Ghosting dims the layers <i>over</i> the art and
    /// cannot touch the ones under it, so throwing the below-stack away too re-rendered a composite
    /// that could not have changed — measured at 8 ms (512px) to 17 ms (1000px) of discarded work per
    /// toggle click, several times the ghost pass it was being redone for.</summary>
    private void InvalidateAboveStack()
    {
        _aboveDirty = true;
        _aboveStack?.Dispose(); _aboveStack = null;
    }

    private void DisposeStackCaches()
    {
        _belowStack?.Dispose(); _belowStack = null;
        _aboveStack?.Dispose(); _aboveStack = null;
    }

    private void EnsureStackCaches()
    {
        if (!_belowDirty && !_aboveDirty) return;
        StackRebuildCount++;
        if (_pinDepth is null)
        {
            _belowDirty = _aboveDirty = false;
            StackProblem = null;
            return;
        }

        try
        {
            var (below, above) = BuildStack();
            var canvas = _draft.Canvas;
            if (_belowDirty)
            {
                _belowStack?.Dispose(); _belowStack = null;
                if (below.Count > 0) _belowStack = StackPreview.Render(canvas, below);
            }
            if (_aboveDirty)
            {
                _aboveStack?.Dispose(); _aboveStack = null;
                if (above.Count > 0)
                {
                    _aboveStack = StackPreview.Render(canvas, above);
                    if (GhostAbove) Ghost(_aboveStack, GhostAlpha);
                }
            }
            // Cleared only on success: a render that threw must be retried, not remembered as done.
            _belowDirty = _aboveDirty = false;
            StackProblem = null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException
                                     or KeyNotFoundException or FormatException)
        {
            // A repaint must not throw. StackPreview.Render already words a canvas mismatch with both
            // sizes named; that message is surfaced verbatim rather than swallowed, and the references
            // simply do not draw until it is resolved.
            //
            // FormatException is in that list because a color spec is parsed at DRAW time, not at read
            // time: `ColorSpec.Parse` refuses an unprefixed spec and `Validator` only REPORTS one, so a
            // book with "1 problem" opens and its static layers reach here unparsed. VariantImagery
            // already catches the same exception for the same reason on the single-layer path.
            DisposeStackCaches();
            StackProblem = ex.Message;
        }
    }

    /// <summary>The enabled references, split around the pinned layer and each ordered bottom-to-top.
    ///
    /// <para>The sibling split comes from <see cref="StackPreview.Split"/> rather than from a second
    /// walk here — it lives in Core precisely so the CLI and this panel cannot disagree about what sits
    /// above what. Kitchen files are then merged in by their gap: a file in gap <c>g</c> sorts directly
    /// after the sibling at depth <c>g</c>, which is what "over <c>g</c>" means.</para></summary>
    private (IReadOnlyList<PreviewLayer> Below, IReadOnlyList<PreviewLayer> Above) BuildStack()
    {
        var recipe = _recipe.Manifest;
        var split = StackPreview.Split(recipe, _ing.Manifest.Id,
            Siblings.Where(s => s.IsOn).Select(s => s.Key));
        int pin = _pinDepth!.Value;

        List<PreviewLayer> Side(IReadOnlyList<string> siblingIds, bool above)
        {
            var items = new List<(int Slot, int Tie, PreviewLayer Layer)>();
            foreach (var id in siblingIds)
                if (_ingById.TryGetValue(id, out var ing))
                    items.Add((LayerDepth.DepthOf(recipe, id), 0, LayerFor(ing)));
            foreach (var row in KitchenLayers)
            {
                if (!row.IsOn || (row.Gap >= pin) != above) continue;
                if (_kitchenOpen.TryGetValue(row.Key, out var ing))
                    items.Add((row.Gap, 1, LayerFor(ing)));
            }
            // Tie 1 after tie 0: "over depth g" paints after the layer at depth g, by definition.
            // OrderBy is stable, so two Kitchen files in the same gap keep the panel's own order.
            return items.OrderBy(i => i.Slot).ThenBy(i => i.Tie).Select(i => i.Layer).ToList();
        }

        return (Side(split.Below, above: false), Side(split.Above, above: true));
    }

    /// <summary>One reference layer, chosen deterministically.
    ///
    /// <para>The variant is <see cref="StackPreview.PickVariant"/>'s — highest weight, ordinal
    /// tie-break — and the color comes from <see cref="StackRoll.ForIngredient"/> seeded off the
    /// ingredient's own id. Both are deterministic on purpose: a reference that changed appearance
    /// between repaints would make the alignment this panel exists to show unreadable. Naming the
    /// variant also costs the RNG no draw, so a Static layer still gets its own fixed color verbatim
    /// and a Custom layer still gets none.</para></summary>
    private static PreviewLayer LayerFor(LoadedIngredient ing) => StackRoll.ForIngredient(
        ing, StackRoll.RngFor("reference:" + ing.Manifest.Id), StackPreview.PickVariant(ing));

    /// <summary>Scales a whole stack's alpha in place.
    ///
    /// <para>Baked into the cached above-stack rather than applied per repaint, so ghosted and
    /// full-opacity both cost the same two <c>DrawImage</c> calls — which is why the toggle invalidates
    /// the cache instead of being read at draw time.</para></summary>
    private static void Ghost(Image<Rgba32> image, double factor)
    {
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    var p = row[x];
                    p.A = (byte)Math.Round(p.A * factor);
                    row[x] = p;
                }
            }
        });
    }

    // ---- commands --------------------------------------------------------------------------------

    /// <summary>Switches one reference on or off.
    ///
    /// <para>Switching a Kitchen file on is the moment its archive is actually read, and the moment its
    /// images are checked against this canvas. A mismatch is <b>refused with the engine's own message</b>
    /// — which names both sizes — and never scaled: a preview that quietly resized a layer would line up
    /// here and nowhere else, which is the single thing this feature exists to rule out.</para></summary>
    /// <param name="row">The row to toggle.</param>
    [RelayCommand]
    private async Task ToggleReference(ReferenceLayer? row)
    {
        if (row is null) return;

        if (row.IsOn)
        {
            row.IsOn = false;
            AfterReferenceChange();
            return;
        }

        if (row.IsKitchen && !_kitchenOpen.ContainsKey(row.Key))
        {
            LoadedIngredient opened;
            try { opened = IngredientArchive.Read(row.Key); }
            catch (Exception ex) { await ShowErrorAsync("Could not open", ex.Message); return; }

            try
            {
                // Probed through the very renderer the panel draws with, so the refusal is worded by
                // the engine rather than paraphrased here.
                using var probe = StackPreview.Render(_draft.Canvas, new[] { LayerFor(opened) });
            }
            // FormatException too: a static layer's fixed color is only parsed when it is drawn, so an
            // unprefixed spec in a loose .igt first surfaces right here — and escaping this filter would
            // both crash the command and orphan every PNG the archive just decoded.
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException
                                         or FormatException)
            {
                opened.Dispose();
                await ShowErrorAsync("Cannot use this layer", ex.Message);
                return;
            }

            _kitchenOpen[row.Key] = opened;
            row.Name = opened.Manifest.Name;
            row.SetKind(opened.Manifest.Kind);
        }

        row.IsOn = true;
        AfterReferenceChange();
    }

    /// <summary>Dims the layers above the edited one — the default.</summary>
    [RelayCommand] private void ShowGhosted() => GhostAbove = true;

    /// <summary>Shows the true composite: every reference at full opacity.</summary>
    [RelayCommand] private void ShowTrueColor() => GhostAbove = false;

    /// <summary>Moves a Kitchen layer one gap up the stack.</summary>
    /// <param name="row">The row to move.</param>
    [RelayCommand] private void PlaceUp(ReferenceLayer? row) => Place(row, +1);

    /// <summary>Moves a Kitchen layer one gap down the stack.</summary>
    /// <param name="row">The row to move.</param>
    [RelayCommand] private void PlaceDown(ReferenceLayer? row) => Place(row, -1);

    private void Place(ReferenceLayer? row, int delta)
    {
        if (row is null || !row.IsKitchen) return;
        int target = Math.Clamp(row.Gap + delta, 0, StackDepth);
        if (target == row.Gap) return;
        SetPlacement(row, target);
        if (row.IsOn) AfterReferenceChange();
    }

    /// <summary>Rebuilds what a selection change actually invalidates: the two cached stacks, the
    /// header count, and the surfaces they are composited into.</summary>
    private void AfterReferenceChange()
    {
        InvalidateStack();
        OnPropertyChanged(nameof(ReferenceCountText));
        OnPropertyChanged(nameof(ReferenceBadgeText));
        RebuildSurfaces();
    }

    // Only the above-stack: ghosting dims what paints OVER the art and cannot reach what paints under
    // it. RenderCanvas is still re-run, so the canvas updates as before.
    partial void OnGhostAboveChanged(bool value)
    {
        InvalidateAboveStack();
        RebuildSurfaces();
    }

    /// <summary>Frees the cached stacks and every Kitchen graph opened for this session.</summary>
    private void DisposeReferences()
    {
        DisposeStackCaches();
        foreach (var ing in _kitchenOpen.Values) ing.Dispose();
        _kitchenOpen.Clear();
    }
}
