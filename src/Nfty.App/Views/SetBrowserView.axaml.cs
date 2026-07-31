using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Nfty.App.ViewModels;

namespace Nfty.App.Views;

public partial class SetBrowserView : UserControl
{
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
