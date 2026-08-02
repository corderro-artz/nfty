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

public record Crumb(string Text, bool Active, bool Leading);

public partial class ExplorerViewModel : ViewModelBase, IDisposable
{
    private readonly INavigationService _nav;
    private readonly IDialogService _dialogs;
    private readonly INotYetWired _notify;
    private readonly IImageBridge _bridge;
    private LoadedCookBook _book;
    private ExplorerNode _fullRoot = default!;
    private readonly Func<LoadedIngredient, LoadedRecipe, LoadedCookBook, IngredientEditorViewModel> _editorFactory;
    private readonly Func<LoadedCookBook, CookDialogViewModel> _cookFactory;
    private readonly ICookBookSession _session;
    private readonly IFilePickerService _picker;
    private readonly IStatusService _status;
    private readonly Func<LoadedIngredient, LoadedCookBook, string, IngredientEditorViewModel> _looseEditorFactory;

    [ObservableProperty] private ExplorerNode? _selectedNode;
    [ObservableProperty] private ViewModelBase? _currentDetail;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    [NotifyPropertyChangedFor(nameof(LockTip))]
    [NotifyPropertyChangedFor(nameof(LockStateText))]
    private bool _isEditing;

    [ObservableProperty] private ExplorerNode _root = default!;

    [ObservableProperty] private string _searchQuery = "";

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    /// <summary>Wraps the single Root as a one-element sequence so a TreeView (which binds to a
    /// collection of roots) can display it.</summary>
    public IReadOnlyList<ExplorerNode> Roots => new[] { Root };

    partial void OnRootChanged(ExplorerNode value) => OnPropertyChanged(nameof(Roots));

    /// <summary>Test hook: the cookbook the tree is currently built from (swapped on editor save).</summary>
    internal LoadedCookBook BookForTest => _book;

    public IReadOnlyList<Crumb> Crumbs { get; private set; } = Array.Empty<Crumb>();

    /// <summary>Status bar's lock-state label (explorer.html .statusbar .state: "read-only"/"editing"),
    /// distinct from <see cref="LockTip"/> which phrases the same state as a click instruction.</summary>
    public string LockStateText => IsEditing ? "editing" : "read-only";

    /// <summary>Status bar counts (explorer.html #rTotal/#iTotal/#varTotal), recomputed from
    /// <see cref="_book"/> — refreshed via <see cref="RefreshCounts"/> whenever the book is swapped.</summary>
    public string RecipeCountText => Pluralize(_book.Recipes.Count, "recipe");
    public string IngredientCountText => Pluralize(_book.Recipes.Sum(r => r.Ingredients.Count), "ingredient");
    public string VariantCountText =>
        Pluralize(_book.Recipes.Sum(r => r.Ingredients.Sum(i => i.Manifest.Variants.Count)), "variant");

