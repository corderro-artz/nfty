using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Nfty.App.ViewModels;

namespace Nfty.App.Views;

/// <summary>The set browser view. Code-behind is limited to loading the XAML and the few
/// interactions that genuinely need a control reference; everything else is bound.</summary>
public partial class SetBrowserView : UserControl
{
    /// <summary>Loads the view.</summary>
    public SetBrowserView()
    {
        InitializeComponent();
        AddHandler(Button.ClickEvent, OnTileClick);
    }

    /// <summary>
    /// A tile click selects the asset and opens the inspector on it.
    ///
    /// <para>One bubbled handler rather than a command per tile: the grid is an ItemsControl of rows
    /// of tiles, so a per-tile binding would have to reach two DataContexts up to find the browser,
    /// and a <c>$parent[]</c> hop through a template is the fragile kind. The row's own DataContext
    /// is the asset, which is all this needs.</para>
    ///
    /// <para>The Save button in the rail is also a Button and also bubbles here — but its
    /// DataContext is the ViewModel, not a SetItemRow, so the pattern match skips it.</para>
    /// </summary>
    private void OnTileClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SetBrowserViewModel vm && e.Source is Button { DataContext: SetItemRow row })
            vm.InspectCommand.Execute(row);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
