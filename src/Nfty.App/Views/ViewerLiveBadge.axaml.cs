using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Nfty.App.Views;

/// <summary>The viewer's live/snapshot badge, shared by the overlay header and the window titlebar.</summary>
public partial class ViewerLiveBadge : UserControl
{
    /// <summary>Loads the view.</summary>
    public ViewerLiveBadge() => AvaloniaXamlLoader.Load(this);
}
