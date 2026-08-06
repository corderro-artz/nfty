using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Nfty.App.Views;

/// <summary>The cook book detail view. Code-behind is limited to loading the XAML and the few
/// interactions that genuinely need a control reference; everything else is bound.</summary>
public partial class CookBookDetailView : UserControl
{
    /// <summary>Loads the view.</summary>
    public CookBookDetailView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
