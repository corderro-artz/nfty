using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Nfty.App.Views;

/// <summary>The Menu export card. Code-behind is limited to loading the XAML; everything else is bound.</summary>
public partial class MenuExportDialogView : UserControl
{
    /// <summary>Loads the view.</summary>
    public MenuExportDialogView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
