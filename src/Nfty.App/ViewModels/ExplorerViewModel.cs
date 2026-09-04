using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Models;
using Nfty.App.Services;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Model;

namespace Nfty.App.ViewModels;

/// <param name="Text">The segment's label.</param>
/// <param name="Active">The segment for the current selection — the trail's last entry.</param>
/// <param name="Leading">Whether to draw a "›" separator before this segment.</param>
/// <param name="Target">The node this segment stands for, so clicking it selects that node.
/// explorer.html's crumbs are <c>role="link" tabindex="0"</c> and handle both click and Enter/Space;
/// ours were display-only text, so the trail showed where you were and offered no way back up.</param>
public record Crumb(string Text, bool Active, bool Leading, ExplorerNode? Target = null);

public partial class ExplorerViewModel : ViewModelBase, IDisposable
{
    private readonly INavigationService _nav;
    private readonly IDialogService _dialogs;
    private readonly IImageBridge _bridge;
    private LoadedCookBook _book;
    private ExplorerNode _fullRoot = default!;
    private readonly Func<LoadedIngredient, LoadedRecipe, LoadedCookBook, IngredientEditorViewModel> _editorFactory;
    private readonly Func<LoadedCookBook, CookDialogViewModel> _cookFactory;
    private readonly ICookBookSession _session;
    private readonly IFilePickerService _picker;
    private readonly IStatusService _status;
    private readonly IKitchenSession? _kitchen;
    private readonly IClipboardService? _clipboard;
    private readonly Func<LoadedIngredient, LoadedCookBook, string, IngredientEditorViewModel> _looseEditorFactory;

    [ObservableProperty] private ExplorerNode? _selectedNode;
    [ObservableProperty] private ViewModelBase? _currentDetail;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    [NotifyPropertyChangedFor(nameof(LockTip))]
    [NotifyPropertyChangedFor(nameof(LockStateText))]
    private bool _isEditing;

    /// <summary>The edit lock, pushed into the open detail pane rather than polled by it: the Recipe
    /// detail's reorder grips have to ghost and un-ghost as the lock flips, and that happens while
    /// the pane is open. Rebuilding the pane to refresh it would be a reflow, and would throw away
    /// the row selection the keyboard reorder moves.</summary>
    partial void OnIsEditingChanged(bool value)
    {
        if (CurrentDetail is RecipeDetailViewModel recipe) recipe.CanReorder = value;
    }

    [ObservableProperty] private ExplorerNode _root = default!;

    [ObservableProperty] private string _searchQuery = "";

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    /// <summary>Wraps the single Root as a one-element sequence so a TreeView (which binds to a
    /// collection of roots) can display it.</summary>
    public IReadOnlyList<ExplorerNode> Roots => new[] { Root };

    partial void OnRootChanged(ExplorerNode value) => OnPropertyChanged(nameof(Roots));

    /// <summary>Test hook: the cookbook the tree is currently built from (swapped on editor save).</summary>
    internal LoadedCookBook BookForTest => _book;

    /// <summary>The titlebar breadcrumb for the current selection.</summary>
    public IReadOnlyList<Crumb> Crumbs { get; private set; } = Array.Empty<Crumb>();

    /// <summary>Status bar's lock-state label (explorer.html .statusbar .state: "read-only"/"editing"),
    /// distinct from <see cref="LockTip"/> which phrases the same state as a click instruction.</summary>
    public string LockStateText => IsEditing ? "editing" : "read-only";

    /// <summary>Status bar counts (explorer.html #rTotal/#iTotal/#varTotal), recomputed from
    /// <see cref="_book"/> — refreshed via <see cref="RefreshCounts"/> whenever the book is swapped.</summary>
    public string RecipeCountText => Pluralize(_book.Recipes.Count, "recipe");
    /// <summary>"N ingredients" for the status bar.</summary>
    public string IngredientCountText => Pluralize(_book.Recipes.Sum(r => r.Ingredients.Count), "ingredient");
    /// <summary>"N variants" for the status bar.</summary>
    public string VariantCountText =>
        Pluralize(_book.Recipes.Sum(r => r.Ingredients.Sum(i => i.Manifest.Variants.Count)), "variant");

    private static string Pluralize(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";

    // ---- validity ------------------------------------------------------------------------------
    // The status bar used to print a hardcoded green "Valid" for every book it was handed, without
    // ever running Validator - so a book with a missing image or a rule pointing at a deleted layer
    // still announced itself as fine. Reading an archive deliberately does NOT validate it (see the
    // note on CookBookDetailViewModel's best-effort counting), so the shell has to ask.
    private IReadOnlyList<string> _problems = Array.Empty<string>();

    /// <summary>Whether the open book validates — the status bar's dot and label.</summary>
    public bool IsValid => _problems.Count == 0;
    /// <summary>"Valid" or the problem count.</summary>
    public string ValidityText => IsValid
        ? "Valid"
        : _problems.Count == 1 ? "1 problem" : $"{_problems.Count} problems";

    /// <summary>The problems themselves, on the status pill's tooltip - "3 problems" is only useful
    /// if you can find out what they are without running the CLI's validate command.</summary>
    public string? ValidityTip => IsValid ? null : string.Join(Environment.NewLine, _problems);

    private void RefreshValidity()
    {
        // Validator REPORTS, never throws - that is its contract, precisely so a broken book can be
        // opened and explained rather than being unopenable.
        _problems = Validator.Validate(_book);
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(ValidityText));
        OnPropertyChanged(nameof(ValidityTip));
    }

