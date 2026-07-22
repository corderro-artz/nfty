using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

public partial class IngredientDetailViewModel : ViewModelBase
{
    private readonly INotYetWired _notify;
    private readonly Action _editIngredient;
    private readonly Func<bool> _isEditing;
    [ObservableProperty] private string _sortColumn = "Variant";

    public IngredientDetailViewModel(INotYetWired notify, Action editIngredient, Func<bool> isEditing)
    { _notify = notify; _editIngredient = editIngredient; _isEditing = isEditing; }

    public void RaiseCanExecuteChanged() => DeleteVariantCommand.NotifyCanExecuteChanged();

    [RelayCommand] private void SortBy(string col) => SortColumn = col;
    [RelayCommand] private void SelectVariant(string id) { /* ui-state: active variant */ }
    [RelayCommand(CanExecute = nameof(CanEdit))] private void DeleteVariant() => _notify.Report("Delete variant");
    [RelayCommand] private void JumpToRules() { /* nav within the recipe rail */ }
    [RelayCommand] private void EditIngredient() => _editIngredient();

    private bool CanEdit() => _isEditing();
}
