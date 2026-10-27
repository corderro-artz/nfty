using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Nfty.App.Views;

/// <summary>The book settings card. Code-behind is limited to loading the XAML and taking focus.</summary>
public partial class BookSettingsDialogView : UserControl
{
    /// <summary>Loads the view.</summary>
    public BookSettingsDialogView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>Takes initial focus once the view is on screen.</summary>
    /// <param name="e">Forwarded to the base implementation.</param>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Focus();
    }
}
