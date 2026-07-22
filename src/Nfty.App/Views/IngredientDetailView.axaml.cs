using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Nfty.App.Views;

public partial class IngredientDetailView : UserControl
{
    public IngredientDetailView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
