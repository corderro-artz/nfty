using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Focus();
    }

    // Ctrl+K can't be a <KeyBinding Command="..."/> because focusing a control isn't a command — it
    // has to happen here, where the actual TextBox instance is reachable.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.K && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            this.FindControl<TextBox>("SearchBox")?.Focus();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }
}