    private static string Pluralize(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";

    private void RefreshCounts()
    {
        OnPropertyChanged(nameof(RecipeCountText));
        OnPropertyChanged(nameof(IngredientCountText));
        OnPropertyChanged(nameof(VariantCountText));
    }

    public string AddLabel => SelectedNode?.Kind switch
    {
        ExplorerNodeKind.CookBook => "Add recipe",
        ExplorerNodeKind.Recipe => "Add ingredient",
        ExplorerNodeKind.Ingredient => "Add variant",
        _ => "Add",
    };

    public ExplorerViewModel(LoadedCookBook book, INavigationService nav, IDialogService dialogs,
        INotYetWired notify, IImageBridge bridge,
        Func<LoadedIngredient, LoadedRecipe, LoadedCookBook, IngredientEditorViewModel> editorFactory,
        Func<LoadedCookBook, CookDialogViewModel> cookFactory, ICookBookSession session,
        IFilePickerService picker,
        Func<LoadedIngredient, LoadedCookBook, string, IngredientEditorViewModel> looseEditorFactory,
        IStatusService status)
    {
        _book = book; _nav = nav; _dialogs = dialogs; _notify = notify; _bridge = bridge;
        _editorFactory = editorFactory;
        _cookFactory = cookFactory;
        _session = session;
        _picker = picker;
        _status = status;
        _looseEditorFactory = looseEditorFactory;
        _fullRoot = BuildTree(book);
        Root = _fullRoot;
        // Select the cookbook on open. Without this nothing is selected, so AddLabel is a bare "Add"
        // and Add itself has no target - it fell through to the stub and reported "not wired", which
        // is exactly the dead end a user hits the moment they open a cookbook.
        SelectedNode = Root;
        RebuildCrumbs();
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
        (CurrentDetail as IDisposable)?.Dispose();
        CurrentDetail = newValue?.Kind switch
        {
            ExplorerNodeKind.CookBook => new CookBookDetailViewModel(_book, _notify,
                () => _dialogs.ShowAsync<object>(_cookFactory(_book))),
            ExplorerNodeKind.Recipe => new RecipeDetailViewModel((LoadedRecipe)newValue!.Domain!, _book, _bridge, _notify,
                id => OpenIngredientCommand.Execute(id)),
            ExplorerNodeKind.Ingredient => newValue!.Domain is (LoadedRecipe r, LoadedIngredient i)
                ? new IngredientDetailViewModel(i, r, _book, _bridge, _notify,
                    () => OpenEditor(i, r), () => IsEditing)
                : null,
            _ => null,
        };
        RebuildCrumbs();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
    }

    private void OpenEditor(LoadedIngredient i, LoadedRecipe r)
    {
        var editor = _editorFactory(i, r, _book);
        editor.Saved += OnEditorSaved;
        _nav.To(editor);
    }

    /// <summary>The editor persisted an ingredient; the session now holds the spliced graph. Rebuild
    /// the tree from it and reselect the same ingredient so its detail/thumbnails refresh in place.</summary>
    internal void OnEditorSaved(LoadedCookBook book) => ApplyBook(book, SelectedNode?.Id);

    /// <summary>Rebuild the tree from a swapped-in book and select the node with <paramref name="selectId"/>
    /// (root, a recipe, or an ingredient), falling back to the cookbook root.</summary>
    private void ApplyBook(LoadedCookBook book, string? selectId)
    {
        _book = book;
        _fullRoot = BuildTree(book);
        Root = Filter(_fullRoot, SearchQuery);
        OnPropertyChanged(nameof(SearchSummary));
        RefreshCounts();
        SelectedNode = FindNode(Root, selectId) ?? Root;
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
        // Only re-home an EXISTING selection; typing must not select the root out of nowhere (which
        // would also flip AddLabel and populate the detail pane as a side effect of searching).
        if (selectedId is not null) SelectedNode = FindNode(Root, selectedId) ?? Root;
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
        _status.Say(IsEditing
            ? "Editing unlocked - you can add, delete and edit."
            : "Editing locked - unlock to make changes.");
    }

    /// <summary>Tooltip that states the CURRENT state and what clicking will do.</summary>
    public string LockTip => IsEditing
        ? "Editing unlocked - click to lock"
        : "Editing locked - click to unlock";
    [RelayCommand]
    private async Task Add()
    {
        // Adding is GATED, not unbuilt — say why, rather than routing through the not-wired channel
        // (which prefixes "Not wired yet:" and told users a working feature didn't exist).
        if (!IsEditing)
        {
            _status.Say("Editing is locked. Use the lock button to unlock, then add.");
            return;
        }
        if (_session.SourcePath is null)
        {
            _status.Say("This view is read-only because it isn't backed by a .cbk file on disk.");
            return;
        }
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
        var wizard = new NewRecipeViewModel(_dialogs, _notify);
        var result = await _dialogs.ShowAsync<NewRecipeViewModel>(wizard);
        if (result is null) return;   // cancelled
        if (string.IsNullOrWhiteSpace(result.DerivedId))
        {
            await ShowError("Invalid recipe", "The recipe needs a name.");
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
        var wizard = new NewIngredientViewModel(_dialogs, _notify);
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
    private async Task CreateLooseIngredient(NewIngredientViewModel result)
    {
        if (!result.TryGetCanvas(out var canvas))
        {
            await ShowError("Invalid canvas", "Enter a canvas size like 512x512.");
            return;
        }
        string? path;
        try { path = await _picker.SaveFileAsync("Save new ingredient", ".igt"); }
        catch (Exception ex) { await ShowError("Could not save", ex.Message); return; }
        if (path is null) return;   // cancelled

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
        var parts = new List<string> { Root.Name };
        switch (SelectedNode?.Kind)
        {
            case ExplorerNodeKind.Recipe:
                parts.Add(SelectedNode.Name);
                break;
            case ExplorerNodeKind.Ingredient when SelectedNode.Domain is (LoadedRecipe r, LoadedIngredient i):
                parts.Add(r.Manifest.Name);
                parts.Add(i.Manifest.Name);
                break;
        }
        Crumbs = parts.Select((t, idx) => new Crumb(t, idx == parts.Count - 1, idx > 0)).ToList();
        OnPropertyChanged(nameof(Crumbs));
    }

    public void Dispose() => (CurrentDetail as IDisposable)?.Dispose();
}
