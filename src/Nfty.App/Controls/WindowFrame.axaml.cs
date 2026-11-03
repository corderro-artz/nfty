using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace Nfty.App.Controls;

/// <summary>
/// The window frame the shell and the pop-out viewer share: rounded border with a shadow gutter, the
/// titlebar (three slots and the caption buttons), a body, and an optional footer with a resize grip.
/// Head-agnostic: it raises <see cref="DragRequested"/>, <see cref="ToggleMaximizeRequested"/> and
/// <see cref="ResizeRequested"/> for the head to act on a Window with, and exposes the caption
/// buttons as commands.
/// </summary>
/// <remarks>
/// A slot is an ordinary property holding a control, so it binds against the DataContext the frame
/// inherits. The frame has no DataContext of its own and every binding inside it names the frame.
/// </remarks>
public partial class WindowFrame : UserControl
{
    /// <summary>The scale the frame's inside renders at.</summary>
    public static readonly StyledProperty<double> ScaleProperty =
        AvaloniaProperty.Register<WindowFrame, double>(nameof(Scale), 1.0);

    /// <summary>Titlebar content at the left edge (the brand).</summary>
    public static readonly StyledProperty<object?> TitleLeftProperty =
        AvaloniaProperty.Register<WindowFrame, object?>(nameof(TitleLeft));

    /// <summary>Titlebar content in the elastic middle.</summary>
    public static readonly StyledProperty<object?> TitleCenterProperty =
        AvaloniaProperty.Register<WindowFrame, object?>(nameof(TitleCenter));

    /// <summary>Titlebar content just left of the caption buttons.</summary>
    public static readonly StyledProperty<object?> TitleRightProperty =
        AvaloniaProperty.Register<WindowFrame, object?>(nameof(TitleRight));

    /// <summary>What fills the window under the titlebar.</summary>
    public static readonly StyledProperty<object?> BodyProperty =
        AvaloniaProperty.Register<WindowFrame, object?>(nameof(Body));

    /// <summary>A bar under the body (the shell's status bar); the resize grip overlays its corner.</summary>
    public static readonly StyledProperty<object?> FooterProperty =
        AvaloniaProperty.Register<WindowFrame, object?>(nameof(Footer));

    /// <summary>The minimise button's command.</summary>
    public static readonly StyledProperty<ICommand?> MinimizeCommandProperty =
        AvaloniaProperty.Register<WindowFrame, ICommand?>(nameof(MinimizeCommand));

    /// <summary>The maximise/restore button's command.</summary>
    public static readonly StyledProperty<ICommand?> ToggleMaximizeCommandProperty =
        AvaloniaProperty.Register<WindowFrame, ICommand?>(nameof(ToggleMaximizeCommand));

    /// <summary>The close button's command.</summary>
    public static readonly StyledProperty<ICommand?> CloseCommandProperty =
        AvaloniaProperty.Register<WindowFrame, ICommand?>(nameof(CloseCommand));

    /// <summary>Whether the corner grip is drawn.</summary>
    public static readonly StyledProperty<bool> ShowResizeGripProperty =
        AvaloniaProperty.Register<WindowFrame, bool>(nameof(ShowResizeGrip), true);

    /// <summary>Whether the four edges and corners resize the window.</summary>
    public static readonly StyledProperty<bool> EdgeResizeProperty =
        AvaloniaProperty.Register<WindowFrame, bool>(nameof(EdgeResize), false);

    /// <summary>Loads the frame.</summary>
    public WindowFrame()
    {
        AvaloniaXamlLoader.Load(this);

        TitlebarArea.PointerPressed += OnTitlebarPressed;
        ResizeGripArea.PointerPressed += (_, e) => RaiseResize(WindowEdge.SouthEast, e);
        foreach (var handle in this.FindControl<Grid>("Edges")!.Children.OfType<Border>())
        {
            var edge = Enum.Parse<WindowEdge>((string)handle.Tag!);
            handle.PointerPressed += (_, e) => RaiseResize(edge, e);
        }
    }

    /// <summary>The scale the frame's inside renders at.</summary>
    public double Scale { get => GetValue(ScaleProperty); set => SetValue(ScaleProperty, value); }
    /// <summary>Titlebar content at the left edge.</summary>
    public object? TitleLeft { get => GetValue(TitleLeftProperty); set => SetValue(TitleLeftProperty, value); }
    /// <summary>Titlebar content in the elastic middle.</summary>
    public object? TitleCenter { get => GetValue(TitleCenterProperty); set => SetValue(TitleCenterProperty, value); }
    /// <summary>Titlebar content just left of the caption buttons.</summary>
    public object? TitleRight { get => GetValue(TitleRightProperty); set => SetValue(TitleRightProperty, value); }
    /// <summary>What fills the window under the titlebar.</summary>
    public object? Body { get => GetValue(BodyProperty); set => SetValue(BodyProperty, value); }
    /// <summary>A bar under the body.</summary>
    public object? Footer { get => GetValue(FooterProperty); set => SetValue(FooterProperty, value); }
    /// <summary>The minimise button's command.</summary>
    public ICommand? MinimizeCommand { get => GetValue(MinimizeCommandProperty); set => SetValue(MinimizeCommandProperty, value); }
    /// <summary>The maximise/restore button's command.</summary>
    public ICommand? ToggleMaximizeCommand { get => GetValue(ToggleMaximizeCommandProperty); set => SetValue(ToggleMaximizeCommandProperty, value); }
    /// <summary>The close button's command.</summary>
    public ICommand? CloseCommand { get => GetValue(CloseCommandProperty); set => SetValue(CloseCommandProperty, value); }
    /// <summary>Whether the corner grip is drawn.</summary>
    public bool ShowResizeGrip { get => GetValue(ShowResizeGripProperty); set => SetValue(ShowResizeGripProperty, value); }
    /// <summary>Whether the four edges and corners resize the window.</summary>
    public bool EdgeResize { get => GetValue(EdgeResizeProperty); set => SetValue(EdgeResizeProperty, value); }

    /// <summary>The titlebar surface: drag to move, double-click to toggle maximize.</summary>
    public Control TitlebarArea => this.FindControl<Control>("Titlebar")!;

    /// <summary>The bottom-right resize grip.</summary>
    public Control ResizeGripArea => this.FindControl<Control>("ResizeGrip")!;

    /// <summary>Raised when a press on the titlebar should start moving the window.</summary>
    public event Action<PointerPressedEventArgs>? DragRequested;

    /// <summary>Raised when the titlebar is double-clicked.</summary>
    public event Action? ToggleMaximizeRequested;

    /// <summary>Raised when a press on the grip or an edge should start resizing the window.</summary>
    public event Action<WindowEdge, PointerPressedEventArgs>? ResizeRequested;

    private void RaiseResize(WindowEdge edge, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) ResizeRequested?.Invoke(edge, e);
    }

    private void OnTitlebarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        // A caption or content button handles its own click.
        if ((e.Source as Control)?.FindAncestorOfType<Button>(includeSelf: true) is not null) return;

        if (e.ClickCount == 2) ToggleMaximizeRequested?.Invoke();
        else DragRequested?.Invoke(e);
    }
}