    private void RefreshCounts()
    {
        OnPropertyChanged(nameof(RecipeCountText));
        OnPropertyChanged(nameof(IngredientCountText));
        OnPropertyChanged(nameof(VariantCountText));
        OnPropertyChanged(nameof(TreeCountText));
    }

    /// <summary>What the Add button offers for the current selection: a Recipe, an Ingredient or a
    /// Variant. One button whose meaning follows the tree, as the mockup has it.</summary>
    public string AddLabel => SelectedNode?.Kind switch
    {
        ExplorerNodeKind.CookBook => "Add recipe",
        ExplorerNodeKind.Recipe => "Add ingredient",
        ExplorerNodeKind.Ingredient => "Add variant",
        _ => "Add",
    };

    // ---- detail pane header (explorer.html .pane-h.detail-h) ----------------------------------
    // The detail pane had no header at all, so its content started 25px above the Contents pane's
    // and the two panes visibly failed to line up. Derived from the selection rather than from a
    // surface each detail ViewModel implements: the Explorer already holds both the node and its
    // domain object, and the mockup's own header is likewise a function of what is selected.

    /// <summary>Drives which type glyph the header shows; null hides the whole band.</summary>
    public ExplorerNodeKind? DetailKind => SelectedNode?.Kind;
    /// <summary>Whether the detail pane is showing the CookBook card.</summary>
    public bool IsDetailCookBook => DetailKind == ExplorerNodeKind.CookBook;
    /// <summary>Whether the detail pane is showing a Recipe.</summary>
    public bool IsDetailRecipe => DetailKind == ExplorerNodeKind.Recipe;
    /// <summary>Whether the detail pane is showing an Ingredient.</summary>
    public bool IsDetailIngredient => DetailKind == ExplorerNodeKind.Ingredient;

    /// <summary>Mockup: the cookbook/recipe name, and for an ingredient "recipe › ingredient".
    /// Uppercased here because Avalonia has no text-transform and the mockup's .pane-h applies
    /// `text-transform: uppercase` to this band. That it is deliberate rather than incidental is
    /// clear from .dcount, which explicitly opts back OUT with `text-transform: none` while the
    /// title does not — confirmed against the rendered mockup, where "Aurora › Body" computes to
    /// uppercase. Invariant casing so the display never varies by machine locale.</summary>
    public string DetailTitle => (SelectedNode?.Domain switch
    {
        LoadedCookBook b => b.Manifest.Name,
        LoadedRecipe r => r.Manifest.Name,
        (LoadedRecipe r, LoadedIngredient i) => $"{r.Manifest.Name} › {i.Manifest.Name}",
        _ => SelectedNode?.Name ?? "",
    }).ToUpperInvariant();

    /// <summary>Mockup: "N recipes" / "N layers" / "N variants" beside the title.</summary>
    public string DetailCount => SelectedNode?.Domain switch
    {
        LoadedCookBook b => Pluralize(b.Recipes.Count, "recipe"),
        LoadedRecipe r => Pluralize(r.Ingredients.Count, "layer"),
        (LoadedRecipe, LoadedIngredient i) => Pluralize(i.Manifest.Variants.Count, "variant"),
        _ => "",
    };

    /// <summary>The right-aligned .vtag chip. Written already-uppercased — Avalonia has no
    /// text-transform, and the mockup uppercases this in CSS.</summary>
    public string DetailTag => SelectedNode?.Kind switch
    {
        ExplorerNodeKind.CookBook => "COOKBOOK",
        ExplorerNodeKind.Recipe => "RECIPE",
        ExplorerNodeKind.Ingredient => "INGREDIENT",
        _ => "",
    };

    private void RefreshDetailHeader()
    {
        OnPropertyChanged(nameof(DetailKind));
        OnPropertyChanged(nameof(IsDetailCookBook));
        OnPropertyChanged(nameof(IsDetailRecipe));
        OnPropertyChanged(nameof(IsDetailIngredient));
        OnPropertyChanged(nameof(DetailTitle));
        OnPropertyChanged(nameof(DetailCount));
        OnPropertyChanged(nameof(DetailTag));
    }

