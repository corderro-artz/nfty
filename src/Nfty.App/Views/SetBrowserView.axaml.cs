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

    private void OnTileClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SetBrowserViewModel vm && e.Source is Button { DataContext: SetItemRow row })
            vm.SelectedItem = row;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
