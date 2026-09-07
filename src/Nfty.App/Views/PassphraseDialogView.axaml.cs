using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Nfty.App.Views;

/// <summary>The passphrase prompt for a sealed export. Everything is bound.</summary>
public partial class PassphraseDialogView : UserControl
{
    /// <summary>Loads the view.</summary>
    public PassphraseDialogView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
