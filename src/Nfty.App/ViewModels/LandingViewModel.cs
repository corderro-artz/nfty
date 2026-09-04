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
/// Explorer; Open .set reads a cooked Set and navigates to the Set browser; the Kitchen actions
/// open, create and leave a workspace through the <see cref="IKitchenSession"/>. Every action here
/// is wired — the summary described New Kitchen and Open-recent as stubs for some time after both
/// had shipped, which is the sort of comment that makes a reader distrust the rest of the file.</summary>
public partial class LandingViewModel : ViewModelBase
{
    private readonly INavigationService _nav;
    private readonly IDialogService _dialogs;
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

    /// <summary>Builds the start screen.</summary>
    /// <param name="nav">The page stack.</param>
    /// <param name="dialogs">The dialog layer.</param>
    /// <param name="picker">Chooses files to open, import or create.</param>
    /// <param name="recents">The persisted Recent list.</param>
    /// <param name="session">Takes ownership of whatever is opened.</param>
    /// <param name="explorerFactory">Opens a CookBook in the Explorer.</param>
    /// <param name="setBrowserFactory">Opens a cooked Set in the browser.</param>
    /// <param name="looseEditorFactory">Opens a loose Ingredient in the editor.</param>
    /// <param name="kitchen">The workspace session; null leaves the Kitchen actions unavailable.</param>
    public LandingViewModel(INavigationService nav, IDialogService dialogs,
        IFilePickerService picker, IRecentsService recents, ICookBookSession session,
        Func<LoadedCookBook, ExplorerViewModel> explorerFactory,
        Func<LoadedSet, SetBrowserViewModel> setBrowserFactory,
        Func<LoadedIngredient, LoadedCookBook, string, IngredientEditorViewModel> looseEditorFactory,
        IKitchenSession? kitchen = null)
    {
        _nav = nav; _dialogs = dialogs; _picker = picker; _recents = recents;
        _session = session; _explorerFactory = explorerFactory; _setBrowserFactory = setBrowserFactory;
        _looseEditorFactory = looseEditorFactory;
        _kitchen = kitchen;
        // Before the Changed subscription below, which loads it.
        KitchenShelf = new KitchenShelfViewModel(OpenKitchenCard);

        // The Kitchen can change from under this screen — NewKitchen and OpenKitchen both alter it,
        // and so does anything that closes one — so the row that names it, and CloseKitchen's own
        // availability, follow the session rather than a snapshot taken at construction.
        if (_kitchen is not null)
            _kitchen.Changed += () =>
            {
                OnPropertyChanged(nameof(KitchenName));
                OnPropertyChanged(nameof(HasKitchen));
                CloseKitchenCommand.NotifyCanExecuteChanged();
                LoadShelf();
            };

        LoadShelf();
    }

    /// <summary>
    /// The workspace shelf: the Kitchen's own contents, one row, paged by kind.
    /// </summary>
    /// <remarks>
    /// It exists whether or not a Kitchen is open — the band is a fixed part of this screen, and its
    /// three states swap the ink inside a box that never changes size. That is also what makes the
    /// Kitchen read as something the app always carries rather than something a CookBook has.
    /// </remarks>
    public KitchenShelfViewModel KitchenShelf { get; } = null!;

    /// <summary>Fills the shelf from the open workspace, or empties it when there is none.
    ///
    /// <para>Membership is DISCOVERED rather than recorded, so this re-reads whatever the session
    /// currently holds instead of trying to keep a list in step — the same rule
    /// <c>IKitchenSession.Rescan</c> exists to serve.</para></summary>
    private void LoadShelf()
    {
        var contents = _kitchen?.Current;
        KitchenShelf.Load(contents?.Manifest.Name,
            contents is null ? Array.Empty<KitchenCard>() : KitchenShelfViewModel.CardsFor(contents));
    }

    /// <summary>Opens whatever a shelf card stands for, through the routes this screen already has:
    /// a CookBook goes to the Explorer, a loose part to its editor. Same dispatch as Import, because
    /// it is the same question — what kind of archive is this?</summary>
    private void OpenKitchenCard(KitchenCard card)
    {
        switch (card.Kind)
        {
            case KitchenItemKind.CookBook: OpenPath(card.Path); break;
            case KitchenItemKind.Ingredient: OpenLooseIngredient(card.Path); break;
            case KitchenItemKind.Recipe: OpenLooseRecipe(card.Path); break;
        }
    }

