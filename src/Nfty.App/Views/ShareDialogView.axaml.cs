using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Nfty.App.Views;

/// <summary>The community share card. Everything is bound; this only loads the XAML.</summary>
public partial class ShareDialogView : UserControl
{
    /// <summary>Loads the view.</summary>
    public ShareDialogView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
