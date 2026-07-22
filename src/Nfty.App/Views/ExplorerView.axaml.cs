using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Nfty.App.Models;
using Nfty.App.ViewModels;

namespace Nfty.App.Views;

public partial class ExplorerView : UserControl
{
    public ExplorerView()
    {
        InitializeComponent();
        var tree = this.FindControl<TreeView>("Tree")!;
        tree.SelectionChanged += (_, e) =>
        {
            if (DataContext is ExplorerViewModel vm && e.AddedItems.Count > 0 && e.AddedItems[0] is ExplorerNode node)
                vm.SelectNodeCommand.Execute(node);
        };
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