    /// <summary>Opens a CookBook in the Explorer.</summary>
    /// <param name="book">The open book.</param>
    /// <param name="nav">The page stack.</param>
    /// <param name="dialogs">The dialog layer.</param>
    /// <param name="bridge">Converts an ImageSharp frame to an Avalonia bitmap.</param>
    /// <param name="editorFactory">Opens the editor for an ingredient inside this book.</param>
    /// <param name="cookFactory">Opens the cook dialog.</param>
    /// <param name="session">Holds the open book.</param>
    /// <param name="picker">Chooses files to import or save.</param>
    /// <param name="looseEditorFactory">Opens the editor for a loose ingredient.</param>
    /// <param name="status">The status bar's guidance channel.</param>
    /// <param name="kitchen">The open workspace, if any.</param>
    /// <param name="clipboard">Where the report dialog's Copy writes.</param>
    public ExplorerViewModel(LoadedCookBook book, INavigationService nav, IDialogService dialogs,
        IImageBridge bridge,
        Func<LoadedIngredient, LoadedRecipe, LoadedCookBook, IngredientEditorViewModel> editorFactory,
        Func<LoadedCookBook, CookDialogViewModel> cookFactory, ICookBookSession session,
        IFilePickerService picker,
        Func<LoadedIngredient, LoadedCookBook, string, IngredientEditorViewModel> looseEditorFactory,
        IStatusService status,
        IKitchenSession? kitchen = null,
        IClipboardService? clipboard = null)
    {
        _book = book; _nav = nav; _dialogs = dialogs; _bridge = bridge;
        _editorFactory = editorFactory;
        _cookFactory = cookFactory;
        _session = session;
        _picker = picker;
        _status = status;
        _kitchen = kitchen;
        _clipboard = clipboard;
        _looseEditorFactory = looseEditorFactory;
        _fullRoot = BuildTree(book);
        Root = _fullRoot;
        // Select the cookbook on open. Without this nothing is selected, so AddLabel is a bare "Add"
        // and Add itself has no target - it fell through to the stub and reported "not wired", which
        // is exactly the dead end a user hits the moment they open a cookbook.
        SelectedNode = Root;
        // The root open on arrival: a collapsed tree on a book you just opened says nothing about
        // what is in it, and every author's first action was to click the chevron.
        Root.IsExpanded = true;
        RebuildCrumbs();
        RefreshValidity();
        // The status line is shared and outlives this page. A second book opens LOCKED, but the line
        // left over from the first still read "Editing unlocked" — the chip and the status bar
        // disagreeing about the same thing, on the same screen.
        SayLockState();
    }

    private static ExplorerNode BuildTree(LoadedCookBook book)
    {
        var recipeNodes = book.Recipes.Select(r =>
        {
            var ingById = r.Ingredients.ToDictionary(i => i.Manifest.Id, StringComparer.Ordinal);
            var ingNodes = r.Manifest.LayerOrder
                .Where(ingById.ContainsKey)
                .Select(id => new ExplorerNode(id, ingById[id].Manifest.Name,
                    ExplorerNodeKind.Ingredient, Array.Empty<ExplorerNode>(), (r, ingById[id]),
                    ingById[id].Manifest.Kind))
                .ToList();
            return new ExplorerNode(r.Manifest.Id, r.Manifest.Name, ExplorerNodeKind.Recipe, ingNodes, r);
        }).ToList();
        return new ExplorerNode(book.Manifest.Id, book.Manifest.Name, ExplorerNodeKind.CookBook, recipeNodes, book);
    }

    partial void OnSelectedNodeChanged(ExplorerNode? oldValue, ExplorerNode? newValue)
    {
        // Filtering rebuilds every kept node via `record with`, so the same logical node arrives as a
        // NEW instance on each keystroke. Rebuilding the detail then costs a full generation pass per
        // character (RecipeDetail renders a hero) and resets the user's Reroll seed — so re-selecting
        // the same domain object under the same id is a no-op. A real graph swap (ApplyBook) builds new
        // domain objects, so it still rebuilds.
        if (oldValue is not null && newValue is not null
            && oldValue.Id == newValue.Id && ReferenceEquals(oldValue.Domain, newValue.Domain)) return;

        OnPropertyChanged(nameof(AddLabel));
        RefreshDetailHeader();
        // Identity, not a bare flag. A reorder swapped the graph under a detail pane that has ALREADY
        // applied the move to itself (see MoveLayerAsync): rebuilding it here would destroy the row
        // selection and the grip focus the drag and the keyboard both hold, and would render the hero
        // twice. But the pane being preserved must be the pane that ASKED — a flag alone kept whatever
        // happened to be on screen, so navigating away mid-write left the CookBook card sitting under
        // a "CAT / RECIPE" header.
        if (_keepDetailFor is not null && ReferenceEquals(_keepDetailFor, CurrentDetail))
        {
            RebuildCrumbs();
            DeleteSelectedCommand.NotifyCanExecuteChanged();
            return;
        }
        (CurrentDetail as IDisposable)?.Dispose();
        CurrentDetail = newValue?.Kind switch
        {
            ExplorerNodeKind.CookBook => new CookBookDetailViewModel(_book,
                () => _dialogs.ShowAsync<object>(_cookFactory(_book)),
                // stats + inspect, rendered by Core so the text matches the CLI's byte for byte.
                () => _dialogs.ShowAsync<object>(
                    new ReportDialogViewModel(_book, _dialogs, _clipboard ?? new NoopClipboardService()))),
            ExplorerNodeKind.Recipe => new RecipeDetailViewModel((LoadedRecipe)newValue!.Domain!, _book, _bridge,
                id => OpenIngredientCommand.Execute(id),
                // The pane asks; the Explorer owns the graph, the gate and the file. It reads _book at
                // CALL time, not now, so a pane that outlives several saves still edits the live book.
                //
                // The id is hoisted out of the lambda ON PURPOSE. Closing over `newValue` instead would
                // store an ExplorerNode — and through its Domain, a LoadedRecipe owning every decoded
                // image in it — on the pane for the pane's whole life, pinning the PRE-reorder graph
                // across every later ApplyBook. That is the opposite of what the sentence above claims.
                RecipeIdCallback((LoadedRecipe)newValue.Domain!),
                IsEditing),
            ExplorerNodeKind.Ingredient => newValue!.Domain is (LoadedRecipe r, LoadedIngredient i)
                ? new IngredientDetailViewModel(i, r, _book, _bridge,
                    () => OpenEditor(i, r), () => IsEditing,
                    // The rule-count pill jumps to the owning recipe, whose Rules panel is where
                    // this layer's rules actually live.
                    () => SelectedNode = FindNode(Root, r.Manifest.Id) ?? SelectedNode,
                    _status, _picker, _dialogs)
                : null,
            _ => null,
        };
        RebuildCrumbs();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
    }

