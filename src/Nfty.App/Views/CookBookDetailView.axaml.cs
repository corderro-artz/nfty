using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Nfty.App.Views;

public partial class CookBookDetailView : UserControl
{
    public CookBookDetailView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
