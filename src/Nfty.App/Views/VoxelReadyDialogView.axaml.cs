using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Nfty.App.Views;

/// <summary>The Make voxel-ready preview card. Code-behind is limited to loading the XAML.</summary>
public partial class VoxelReadyDialogView : UserControl
{
    /// <summary>Loads the view.</summary>
    public VoxelReadyDialogView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
