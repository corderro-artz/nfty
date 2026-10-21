using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Nfty.App.Views;

/// <summary>The three-way leave card. Everything is bound; code-behind only loads the XAML.</summary>
public partial class LeaveDialogView : UserControl
{
    /// <summary>Loads the view.</summary>
    public LeaveDialogView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
