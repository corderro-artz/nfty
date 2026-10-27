using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Nfty.App.Views;

/// <summary>The Settings card.</summary>
public partial class SettingsDialogView : UserControl
{
    /// <summary>Loads the view.</summary>
    public SettingsDialogView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
