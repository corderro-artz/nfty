using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Nfty.App.ViewModels;

namespace Nfty.App.Views;

/// <summary>
/// The shell chrome (frame + titlebar + status bar). Head-agnostic on purpose - see the comment in
/// ShellChromeView.axaml. The two properties below are the seam the desktop head wires window
/// behaviour to; a UserControl has its own name scope, so the host cannot reach these by name.
/// </summary>
/// <summary>The shell chrome view. Code-behind is limited to loading the XAML and the few
/// interactions that genuinely need a control reference; everything else is bound.</summary>
public partial class ShellChromeView : UserControl
{
    /// <summary>Loads the view.</summary>
    public ShellChromeView()
    {
        AvaloniaXamlLoader.Load(this);

        // Click-the-scrim-to-dismiss is ordinary UI behaviour rather than window behaviour, so it
        // stays here with the scrim itself instead of being reached into from the head.
        var scrim = this.FindControl<Panel>("DialogScrim")!;
        scrim.PointerPressed += (sender, e) =>
        {
            // Only close when the click landed on the scrim itself, not bubbled up from the dialog content.
            if (e.Source == sender && DataContext is ShellViewModel shell)
                shell.CloseDialogCommand.Execute(null);
        };
    }

    /// <summary>The titlebar surface: drag to move, double-click to toggle maximize.</summary>
    public Control TitlebarArea => this.FindControl<Control>("Titlebar")!;

    /// <summary>The bottom-right resize grip.</summary>
    public Control ResizeGripArea => this.FindControl<Control>("ResizeGrip")!;
}
