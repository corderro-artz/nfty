using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Nfty.App.Views;

/// <summary>The viewer's 2D / Both / 3D switch, shared by the overlay header and the window titlebar.</summary>
public partial class ViewerLayoutSwitch : UserControl
{
    /// <summary>Whether the word LAYOUT is drawn before the switch. The titlebar leaves it out: the
    /// segments name themselves and the width is the name's.</summary>
    public static readonly StyledProperty<bool> ShowLabelProperty =
        AvaloniaProperty.Register<ViewerLayoutSwitch, bool>(nameof(ShowLabel), true);

    /// <summary>Loads the view.</summary>
    public ViewerLayoutSwitch() => AvaloniaXamlLoader.Load(this);

    /// <summary>Whether the word LAYOUT is drawn before the switch.</summary>
    public bool ShowLabel { get => GetValue(ShowLabelProperty); set => SetValue(ShowLabelProperty, value); }
}
