using CommunityToolkit.Mvvm.Input;
using Nfty.App.Models;
using Nfty.App.Services;
using Nfty.Core.Formats;
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

    public IReadOnlyList<RecentItem> Recents => _recents.Items;

    public LandingViewModel(INavigationService nav, IDialogService dialogs, INotYetWired notify,
        IFilePickerService picker, IRecentsService recents, ICookBookSession session,
        Func<LoadedCookBook, ExplorerViewModel> explorerFactory,
        Func<LoadedSet, SetBrowserViewModel> setBrowserFactory,
        Func<LoadedIngredient, LoadedCookBook, string, IngredientEditorViewModel> looseEditorFactory)
    {
        _nav = nav; _dialogs = dialogs; _notify = notify; _picker = picker; _recents = recents;
        _session = session; _explorerFactory = explorerFactory; _setBrowserFactory = setBrowserFactory;
        _looseEditorFactory = looseEditorFactory;
    }

    [RelayCommand] private void NewCookBook() => _dialogs.ShowAsync<object>(new NewCookBookViewModel(_dialogs, _notify));
    [RelayCommand(CanExecute = nameof(Never))] private void NewKitchen() => _notify.Report("New Kitchen");
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

        var built = result.Build(canvas);   // manifest + one blank variant (we own its images)
        try { IngredientArchive.Write(path, built.Manifest, built.VariantImages); }
        catch (Exception ex) { ShowError("Could not save", ex.Message); built.Dispose(); return; }
        built.Dispose();

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
        _nav.To(_looseEditorFactory(ing, book, path));
    }

    private void OpenPath(string path)
    {
        LoadedCookBook book;
        try { book = CookBookArchive.Read(path); }
        catch (Exception ex) { ShowError("Could not open", ex.Message); return; }
        _session.Open(book, path);
        _nav.To(_explorerFactory(book));
    }

    private void ShowError(string title, string message) =>
        _dialogs.ShowAsync<object>(new ErrorDialogViewModel(_dialogs, title, message));

    [RelayCommand]
    private async Task OpenSet()
    {
        var path = await _picker.OpenFileAsync("Open a cooked .set", ".set");
        if (path is null) return;
        LoadedSet set;
        try { set = SetReader.Read(path); }
        catch (Exception ex)
        {
            await _dialogs.ShowAsync<object>(new ErrorDialogViewModel(_dialogs, "Could not open the set", ex.Message));
            return;
        }
        _nav.To(_setBrowserFactory(set));
    }

    [RelayCommand] private void OpenRecent(RecentItem item) => _notify.Report($"Open recent: {item.Name}");
    [RelayCommand] private void ShowHelp() => _dialogs.ShowAsync<object>(new HelpViewModel(_dialogs));

    private bool Never() => false;
}
