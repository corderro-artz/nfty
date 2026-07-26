using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Models;
using Nfty.App.Services;
using Nfty.Core.Formats;

namespace Nfty.App.ViewModels;

public record Crumb(string Text, bool Active, bool Leading);

public partial class ExplorerViewModel : ViewModelBase, IDisposable
{
    private readonly INavigationService _nav;
    private readonly IDialogService _dialogs;
    private readonly INotYetWired _notify;
    private readonly IImageBridge _bridge;
    private readonly LoadedCookBook _book;
    private readonly Func<LoadedIngredient, LoadedRecipe, LoadedCookBook, IngredientEditorViewModel> _editorFactory;
    private readonly Func<LoadedCookBook, CookDialogViewModel> _cookFactory;

    [ObservableProperty] private ExplorerNode? _selectedNode;
    [ObservableProperty] private ViewModelBase? _currentDetail;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    private bool _isEditing;

    public ExplorerNode Root { get; }

    /// <summary>Wraps the single Root as a one-element sequence so a TreeView (which binds to a
    /// collection of roots) can display it.</summary>
    public IReadOnlyList<ExplorerNode> Roots => new[] { Root };

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
        Func<LoadedCookBook, CookDialogViewModel> cookFactory)
    {
        _book = book; _nav = nav; _dialogs = dialogs; _notify = notify; _bridge = bridge;
        _editorFactory = editorFactory;
        _cookFactory = cookFactory;
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
                    () => _nav.To(_editorFactory(i, r, _book)), () => IsEditing)
                : null,
            _ => null,
        };
        RebuildCrumbs();
    }

    [RelayCommand] private void ToggleLock() => IsEditing = !IsEditing;
    [RelayCommand] private void Search() => _notify.Report("Search (⌘K)");
    [RelayCommand] private void Add() => _notify.Report(AddLabel);
    [RelayCommand(CanExecute = nameof(CanEdit))] private void DeleteSelected() => _notify.Report("Delete");
    [RelayCommand] private void Import() => _notify.Report("Import");
    [RelayCommand] private void SelectNode(ExplorerNode node) => SelectedNode = node;
    [RelayCommand] private void OpenIngredient(string id) => _notify.Report($"Open ingredient {id}");
    private bool CanEdit() => IsEditing;

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
