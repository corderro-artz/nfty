using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Nfty.App.Views;

/// <summary>The color-save dialog view. Code-behind is limited to loading the XAML; everything
/// else is bound.</summary>
public partial class ColorSaveDialogView : UserControl
{
    /// <summary>Loads the view.</summary>
    public ColorSaveDialogView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
