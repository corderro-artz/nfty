using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Nfty.App.Views;

/// <summary>
/// The product's mark at tile size: the application icon's layer stack, drawn from theme brushes so
/// one drawing serves light and dark. Used by the titlebar and by the quick-reference sheet - see
/// the comment in BrandMarkView.axaml for why it is a control rather than two copies of the markup.
/// </summary>
public partial class BrandMarkView : UserControl
{
    /// <summary>Loads the view.</summary>
    public BrandMarkView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