    [RelayCommand]
    private async Task NewCookBook()
    {
        var wizard = new NewCookBookViewModel(_dialogs);
        var result = await _dialogs.ShowAsync<NewCookBookViewModel>(wizard);
        if (result is null) return;   // canceled
        if (string.IsNullOrWhiteSpace(result.DerivedId))
        {
            ShowError("Invalid cookbook", "The cookbook needs a name.");
            return;
        }
        string? path;
        try { path = await _picker.SaveFileAsync("Save new cookbook", ".cbk"); }
        catch (Exception ex) { ShowError("Could not save", ex.Message); return; }
        if (path is null) return;   // canceled the picker

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
        if (path is null) return;   // canceled

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
    /// one; a ViewModel constructed without it (some tests) keeps the old disabled behavior rather
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
        if (path is null) return;   // canceled

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

    /// <summary>Same rule the wizards derive ids by — now literally the same code.</summary>
    private static string DeriveId(string name) => WizardViewModelBase.DeriveId(name);
    /// <summary>
    /// Landing's "+ Recipe": a loose <c>.rcp</c>, saved and then opened.
    /// </summary>
    /// <remarks>
    /// This used to be <c>_dialogs.ShowAsync&lt;object&gt;(new NewRecipeViewModel(_dialogs))</c> - the
    /// wizard was shown and its result dropped on the floor, so the button collected a name, a
    /// weight and a destination and then did nothing at all. Its sibling "+ Ingredient" beside it
    /// was fully wired, which is what made the gap invisible: the two buttons look the same and only
    /// one of them worked. Found by driving, not by a test - nothing referenced the command.
    ///
    /// Shaped like <see cref="NewIngredient"/> on purpose, including forcing the destination:
    /// Landing has no CookBook open, so "This CookBook" has nothing to add to. The recipe is written
    /// EMPTY and not validated, exactly as the Explorer's own loose branch does it - a fresh recipe
    /// has no layers yet, and the user fills it in the editor next.
    /// </remarks>
    [RelayCommand]
    private async Task NewRecipe()
    {
        var wizard = new NewRecipeViewModel(_dialogs) { Destination = RecipeDestination.LooseKitchen };
        var result = await _dialogs.ShowAsync<NewRecipeViewModel>(wizard);
        if (result is null) return;   // canceled

        if (result.Destination == RecipeDestination.IntoCookBook)
        {
            ShowError("No cookbook open", "Open or create a cookbook, then add recipes from the Explorer.");
            return;
        }

        var path = await _picker.SaveFileAsync("Save new recipe", ".rcp");
        if (path is null) return;   // canceled the picker

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
                ShowError("Invalid recipe", string.Join(Environment.NewLine, problems));
                return;
            }
        }
        catch (Exception ex) { ShowError("Could not save", ex.Message); return; }

        OpenLooseRecipe(path);
    }
    [RelayCommand]
    private async Task NewIngredient()
    {
        var wizard = new NewIngredientViewModel(_dialogs) { Destination = RecipeDestination.LooseKitchen };
        var result = await _dialogs.ShowAsync<NewIngredientViewModel>(wizard);
        if (result is null) return;   // canceled

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
        if (path is null) return;   // canceled the picker

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

        // REACHABLE, and it used to lie about itself. There are FOUR known kinds, not three: the
        // picker is filtered to .cbk/.rcp/.igt but a typed filename is not, and Archives.KindOf
        // resolves a .ktn happily — so importing one fell through to the unbuilt-action channel, which
        // renders as "Not wired yet: …". That told the user a working feature was unbuilt, which is
        // the exact mistake IStatusService was split out to stop. A Kitchen is openable; just not
        // from here, so say which action does it.
        ShowError("That's a Kitchen",
            "A Kitchen (.ktn) is a workspace rather than something to import. Use “Open Kitchen…” to "
            + "open it, and its CookBooks and loose parts appear on the shelf below.");
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
        RecordRecent(new RecentItem(book.Manifest.Name, RecentMeta(book), path, false));
    }

    /// <summary>The subtitle a remembered CookBook carries. Singular where it should be: "1 recipes"
    /// was on the Landing screen every time a book had one.</summary>
    /// <param name="book">The book being remembered.</param>
    /// <returns>Its subtitle line.</returns>
    /// <remarks>Leads with the kind, as every other remembered thing does — "set · 300 assets",
    /// "loose recipe · 3 ingredients". A CookBook was the one row that did not, so the list read as
    /// three conventions instead of one, and "2 recipes · 512×512" beside "set · 300 assets" left
    /// the reader to infer which of the four kinds an unlabeled row was.</remarks>
    public static string RecentMeta(LoadedCookBook book) =>
        $"cookbook · {book.Recipes.Count} {(book.Recipes.Count == 1 ? "recipe" : "recipes")} · "
        + $"{book.Manifest.Canvas.Width}×{book.Manifest.Canvas.Height}";

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
