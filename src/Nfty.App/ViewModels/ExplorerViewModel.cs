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
    private readonly Func<LoadedIngredient, LoadedRecipe, LoadedCookBook, IngredientEditorViewModel> _editorFactory;
    private readonly Func<LoadedCookBook, CookDialogViewModel> _cookFactory;
    private readonly ICookBookSession _session;
    private readonly IFilePickerService _picker;
    private readonly Func<LoadedIngredient, LoadedCookBook, string, IngredientEditorViewModel> _looseEditorFactory;

    [ObservableProperty] private ExplorerNode? _selectedNode;
    [ObservableProperty] private ViewModelBase? _currentDetail;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    private bool _isEditing;

    [ObservableProperty] private ExplorerNode _root = default!;

    /// <summary>Wraps the single Root as a one-element sequence so a TreeView (which binds to a
    /// collection of roots) can display it.</summary>
    public IReadOnlyList<ExplorerNode> Roots => new[] { Root };

    partial void OnRootChanged(ExplorerNode value) => OnPropertyChanged(nameof(Roots));

    /// <summary>Test hook: the cookbook the tree is currently built from (swapped on editor save).</summary>
    internal LoadedCookBook BookForTest => _book;

    public IReadOnlyList<Crumb> Crumbs { get; private set; } = Array.Empty<Crumb>();

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
        Func<LoadedIngredient, LoadedCookBook, string, IngredientEditorViewModel> looseEditorFactory)
    {
        _book = book; _nav = nav; _dialogs = dialogs; _notify = notify; _bridge = bridge;
        _editorFactory = editorFactory;
        _cookFactory = cookFactory;
        _session = session;
        _picker = picker;
        _looseEditorFactory = looseEditorFactory;
        Root = BuildTree(book);
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

    partial void OnSelectedNodeChanged(ExplorerNode? value)
    {
        OnPropertyChanged(nameof(AddLabel));
        (CurrentDetail as IDisposable)?.Dispose();
        CurrentDetail = value?.Kind switch
        {
            ExplorerNodeKind.CookBook => new CookBookDetailViewModel(_book, _notify,
                () => _dialogs.ShowAsync<object>(_cookFactory(_book))),
            ExplorerNodeKind.Recipe => new RecipeDetailViewModel((LoadedRecipe)value!.Domain!, _book, _bridge, _notify,
                id => OpenIngredientCommand.Execute(id)),
            ExplorerNodeKind.Ingredient => value!.Domain is (LoadedRecipe r, LoadedIngredient i)
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
        Root = BuildTree(book);
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

    [RelayCommand] private void ToggleLock() => IsEditing = !IsEditing;
    [RelayCommand] private void Search() => _notify.Report("Search (⌘K)");
    [RelayCommand]
    private async Task Add()
    {
        // Add is wired for a recipe (add ingredient) and the cookbook root (add recipe), when editing
        // with a source file; other cases stay notify stubs.
        if (!IsEditing || _session.SourcePath is null) { _notify.Report(AddLabel); return; }
        switch (SelectedNode?.Domain)
        {
            case LoadedRecipe recipe: await AddIngredientTo(recipe); return;
            case LoadedCookBook: await AddRecipe(); return;
            default: _notify.Report(AddLabel); return;
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
    [RelayCommand] private void Import() => _notify.Report("Import");
    [RelayCommand] private void SelectNode(ExplorerNode node) => SelectedNode = node;
    [RelayCommand] private void OpenIngredient(string id) => _notify.Report($"Open ingredient {id}");

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
