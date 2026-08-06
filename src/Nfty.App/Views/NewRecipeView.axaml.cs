using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Nfty.App.Views;

/// <summary>The new recipe view. Code-behind is limited to loading the XAML and the few
/// interactions that genuinely need a control reference; everything else is bound.</summary>
public partial class NewRecipeView : UserControl
{
    /// <summary>Loads the view.</summary>
    public NewRecipeView()
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
