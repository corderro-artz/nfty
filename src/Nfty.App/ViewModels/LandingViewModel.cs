using CommunityToolkit.Mvvm.Input;
using Nfty.App.Models;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

/// <summary>The pre-open default screen: Create/Open groups plus a Recent list. Phase-1 wires the real
/// wizard dialogs (CookBook/Recipe/Ingredient) and the real Help dialog; the remaining actions
/// (Open CookBook, Import, New Kitchen, Open .set, Open recent) are not-yet-wired stubs.</summary>
public partial class LandingViewModel : ViewModelBase
{
    private readonly INavigationService _nav;
    private readonly IDialogService _dialogs;
    private readonly INotYetWired _notify;
    private readonly IFilePickerService _picker;
    private readonly IRecentsService _recents;

    public IReadOnlyList<RecentItem> Recents => _recents.Items;

    public LandingViewModel(INavigationService nav, IDialogService dialogs, INotYetWired notify,
        IFilePickerService picker, IRecentsService recents)
    { _nav = nav; _dialogs = dialogs; _notify = notify; _picker = picker; _recents = recents; }

    [RelayCommand] private void NewCookBook() => _dialogs.ShowAsync<object>(new NewCookBookViewModel(_dialogs, _notify));
    [RelayCommand(CanExecute = nameof(Never))] private void NewKitchen() => _notify.Report("New Kitchen");
    [RelayCommand] private void NewRecipe() => _dialogs.ShowAsync<object>(new NewRecipeViewModel(_dialogs, _notify));
    [RelayCommand] private void NewIngredient() => _dialogs.ShowAsync<object>(new NewIngredientViewModel(_dialogs, _notify));
    [RelayCommand] private void OpenCookBook() => _notify.Report("Open CookBook");
    [RelayCommand] private void Import() => _notify.Report("Import");
    [RelayCommand(CanExecute = nameof(Never))] private void OpenSet() => _notify.Report("Open .set");
    [RelayCommand] private void OpenRecent(RecentItem item) => _notify.Report($"Open recent: {item.Name}");
    [RelayCommand] private void ShowHelp() => _dialogs.ShowAsync<object>(new HelpViewModel(_dialogs));

    private bool Never() => false;
}