    // The editor currently on top of this page, so a save can refresh what it is showing. Cleared
    // when it goes away: holding a disposed editor would let a later save call into it.
    private IngredientEditorViewModel? _openEditor;

    private void OpenEditor(LoadedIngredient i, LoadedRecipe r)
    {
        var editor = _editorFactory(i, r, _book);
        editor.Saved += OnEditorSaved;
        editor.Closed += () => { if (ReferenceEquals(_openEditor, editor)) _openEditor = null; };
        _openEditor = editor;
        _nav.To(editor);
    }

    /// <summary>The editor persisted an ingredient; the session now holds the spliced graph. Rebuild
    /// the tree from it and reselect the same ingredient so its detail/thumbnails refresh in place.</summary>
    internal void OnEditorSaved(LoadedCookBook book)
    {
        ApplyBook(book, SelectedNode?.Id);
        // A colour save ADDS a layer to the recipe, and the editor is still open on top of this page
        // with a reference panel listing that recipe's layers — which would now be missing the one
        // just created. The editor rebuilds it from the graph it is handed.
        _openEditor?.RefreshFromBook(book);
    }

    /// <summary>Rebuild the tree from a swapped-in book and select the node with <paramref name="selectId"/>
    /// (root, a recipe, or an ingredient), falling back to the cookbook root.</summary>
    /// <param name="book">The swapped-in graph.</param>
    /// <param name="selectId">Which node to select afterwards.</param>
    /// <param name="revalidate">
    /// Whether to re-run <c>Validator</c> over the whole book. True for every edit that can change
    /// what is legal.
    ///
    /// <para>False for a <b>reorder</b>, and provably so rather than as an optimisation guess:
    /// <c>LayerDepth.MoveTo</c> re-seats one entry in a list, so the id set, the ingredients, the
    /// variants, the weights and the rules are all identical afterwards — every rule Validator applies
    /// sees the same inputs. <c>LayerDepth</c>'s own doc says as much ("Validator gains nothing for
    /// depth, because those bijection rules already ARE the depth invariant"). It is worth skipping
    /// because it is not cheap: <c>Validator</c> reads <i>every pixel of every non-Custom variant</i>
    /// to check greyscale, measured at 51 ms on a six-layer 1000x1000 book — synchronously, on the UI
    /// thread, per keystroke.</para>
    /// </param>
    private void ApplyBook(LoadedCookBook book, string? selectId, bool revalidate = true)
    {
        // Which branches were open, before the tree they belong to stops existing. Selection was
        // already carried across by id; expansion has to be, for the same reason and by the same key.
        var open = OpenBranchIds(_fullRoot);

        _book = book;
        _fullRoot = BuildTree(book);
        Reopen(_fullRoot, open);
        Root = Filter(_fullRoot, SearchQuery);
        OnPropertyChanged(nameof(SearchSummary));
        OnPropertyChanged(nameof(TreeCountText));
        RefreshCounts();
        if (revalidate) RefreshValidity();   // an edit can fix or introduce a problem; a reorder cannot
        SelectedNode = FindNode(Root, selectId) ?? Root;
    }

    private static HashSet<string> OpenBranchIds(ExplorerNode? node)
    {
        var open = new HashSet<string>(StringComparer.Ordinal);
        Walk(node);
        return open;

        void Walk(ExplorerNode? n)
        {
            if (n is null) return;
            if (n.IsExpanded) open.Add(n.Id);
            foreach (var c in n.Children) Walk(c);
        }
    }

    private static void Reopen(ExplorerNode? node, HashSet<string> open)
    {
        if (node is null) return;
        node.IsExpanded = open.Contains(node.Id);
        foreach (var c in node.Children) Reopen(c, open);
    }

    /// <summary>
    /// Whether this book can be edited at all, <b>saying why not</b> when it cannot.
    ///
    /// <para>One place, because there is one rule: edits need the lock open and a <c>.cbk</c> on disk
    /// to write to. Add and reorder had grown their own copies — the read-only sentence was written out
    /// verbatim twice — while <c>CanDeleteSelected</c> expressed the identical pair as a
    /// <c>CanExecute</c> that greys the button and explains nothing. Same rule, three shapes, and a
    /// wording fix that had to be made in two of them.</para>
    /// </summary>
    /// <param name="action">How to finish the sentence: "…then <paramref name="action"/>."</param>
    /// <returns>True when the edit may proceed; false after saying why it may not.</returns>
    private bool CanEditBook(string action)
    {
        if (!IsEditing)
        {
            _status.Say($"Editing is locked. Use the lock button to unlock, then {action}.");
            return false;
        }
        if (_session.SourcePath is null)
        {
            _status.Say("This view is read-only because it isn't backed by a .cbk file on disk.");
            return false;
        }
        return true;
    }

