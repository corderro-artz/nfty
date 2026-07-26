using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Nfty.App.ViewModels;

namespace Nfty.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        var scrim = this.FindControl<Panel>("DialogScrim")!;
        scrim.PointerPressed += (sender, e) =>
        {
            // Only close when the click landed on the scrim itself, not bubbled up from the dialog content.
            if (e.Source == sender && DataContext is ShellViewModel shell)
                shell.CloseDialogCommand.Execute(null);
        };

        // Custom chrome (SystemDecorations=None): titlebar drags the window and double-click
        // toggles maximize; the corner grip resizes. The OS provides none of this here.
        this.FindControl<Grid>("Titlebar")!.PointerPressed += OnTitlebarPointerPressed;
        this.FindControl<Control>("ResizeGrip")!.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginResizeDrag(WindowEdge.SouthEast, e);
        };
    }

    private void OnTitlebarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        // Let the window-control buttons (min/max/close) handle their own clicks.
        if ((e.Source as Control)?.FindAncestorOfType<Button>(includeSelf: true) is not null) return;

        if (e.ClickCount == 2)
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else
            BeginMoveDrag(e);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ShellViewModel shell)
        {
            shell.MinimizeRequested += () => WindowState = WindowState.Minimized;
            shell.ToggleMaximizeRequested += () =>
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            shell.CloseRequested += Close;
        }
    }
}
