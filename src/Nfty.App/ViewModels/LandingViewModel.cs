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
    [RelayCommand] private void NewIngredient() => _dialogs.ShowAsync<object>(new NewIngredientViewModel(_dialogs, _notify));

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
        _notify.Report("Importing a loose recipe needs the Kitchen (coming soon)");   // .rcp → later slice
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
