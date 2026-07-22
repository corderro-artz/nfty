using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Models;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

public partial class ExplorerViewModel : ViewModelBase
{
    private readonly INavigationService _nav;
    private readonly IDialogService _dialogs;
    private readonly INotYetWired _notify;

    [ObservableProperty] private ExplorerNode? _selectedNode;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    private bool _isEditing;
    [ObservableProperty] private ViewModelBase? _currentDetail;

    public ExplorerNode Root { get; } = Sample();

    public string AddLabel => SelectedNode?.Kind switch
    {
        ExplorerNodeKind.CookBook => "Add recipe",
        ExplorerNodeKind.Recipe => "Add ingredient",
        ExplorerNodeKind.Ingredient => "Add variant",
        _ => "Add",
    };

    public ExplorerViewModel(INavigationService nav, IDialogService dialogs, INotYetWired notify)
    { _nav = nav; _dialogs = dialogs; _notify = notify; }

    partial void OnSelectedNodeChanged(ExplorerNode? value)
    {
        OnPropertyChanged(nameof(AddLabel));
        CurrentDetail = value?.Kind switch
        {
            ExplorerNodeKind.CookBook => new CookBookDetailViewModel(_notify),
            ExplorerNodeKind.Recipe => new RecipeDetailViewModel(_notify, id => OpenIngredientCommand.Execute(id)),
            ExplorerNodeKind.Ingredient => new IngredientDetailViewModel(_notify,
                () => _notify.Report("Edit ingredient"),   // TODO(Task 13): _nav.To(new IngredientEditorViewModel(_nav, _notify))
                () => IsEditing),
            _ => null,
        };
    }

    [RelayCommand] private void ToggleLock() => IsEditing = !IsEditing;
    [RelayCommand] private void Search() => _notify.Report("Search (⌘K)");
    [RelayCommand] private void Add() => _notify.Report($"{AddLabel}");
    [RelayCommand(CanExecute = nameof(CanEdit))] private void DeleteSelected() => _notify.Report("Delete");
    [RelayCommand] private void Import() => _notify.Report("Import");
    [RelayCommand] private void SelectNode(ExplorerNode node) => SelectedNode = node;
    [RelayCommand] private void OpenIngredient(string id) => _notify.Report($"Open ingredient {id}");

    private bool CanEdit() => IsEditing;

    private static ExplorerNode Sample() =>
        new("cb", "VaporPets", ExplorerNodeKind.CookBook,
        [
            new("cat", "Cat", ExplorerNodeKind.Recipe,
            [
                new("bg", "Background", ExplorerNodeKind.Ingredient, []),
                new("aura", "Aura", ExplorerNodeKind.Ingredient, []),
            ]),
        ]);
}
