using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Nfty.App.Views;

/// <summary>The recipe and ingredient name form.</summary>
public partial class ItemSettingsDialogView : UserControl
{
    /// <summary>Loads the form.</summary>
    public ItemSettingsDialogView() => AvaloniaXamlLoader.Load(this);
}
