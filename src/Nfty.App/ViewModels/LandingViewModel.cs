using System.IO;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Models;
using Nfty.App.Services;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Nfty.Core.Output;

namespace Nfty.App.ViewModels;

/// <summary>The pre-open default screen: Create/Open groups plus a Recent list. Open/Import read a
/// real archive off disk and hand it to the <see cref="ICookBookSession"/>, then navigate into the
/// Explorer; Open .set reads a cooked Set and navigates to the Set browser. The remaining actions
/// (New Kitchen, Open recent) are not-yet-wired stubs.</summary>
public partial class LandingViewModel : ViewModelBase
{
    private readonly INavigationService _nav;
    private readonly IDialogService _dialogs;
    private readonly INotYetWired _notify;
    private readonly IFilePickerService _picker;
    private readonly IRecentsService _recents;
    private readonly ICookBookSession _session;
    private readonly Func<LoadedCookBook, ExplorerViewModel> _explorerFactory;
    private readonly Func<LoadedSet, SetBrowserViewModel> _setBrowserFactory;
    private readonly Func<LoadedIngredient, LoadedCookBook, string, IngredientEditorViewModel> _looseEditorFactory;
    private readonly IKitchenSession? _kitchen;

    /// <summary>A snapshot, not the service's live list: bindings short-circuit when a property
    /// returns the same instance, so returning the live list would make OnPropertyChanged inert and
    /// a removed row would stay on screen.</summary>
    public IReadOnlyList<RecentItem> Recents => _recents.Items.ToArray();

    /// <summary>Drives the first-run empty state. Without it the whole right half of the start
    /// screen is blank on a fresh install, which reads as a failed load rather than as "no history
    /// yet".</summary>
    public bool HasNoRecents => Recents.Count == 0;

    public LandingViewModel(INavigationService nav, IDialogService dialogs, INotYetWired notify,
        IFilePickerService picker, IRecentsService recents, ICookBookSession session,
        Func<LoadedCookBook, ExplorerViewModel> explorerFactory,
        Func<LoadedSet, SetBrowserViewModel> setBrowserFactory,
        Func<LoadedIngredient, LoadedCookBook, string, IngredientEditorViewModel> looseEditorFactory,
        IKitchenSession? kitchen = null)
    {
        _nav = nav; _dialogs = dialogs; _notify = notify; _picker = picker; _recents = recents;
        _session = session; _explorerFactory = explorerFactory; _setBrowserFactory = setBrowserFactory;
        _looseEditorFactory = looseEditorFactory;
        _kitchen = kitchen;

        // The Kitchen can change from under this screen — NewKitchen and OpenKitchen both alter it,
        // and so does anything that closes one — so the row that names it, and CloseKitchen's own
        // availability, follow the session rather than a snapshot taken at construction.
        if (_kitchen is not null)
            _kitchen.Changed += () =>
            {
                OnPropertyChanged(nameof(KitchenName));
                OnPropertyChanged(nameof(HasKitchen));
                CloseKitchenCommand.NotifyCanExecuteChanged();
            };
    }

    [RelayCommand]
    private async Task NewCookBook()
    {
        var wizard = new NewCookBookViewModel(_dialogs, _notify);
        var result = await _dialogs.ShowAsync<NewCookBookViewModel>(wizard);
        if (result is null) return;   // cancelled
        if (string.IsNullOrWhiteSpace(result.DerivedId))
        {
            ShowError("Invalid cookbook", "The cookbook needs a name.");
            return;
        }
        string? path;
        try { path = await _picker.SaveFileAsync("Save new cookbook", ".cbk"); }
        catch (Exception ex) { ShowError("Could not save", ex.Message); return; }
        if (path is null) return;   // cancelled the picker

        var manifest = new CookBookManifest(result.DerivedId, result.Name,
            new Dimensions(result.Width, result.Height),
            new Collection(result.Name, result.Description, result.Symbol),
            new Dictionary<string, double>(),   // no recipes yet
            TargetSupply: result.TargetSupplyOrNull);
        try { CookBookPersistence.WriteNew(path, manifest, Array.Empty<LoadedRecipe>()); }
        catch (Exception ex) { ShowError("Could not save", ex.Message); return; }

        OpenPath(path);   // reads it back (fresh hash), session.Open(book, path), → Explorer
    }
    /// <summary>Creates a Kitchen: a .ktn naming the folder it is saved into. The folder becomes the
    /// workspace, so this deliberately picks a FILE location rather than a folder — the user names
    /// the workspace and chooses where it lives in one step, and Kitchen.Create makes the folder if
    /// it is missing.
    ///
    /// This was CanExecute=Never with a "Not wired yet" report for as long as Kitchens were unbuilt,
    /// which was the honest state then and is not now.</summary>
    [RelayCommand(CanExecute = nameof(CanNewKitchen))]
    private async Task NewKitchen()
    {
        string? path;
        try { path = await _picker.SaveFileAsync("New Kitchen", Nfty.Core.Formats.Kitchen.Extension); }
        catch (Exception ex) { ShowError("Could not create", ex.Message); return; }
        if (path is null) return;   // cancelled

        var name = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(name)) { ShowError("Invalid Kitchen", "The Kitchen needs a name."); return; }