    /// <summary>
    /// The reorder callback a detail pane is handed, capturing the recipe's <b>id</b> and nothing else.
    ///
    /// <para>A separate method rather than a lambda in the switch arm, because a lambda written there
    /// closes over the whole switch scope — the <c>ExplorerNode</c> and, through its
    /// <c>Domain</c>, a <c>LoadedRecipe</c> holding every decoded <c>Image&lt;Rgba32&gt;</c> in that
    /// recipe — and the pane holds the delegate for its whole life, pinning the pre-reorder graph
    /// across every later save. Taking the id as a parameter makes the capture exactly one string.</para>
    /// </summary>
    /// <param name="recipe">The recipe whose id the callback should carry.</param>
    /// <returns>A move callback bound to that id.</returns>
    private Func<string, int, Task<LoadedCookBook?>> RecipeIdCallback(LoadedRecipe recipe)
    {
        string recipeId = recipe.Manifest.Id;
        return (ingredientId, depth) => MoveLayerAsync(recipeId, ingredientId, depth);
    }

    /// <summary>
    /// The detail pane a reorder's tree rebuild must leave alone — the pane that ASKED for the move
    /// and has already applied it to itself. Null except for the duration of that rebuild.
    ///
    /// <para>An identity, not a bare flag. A reorder rewrites every PNG in the book, so on real art
    /// the write takes seconds and the user can navigate away mid-flight. A flag preserved whatever
    /// happened to be on screen when the write landed, which left the CookBook card sitting under a
    /// "CAT / RECIPE" header. Holding the pane itself means the guard can ask "is this still the one
    /// that asked?" and step aside when it is not.</para>
    /// </summary>
    private object? _keepDetailFor;

    /// <summary>
    /// True while a reorder is being written. Reorders do not queue: <c>OnKeyDown</c> is
    /// <c>async void</c> and reentrant, so holding Alt+Up fired a second move before the first had
    /// saved — the two collided on <c>book.cbk.tmp</c> and the loser had already recomputed from the
    /// stale graph, so one keystroke was silently discarded on top of the error dialog.
    ///
    /// <para>Refused rather than queued, deliberately. A queue would apply a move computed against a
    /// stack the user can no longer see, and the honest thing on a seconds-long write is to say so
    /// and let them press again. The same shape as the editor's <c>IsSaving</c>.</para>
    /// </summary>
    private bool _reordering;

    /// <summary>
    /// Reorders one of a recipe's layers, saves the book, and rebuilds the tree onto the saved graph
    /// — the Recipe detail pane's reorder, routed through the one place that owns the edit lock, the
    /// source file and the cookbook graph.
    /// </summary>
    /// <param name="recipeId">Which recipe owns the layer.</param>
    /// <param name="ingredientId">The layer to move.</param>
    /// <param name="toDepth">Its new 1-based depth; <c>LayerDepth</c> clamps it to the stack.</param>
    /// <returns>The saved graph, or null when the move was refused or failed. Refusals are the same
    /// two the Add path checks and they are <b>said</b>, not thrown: reordering while locked is a
    /// user mistake with an obvious remedy, not an error.</returns>
    internal async Task<LoadedCookBook?> MoveLayerAsync(string recipeId, string ingredientId, int toDepth)
    {
        if (!CanEditBook("reorder the layers")) return null;
        if (_reordering)
        {
            _status.Say("Still saving the last reorder — try that again in a moment.");
            return null;
        }
        // No special case here for a recipe that stacks a layer it does not carry. There used to be
        // one, because LayerRow.Index was the row's POSITION and got fed back as a depth, so the two
        // diverged on exactly that book. Numbering the rows from LayerDepth instead made the index a
        // depth by construction — the bandaid's premise, not just the bandaid, is gone.

        _reordering = true;
        // Captured BEFORE the await: whatever is on screen when the write finishes may not be this.
        object? askedFrom = CurrentDetail;
        try
        {
            var book2 = CookBookEdits.MoveLayer(_book, recipeId, ingredientId, toDepth);
            var book3 = await CookBookPersistence.PersistAsync(_session, book2);

            // Navigating away mid-write is not an error and must not be undone by the write landing:
            // re-select the recipe only if the user is still looking at the pane that asked.
            string? selectId = ReferenceEquals(CurrentDetail, askedFrom) ? recipeId : SelectedNode?.Id;
            _keepDetailFor = askedFrom;
            try { ApplyBook(book3, selectId, revalidate: false); }
            finally { _keepDetailFor = null; }
            return book3;
        }
        catch (Exception ex)
        {
            await ShowError("Could not reorder layers", ex.Message);
            return null;
        }
        finally { _reordering = false; }
    }

    private static ExplorerNode? FindNode(ExplorerNode root, string? id)
    {
        if (id is null) return null;
        if (root.Id == id) return root;
        foreach (var r in root.Children)
        {
            if (r.Id == id) return r;
            var hit = r.Children.FirstOrDefault(n => n.Id == id);
            if (hit is not null) return hit;
        }
        return null;
    }

    /// <summary>Recompute the visible tree from the unfiltered one, keeping the selection only if it
    /// survived (a filtered-away selection would leave the detail pane — and the mutating commands —
    /// pointing at a node the user can no longer see).</summary>
    private void ApplyFilter()
    {
        var selectedId = SelectedNode?.Id;
        Root = Filter(_fullRoot, SearchQuery);
        OnPropertyChanged(nameof(SearchSummary));
        OnPropertyChanged(nameof(TreeCountText));
        // Only re-home an EXISTING selection; typing must not select the root out of nowhere (which
        // would also flip AddLabel and populate the detail pane as a side effect of searching).
        if (selectedId is not null) SelectedNode = FindNode(Root, selectedId) ?? Root;
    }

