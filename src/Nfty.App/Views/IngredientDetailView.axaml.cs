using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Nfty.App.Views;

/// <summary>The ingredient detail view. Code-behind is limited to loading the XAML and the few
/// interactions that genuinely need a control reference; everything else is bound.</summary>
public partial class IngredientDetailView : UserControl
{
    /// <summary>Loads the view.</summary>
    public IngredientDetailView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
