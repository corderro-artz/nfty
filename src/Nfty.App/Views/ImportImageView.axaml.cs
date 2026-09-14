using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Nfty.App.Views;

/// <summary>The import-an-image-as-an-ingredient form. Code-behind loads the XAML and takes focus;
/// everything else is bound.</summary>
public partial class ImportImageView : UserControl
{
    /// <summary>Loads the view.</summary>
    public ImportImageView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>Takes initial focus once the view is on screen.</summary>
    /// <param name="e">Ignored beyond forwarding to the base implementation.</param>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Focus();
    }
}
