using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Nfty.App.Models;
using Nfty.App.ViewModels;

namespace Nfty.App.Views;

/// <summary>The explorer view. Code-behind is limited to loading the XAML and the few
/// interactions that genuinely need a control reference; everything else is bound.</summary>
public partial class ExplorerView : UserControl
{
    /// <summary>Loads the view.</summary>
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

    /// <summary>Takes initial focus once the view is on screen.</summary>
    /// <param name="e">Ignored beyond forwarding to the base implementation.</param>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Focus();
    }

    // Ctrl+K can't be a <KeyBinding Command="..."/> because focusing a control isn't a command — it
    // has to happen here, where the actual TextBox instance is reachable.
    /// <summary>Handles Ctrl+K, which focuses the search box. In code-behind because focusing a
    /// specific control is not something a ViewModel can express.</summary>
    /// <param name="e">The key event; marked handled when the gesture matches.</param>
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
