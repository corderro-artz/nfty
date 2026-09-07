using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Nfty.App.Views;

/// <summary>The export dialog. Everything is bound; there is no code-behind interaction.</summary>
public partial class ExportDialogView : UserControl
{
    /// <summary>Loads the view.</summary>
    public ExportDialogView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
