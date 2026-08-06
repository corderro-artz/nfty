using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Nfty.App.Views;

/// <summary>The help view. Code-behind is limited to loading the XAML and the few
/// interactions that genuinely need a control reference; everything else is bound.</summary>
public partial class HelpView : UserControl
{
    /// <summary>Loads the view.</summary>
    public HelpView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
