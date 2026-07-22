using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

public partial class RecipeDetailViewModel : ViewModelBase
{
    private readonly INotYetWired _notify;
    private readonly Action<string> _openIngredient;
    [ObservableProperty] private int _rollSeed = 1;

    public RecipeDetailViewModel(INotYetWired notify, Action<string> openIngredient)
    { _notify = notify; _openIngredient = openIngredient; }

    [RelayCommand] private void Reroll() => RollSeed++;   // ui-state; P2 samples a real colour
    [RelayCommand] private void OpenIngredient(string id) => _openIngredient(id);
}