        try
        {
            Nfty.Core.Formats.Kitchen.Create(path,
                new KitchenManifest(DeriveId(name), name));
            _kitchen!.Open(path);
        }
        catch (Exception ex) { ShowError("Could not create", ex.Message); }
    }

    /// <summary>Only offered when a Kitchen session exists to hold the result. Composition supplies
    /// one; a ViewModel constructed without it (some tests) keeps the old disabled behaviour rather
    /// than throwing at click time.</summary>
    private bool CanNewKitchen() => _kitchen is not null;

    /// <summary>
    /// Opens an existing Kitchen.
    ///
    /// <para>Without this a Kitchen was a one-session object: <c>IKitchenSession.Open</c> was called
    /// from exactly one place — the CREATE flow — so a workspace made on Monday was unreachable on
    /// Tuesday. The titlebar chip stayed empty and loose saves stopped defaulting into it, with
    /// nothing on screen to explain why.</para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanNewKitchen))]
    private async Task OpenKitchen()
    {
        string? path;
        try { path = await _picker.OpenFileAsync("Open Kitchen", Nfty.Core.Formats.Kitchen.Extension); }
        catch (Exception ex) { ShowError("Could not open", ex.Message); return; }
        if (path is null) return;   // cancelled

        try { _kitchen!.Open(path); }
        catch (Exception ex) { ShowError("Could not open", ex.Message); }
    }

    /// <summary>Leaves the open Kitchen without opening another. The counterpart to
    /// <see cref="OpenKitchenCommand"/>: a workspace you can enter and never leave is a trap, and
    /// <c>IKitchenSession.Close</c> otherwise had no caller anywhere in the app.</summary>
    [RelayCommand(CanExecute = nameof(HasKitchen))]
    private void CloseKitchen() => _kitchen!.Close();

    /// <summary>The open Kitchen's name, or null. Drives the Landing row that reports which workspace
    /// loose saves will default into — the titlebar chip only exists once a CookBook is open, so
    /// without this the Landing screen could not say which Kitchen was active.</summary>
    public string? KitchenName => _kitchen?.Current?.Manifest.Name;

    /// <summary>Whether a Kitchen is open.</summary>
    public bool HasKitchen => _kitchen?.Current is not null;

    /// <summary>Same rule the other wizards derive ids by: lower-case, spaces to dashes.</summary>
    private static string DeriveId(string name) => string.Join('-',
        name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    [RelayCommand] private void NewRecipe() => _dialogs.ShowAsync<object>(new NewRecipeViewModel(_dialogs, _notify));
    [RelayCommand]
    private async Task NewIngredient()
    {
        var wizard = new NewIngredientViewModel(_dialogs, _notify) { Destination = RecipeDestination.LooseKitchen };
        var result = await _dialogs.ShowAsync<NewIngredientViewModel>(wizard);
        if (result is null) return;   // cancelled

        if (result.Destination == RecipeDestination.IntoCookBook)
        {
            ShowError("No cookbook open", "Open or create a cookbook, then add ingredients from the Explorer.");
            return;
        }
        if (!result.TryGetCanvas(out var canvas))
        {
            ShowError("Invalid canvas", "Enter a canvas size like 512x512.");
            return;
        }
        var path = await _picker.SaveFileAsync("Save new ingredient", ".igt");
        if (path is null) return;   // cancelled the picker

        LoadedIngredient built;
        try { built = result.Build(canvas); }   // Build allocates the raster — guard it (OOM on a huge canvas)
        catch (Exception ex) { ShowError("Could not save", ex.Message); return; }

        try
        {
            // Validate + atomically write (replaces an existing file rather than throwing).
            var problems = LooseWorkspace.WriteIngredient(path, built);
            if (problems.Count > 0)
            {
                ShowError("Invalid ingredient", string.Join("\n", problems));
                return;
            }
        }
        catch (Exception ex) { ShowError("Could not save", ex.Message); return; }
        finally { built.Dispose(); }

        OpenLooseIngredient(path);   // B1: reads it back + opens the editor with a loose-save path
    }

    [RelayCommand]
    private async Task OpenCookBook()
    {
        var path = await _picker.OpenFileAsync("Open CookBook", ".cbk");
        if (path is null) return;
        OpenPath(path);
    }

    [RelayCommand]
    private async Task Import()
    {
        var path = await _picker.OpenFileAsync("Import", ".cbk", ".rcp", ".igt");
        if (path is null) return;
        ArchiveKind kind;
        try { kind = Archives.KindOf(path); }
        catch (Exception ex) { ShowError("Could not import", ex.Message); return; }

        if (kind == ArchiveKind.CookBook) { OpenPath(path); return; }
        if (kind == ArchiveKind.Ingredient) { OpenLooseIngredient(path); return; }
        if (kind == ArchiveKind.Recipe) { OpenLooseRecipe(path); return; }
        _notify.Report("This file type can't be imported.");   // guard (unreachable for the three known kinds)
    }

    private void OpenLooseRecipe(string path)
    {
        LoadedRecipe recipe;
        try { recipe = RecipeArchive.Read(path); }
        catch (Exception ex) { ShowError("Could not open", ex.Message); return; }
        var book = LooseWorkspace.WrapRecipe(recipe);
        _session.Open(book, null);            // no source .cbk → the Explorer is read-only; session owns `book`
        _nav.To(_explorerFactory(book));
        RecordRecent(new RecentItem(recipe.Manifest.Name, $"loose recipe · {recipe.Ingredients.Count} ingredients", path, true));
    }

    private void OpenLooseIngredient(string path)
    {
        LoadedIngredient ing;
        try { ing = IngredientArchive.Read(path); }
        catch (Exception ex) { ShowError("Could not open", ex.Message); return; }
        if (ing.VariantImages.Count == 0)
        {
            ShowError("Can't open", "This ingredient has no variants to edit.");
            ing.Dispose(); return;
        }
        var book = LooseWorkspace.WrapIngredient(ing);   // the editor owns + disposes this
        // The loose-editor factory records the recent itself (see ServiceRegistration) so that every
        // route into a loose editor does, not just this one.
        _nav.To(_looseEditorFactory(ing, book, path));
        OnPropertyChanged(nameof(Recents));
        OnPropertyChanged(nameof(HasNoRecents));
    }

    private void OpenPath(string path)
    {
        LoadedCookBook book;
        try { book = CookBookArchive.Read(path); }
        catch (Exception ex) { ShowError("Could not open", ex.Message); return; }
        _session.Open(book, path);
        _nav.To(_explorerFactory(book));
        RecordRecent(new RecentItem(book.Manifest.Name,
            $"{book.Recipes.Count} recipes · {book.Manifest.Canvas.Width}×{book.Manifest.Canvas.Height}", path, false));
    }

    private void ShowError(string title, string message) =>
        _dialogs.ShowAsync<object>(new ErrorDialogViewModel(_dialogs, title, message));

    [RelayCommand]
    private async Task OpenSet()
    {
        var path = await _picker.OpenFileAsync("Open a cooked .set", ".set");
        if (path is null) return;
        OpenSetPath(path);
    }

    private void OpenSetPath(string path)
    {
        LoadedSet set;
        try { set = SetReader.Read(path); }
        catch (Exception ex)
        {
            ShowError("Could not open the set", ex.Message);
            return;
        }
        _nav.To(_setBrowserFactory(set));
        RecordRecent(new RecentItem(set.Manifest.Name, $"set · {set.Manifest.Count} assets", path, false));
    }

    private void RecordRecent(RecentItem item)
    {
        _recents.Add(item);
        OnPropertyChanged(nameof(Recents));
        OnPropertyChanged(nameof(HasNoRecents));
    }

    [RelayCommand]
    private void OpenRecent(RecentItem item)
    {
        if (!File.Exists(item.Path) && !Directory.Exists(item.Path))   // a Set may be a folder
        {
            _recents.Remove(item.Path);
            OnPropertyChanged(nameof(Recents));
            OnPropertyChanged(nameof(HasNoRecents));
            ShowError("Missing file", $"“{item.Path}” is no longer there, so it was removed from Recents.");
            return;
        }
        if (string.Equals(Path.GetExtension(item.Path), ".set", StringComparison.OrdinalIgnoreCase))
        { OpenSetPath(item.Path); return; }

        ArchiveKind kind;
        try { kind = Archives.KindOf(item.Path); }
        catch (Exception ex) { ShowError("Can't open", ex.Message); return; }
        switch (kind)
        {
            case ArchiveKind.CookBook: OpenPath(item.Path); return;
            case ArchiveKind.Ingredient: OpenLooseIngredient(item.Path); return;
            case ArchiveKind.Recipe: OpenLooseRecipe(item.Path); return;
        }
    }

    [RelayCommand] private void ShowHelp() => _dialogs.ShowAsync<object>(new HelpViewModel(_dialogs));
}