    /// <summary>The CONTENTS pane header count (the mockup's .hcount). While filtering it reports
    /// the match count, so a query that matches nothing reads as "0 matches" rather than as an
    /// unexplained empty tree; otherwise it reports the recipe count the header stands for.</summary>
    public string TreeCountText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(SearchQuery)) return SearchSummary;
            int n = Root.Children.Count;
            return n == 1 ? "1 recipe" : $"{n} recipes";
        }
    }

    /// <summary>Match count for the current query ("" when not filtering), so a zero-result query
    /// reads as such instead of an unexplained empty tree.</summary>
    public string SearchSummary
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SearchQuery)) return "";
            int n = Root.Children.Count + Root.Children.Sum(r => r.Children.Count);
            return n == 1 ? "1 match" : $"{n} matches";
        }
    }

    private static ExplorerNode Filter(ExplorerNode root, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return root;
        var q = query.Trim();
        var recipes = new List<ExplorerNode>();
        foreach (var r in root.Children)
        {
            bool recipeMatches = Matches(r, q);
            var kept = recipeMatches ? r.Children.ToList() : r.Children.Where(i => Matches(i, q)).ToList();
            if (recipeMatches || kept.Count > 0)
                recipes.Add(r with { Children = kept });
        }
        return root with { Children = recipes };
    }

    /// <summary>Name or id, case-insensitive; an ingredient also matches on its variants' ids/names
    /// (variants aren't tree nodes, so this only decides whether the ingredient is shown).</summary>
    private static bool Matches(ExplorerNode n, string q)
    {
        if (n.Id.Contains(q, StringComparison.OrdinalIgnoreCase)
            || n.Name.Contains(q, StringComparison.OrdinalIgnoreCase)) return true;
        if (n.Domain is (LoadedRecipe, LoadedIngredient ing))
            return ing.Manifest.Variants.Any(v =>
                v.Id.Contains(q, StringComparison.OrdinalIgnoreCase)
                || v.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
        return false;
    }

    /// <summary>The edit lock had no visible state at all (static padlock, no active styling), so a
    /// working toggle read as a dead button. Flip it, restyle it, and say so on the status line.</summary>
    [RelayCommand]
    private void ToggleLock()
    {
        IsEditing = !IsEditing;
        SayLockState();
    }

    /// <summary>Puts the current lock state on the status line.
    ///
    /// <para>Called on open as well as on toggle. Opening a second book starts it locked, but the
    /// line left over from the first still read "Editing unlocked" — so the chip said read-only and
    /// the status bar said the opposite, on the same screen.</para></summary>
    private void SayLockState() => _status.Say(IsEditing
        ? "Editing unlocked - you can add, delete and edit."
        : "Editing locked - unlock to make changes.");

    /// <summary>Tooltip that states the CURRENT state and what clicking will do.</summary>
    public string LockTip => IsEditing
        ? "Editing unlocked - click to lock"
        : "Editing locked - click to unlock";
    [RelayCommand]
    private async Task Add()
    {
        // Adding is GATED, not unbuilt — say why, rather than routing through the not-wired channel
        // (which prefixes "Not wired yet:" and told users a working feature didn't exist).
        if (!CanEditBook("add")) return;
        switch (SelectedNode?.Domain)
        {
            case LoadedRecipe recipe: await AddIngredientTo(recipe); return;
            case LoadedCookBook: await AddRecipe(); return;
            // Variants belong to the ingredient editor, which owns the draft + undo history — so
            // "Add variant" opens it there rather than pretending the action doesn't exist.
            case (LoadedRecipe r, LoadedIngredient i):
                _status.Say($"Add variants to “{i.Manifest.Name}” in the editor.");
                OpenEditor(i, r);
                return;
            default:
                _status.Say("Select a cookbook, recipe or ingredient to add to.");
                return;
        }
    }

    private async Task AddRecipe()
    {
        // The wizard's "Resulting mix" needs the siblings the new weight will be normalised against.
        var siblings = _book.Recipes
            .Select(r => (r.Manifest.Name, Weight: _book.Manifest.RecipeWeights.GetValueOrDefault(r.Manifest.Id)))
            .ToList();
        var wizard = new NewRecipeViewModel(_dialogs, siblings);
        var result = await _dialogs.ShowAsync<NewRecipeViewModel>(wizard);
        if (result is null) return;   // cancelled
        if (string.IsNullOrWhiteSpace(result.DerivedId))
        {
            await ShowError("Invalid recipe", "The recipe needs a name.");
            return;
        }
        // The wizard offers "The Kitchen" as a destination and this branch used to be missing
        // entirely, so choosing it silently added the recipe to the CookBook anyway - a user choice
        // accepted and then discarded, which is worse than not offering it.
        if (result.Destination == RecipeDestination.LooseKitchen)
        {
            await CreateLooseRecipe(result);
            return;
        }
        if (_book.Recipes.Any(r => r.Manifest.Id == result.DerivedId))
        {
            await ShowError("Duplicate recipe", $"A recipe “{result.DerivedId}” already exists.");
            return;
        }
        try
        {
            // A fresh recipe is intentionally empty; it is not yet generatable (ValidateRecipe would
            // flag the empty layerOrder), so it is NOT validated here — the user fills it via
            // "Add ingredient" next, and the cook path validates the whole book at generation time.
            var recipe = new LoadedRecipe
            {
                Manifest = new RecipeManifest(result.DerivedId, result.Name,
                    Array.Empty<string>(), Array.Empty<IncompatibilityRule>()),
                Ingredients = Array.Empty<LoadedIngredient>(),
            };

            var book2 = CookBookEdits.UpsertRecipe(_book, recipe, result.Weight);
            var book3 = await CookBookPersistence.PersistAsync(_session, book2);
            ApplyBook(book3, recipe.Manifest.Id);   // select the new (empty) recipe
        }
        catch (Exception ex)
        {
            await ShowError("Could not add recipe", ex.Message);
        }
    }

    private async Task AddIngredientTo(LoadedRecipe recipe)
    {
        var wizard = new NewIngredientViewModel(_dialogs);
        var result = await _dialogs.ShowAsync<NewIngredientViewModel>(wizard);
        if (result is null) return;   // cancelled

        if (string.IsNullOrWhiteSpace(result.DerivedId))   // authoritative: never persist an empty id
        {
            await ShowError("Invalid ingredient", "The ingredient needs a name.");
            return;
        }

        if (result.Destination == RecipeDestination.LooseKitchen)
        {
            await CreateLooseIngredient(result);
            return;
        }

        var newIng = result.Build(_book.Manifest.Canvas);   // owns the blank image until adopted
        var adopted = false;                                 // true once the persisted book owns its image
        try
        {
            if (recipe.Ingredients.Any(i => i.Manifest.Id == newIng.Manifest.Id))
            {
                await ShowError("Duplicate ingredient",
                    $"An ingredient “{newIng.Manifest.Id}” already exists in “{recipe.Manifest.Name}”.");
                return;
            }
            var problems = Validator.ValidateIngredient(newIng);
            if (problems.Count > 0)
            {
                await ShowError("Invalid ingredient", string.Join("\n", problems));
                return;
            }

            var book2 = CookBookEdits.UpsertIngredient(_book, recipe.Manifest.Id, newIng);
            var book3 = await CookBookPersistence.PersistAsync(_session, book2);
            adopted = true;   // book3 now owns newIng's image — don't dispose it below
            ApplyBook(book3, newIng.Manifest.Id);

            var recipe3 = book3.Recipes.First(r => r.Manifest.Id == recipe.Manifest.Id);
            var ing3 = recipe3.Ingredients.First(i => i.Manifest.Id == newIng.Manifest.Id);
            OpenEditor(ing3, recipe3);   // paint the blank variant; the editor's Save persists
        }
        catch (Exception ex)
        {
            await ShowError("Could not add ingredient", ex.Message);
        }
        finally
        {
            if (!adopted) newIng.Dispose();   // cancelled early / validation failed / write failed → free it
        }
    }

    /// <summary>The "Loose (Kitchen)" destination: write a standalone .igt (never touching the open
    /// cookbook) and open it in a loose editor — mirrors LandingViewModel.NewIngredient's B3a steps.</summary>
    /// <summary>Saves a new Recipe as a loose .rcp rather than adding it to the open CookBook.
    /// Mirrors <see cref="CreateLooseIngredient"/>: pick a path, write atomically, report problems
    /// rather than throwing. The recipe is empty by design - it is filled by opening it and adding
    /// ingredients - so it is not added to the recents list as something already useful.</summary>
    private async Task CreateLooseRecipe(NewRecipeViewModel result)
    {
        // The Kitchen IS the loose-items folder - the creation-flows spec settled that they are one
        // concept, not two - so when one is open the save defaults into it rather than asking again.
        var path = DefaultLoosePath(result.DerivedId + Archives.RecipeExtension);
        if (path is null)
        {
            try { path = await _picker.SaveFileAsync("Save new recipe", ".rcp"); }
            catch (Exception ex) { await ShowError("Could not save", ex.Message); return; }
            if (path is null) return;   // cancelled
        }

        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest(result.DerivedId, result.Name,
                Array.Empty<string>(), Array.Empty<IncompatibilityRule>()),
            Ingredients = Array.Empty<LoadedIngredient>(),
        };

        try
        {
            var problems = LooseWorkspace.WriteRecipe(path, recipe);
            if (problems.Count > 0)
            {
                await ShowError("Invalid recipe", string.Join("\n", problems));
                return;
            }
            _status.Say($"Saved “{result.Name}” to {path}. Open it to add ingredients.");
        }
        catch (Exception ex) { await ShowError("Could not save", ex.Message); }
    }

    /// <summary>Where a loose item goes when a Kitchen is open: into it, under the given file name.
    /// Null when no Kitchen is open (the caller falls back to asking), or when a file of that name is
    /// already there — silently replacing something in the workspace is not a save, it is a
    /// deletion, so that case goes to the picker where the user can see and confirm it.</summary>
    private string? DefaultLoosePath(string fileName)
    {
        var dir = _kitchen?.Current?.Directory;
        if (dir is null) return null;
        var candidate = Path.Combine(dir, fileName);
        return File.Exists(candidate) ? null : candidate;
    }

    private async Task CreateLooseIngredient(NewIngredientViewModel result)
    {
        if (!result.TryGetCanvas(out var canvas))
        {
            await ShowError("Invalid canvas", "Enter a canvas size like 512x512.");
            return;
        }
        var path = DefaultLoosePath(result.DerivedId + Archives.IngredientExtension);
        if (path is null)
        {
            try { path = await _picker.SaveFileAsync("Save new ingredient", ".igt"); }
            catch (Exception ex) { await ShowError("Could not save", ex.Message); return; }
            if (path is null) return;   // cancelled
        }

        LoadedIngredient built;
        try { built = result.Build(canvas); }   // Build allocates the raster — guard it (OOM on a huge canvas)
        catch (Exception ex) { await ShowError("Could not save", ex.Message); return; }

        try
        {
            // Validate + atomically write (replaces an existing file rather than throwing).
            var problems = LooseWorkspace.WriteIngredient(path, built);
            if (problems.Count > 0)
            {
                await ShowError("Invalid ingredient", string.Join("\n", problems));
                return;
            }
        }
        catch (Exception ex) { await ShowError("Could not save", ex.Message); return; }
        finally { built.Dispose(); }

        // Open the new .igt in a loose editor (a fresh copy; the editor owns the wrapper book).
        LoadedIngredient? ing = null;
        try
        {
            ing = IngredientArchive.Read(path);
            var book = LooseWorkspace.WrapIngredient(ing);   // the loose editor owns + disposes this
            _nav.To(_looseEditorFactory(ing, book, path));
            ing = null;   // ownership handed to the editor
        }
        catch (Exception ex)
        {
            ing?.Dispose();   // never reached the editor — free it
            await ShowError("Could not open", ex.Message);
        }
    }

    private Task ShowError(string title, string message) =>
        _dialogs.ShowAsync<object>(new ErrorDialogViewModel(_dialogs, title, message));
    /// <summary>Importing a loose file opens it as its own document, which is a start-screen action;
    /// importing INTO the open cookbook isn't built yet, so say that rather than nothing.</summary>
    [RelayCommand] private void Import() =>
        _status.Say("Importing into an open cookbook isn't available yet - use Import on the start screen to open a loose file.");
    [RelayCommand] private void SelectNode(ExplorerNode node) => SelectedNode = node;
    /// <summary>Clicking a layer in the recipe detail jumps to that ingredient in the tree.</summary>
    [RelayCommand]
    private void OpenIngredient(string id)
    {
        var node = FindNode(Root, id);
        if (node is not null) SelectedNode = node;
        else _status.Say($"“{id}” isn't in the current view (a filter may be hiding it).");
    }

    // Delete needs edit-mode ON, a source .cbk to write to, and a recipe/ingredient selected
    // (the cookbook root is never deletable — close the book instead).
    private bool CanDeleteSelected() =>
        IsEditing && _session.SourcePath is not null
        && SelectedNode?.Kind is ExplorerNodeKind.Recipe or ExplorerNodeKind.Ingredient;

    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private async Task DeleteSelected()
    {
        if (SelectedNode is not { } node) return;
        var ok = await _dialogs.ShowAsync<bool>(new ConfirmDialogViewModel(_dialogs,
            "Delete?", $"Delete “{node.Name}” — this can’t be undone.", "Delete"));
        if (!ok) return;
        try
        {
            LoadedCookBook book2;
            string? parentId;
            IDisposable removed;
            if (node.Domain is (LoadedRecipe r, LoadedIngredient i))
            {
                book2 = CookBookEdits.RemoveIngredient(_book, r.Manifest.Id, i.Manifest.Id);
                parentId = r.Manifest.Id; removed = i;
            }
            else if (node.Domain is LoadedRecipe rr)
            {
                book2 = CookBookEdits.RemoveRecipe(_book, rr.Manifest.Id);
                parentId = Root.Id; removed = rr;
            }
            else return;   // cookbook root — not deletable (also gated by CanExecute)

            var book3 = await CookBookPersistence.PersistAsync(_session, book2);
            removed.Dispose();                 // free the orphaned subtree's images (recipe cascades)
            ApplyBook(book3, parentId);
        }
        catch (Exception ex)
        {
            await _dialogs.ShowAsync<object>(new ErrorDialogViewModel(_dialogs, "Could not delete", ex.Message));
        }
    }

    private void RebuildCrumbs()
    {
        // Each segment carries the node it names, so the trail navigates rather than just reporting.
        var parts = new List<(string Text, ExplorerNode? Target)> { (Root.Name, Root) };
        switch (SelectedNode?.Kind)
        {
            case ExplorerNodeKind.Recipe:
                parts.Add((SelectedNode.Name, SelectedNode));
                break;
            case ExplorerNodeKind.Ingredient when SelectedNode.Domain is (LoadedRecipe r, LoadedIngredient i):
                // The ingredient node knows its recipe as a domain object, not as a tree node, so the
                // middle segment resolves back to the node that owns it.
                parts.Add((r.Manifest.Name, Root.Children.FirstOrDefault(c => ReferenceEquals(c.Domain, r))));
                parts.Add((i.Manifest.Name, SelectedNode));
                break;
        }
        Crumbs = parts
            .Select((p, idx) => new Crumb(p.Text, idx == parts.Count - 1, idx > 0, p.Target))
            .ToList();
        OnPropertyChanged(nameof(Crumbs));
    }

    /// <summary>Disposes the detail pane, which owns its rendered bitmaps.</summary>
    public void Dispose() => (CurrentDetail as IDisposable)?.Dispose();
}
