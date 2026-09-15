using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Nfty.App.Views;

/// <summary>The validation dialog view. Code-behind is limited to loading the XAML; everything
/// else is bound.</summary>
public partial class ValidityDialogView : UserControl
{
    /// <summary>Loads the view.</summary>
    public ValidityDialogView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
