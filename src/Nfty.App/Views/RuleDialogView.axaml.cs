using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Nfty.App.Views;

/// <summary>The add/edit-rule dialog view. Code-behind is limited to loading the XAML; everything
/// else is bound.</summary>
public partial class RuleDialogView : UserControl
{
    /// <summary>Loads the view.</summary>
    public RuleDialogView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
