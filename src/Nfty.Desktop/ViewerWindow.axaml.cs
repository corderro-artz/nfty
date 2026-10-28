using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.App.Views;

namespace Nfty.Desktop;

/// <summary>
/// One pop-out viewer in its own operating-system window.
/// </summary>
/// <remarks>
/// <para><b>It owns what it shows.</b> The window disposes the view model and the subject when it
/// closes, so a host hands both over and forgets them.</para>
/// <para><b>It writes its size when it closes</b>, into the entry for the layout it is in (2D only, or
/// 2D and 3D), together with the backdrop the reader chose. It remembers the last NORMAL size and
/// position, so a window closed while maximized comes back with the size it had before it was
/// maximized, not the size of the screen.</para>
/// <para>Escape closes the window's open dialog and does nothing otherwise: a viewer holds nothing
/// unsaved, but a stray Escape must not make it vanish. Ctrl+W closes the window.</para>
/// </remarks>
public partial class ViewerWindow : Window
{
    private readonly ViewerViewModel _vm;
    private readonly DialogService _dialogs;
    private readonly IViewerWindowState _state;
    private Size _normalSize;
    private PixelPoint? _normalPosition;

    /// <summary>Exists only so Avalonia's runtime XAML loader and designers find a public constructor;
    /// a viewer without a subject has nothing to show.</summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public ViewerWindow() =>
        throw new NotSupportedException("A viewer window needs a view model; use DesktopViewerHost.");

    /// <summary>Builds the window; <see cref="DesktopViewerHost"/> normally does.</summary>
    /// <param name="vm">What the window shows; the window disposes it on close.</param>
    /// <param name="dialogs">The window's own dialog service, the one <paramref name="vm"/> raises to.</param>
    /// <param name="state">Where the size and backdrop are remembered.</param>
    /// <param name="entry">The remembered size to open at.</param>
    public ViewerWindow(ViewerViewModel vm, DialogService dialogs, IViewerWindowState state, ViewerWindowEntry entry)
    {
        AvaloniaXamlLoader.Load(this);
        _vm = vm;
        _dialogs = dialogs;
        _state = state;

        Title = vm.Subject.Title;
        Width = Math.Max(entry.Width, MinWidth);
        Height = Math.Max(entry.Height, MinHeight);
        _normalSize = new Size(Width, Height);
        if (entry.Maximized) WindowState = WindowState.Maximized;

        var layers = this.FindControl<Panel>("Layers")!;
        layers.Children.Insert(0, new ViewerView { DataContext = vm });

        var scrim = this.FindControl<Panel>("DialogScrim")!;
        var slot = this.FindControl<ContentControl>("DialogSlot")!;
        dialogs.Changed += () =>
        {
            slot.Content = dialogs.Active;
            scrim.IsVisible = dialogs.Active is not null;
        };

        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.W, KeyModifiers.Control),
            Command = new RelayCommand(Close),
        });
        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.Escape),
            Command = new RelayCommand(CloseOpenDialog),
        });

        Resized += (_, _) =>
        {
            if (WindowState == WindowState.Normal) _normalSize = ClientSize;
        };
        PositionChanged += (_, _) =>
        {
            if (WindowState == WindowState.Normal) _normalPosition = Position;
        };
    }

    /// <summary>The window's view model.</summary>
    public ViewerViewModel ViewModel => _vm;

    /// <summary>The window's own dialog service: the one its view model raises dialogs to.</summary>
    public DialogService Dialogs => _dialogs;

    /// <summary>
    /// Places the window at the remembered position if some screen still shows it, and otherwise
    /// asks for the center of the screen: an unplugged monitor cannot strand a window.
    /// </summary>
    /// <param name="entry">The remembered window.</param>
    /// <param name="workingAreas">The working area of every screen.</param>
    public void Place(ViewerWindowEntry entry, IReadOnlyList<PixelRect> workingAreas)
    {
        if (ViewerWindowPlacement.Resolve(entry, workingAreas) is { } at)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = at;
            _normalPosition = at;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    // Escape with no dialog open does nothing, deliberately: the key is not ours to spend on closing
    // a window that holds nothing unsaved but was opened on purpose.
    private void CloseOpenDialog()
    {
        if (_dialogs.Active is not null) _dialogs.Close(null);
    }

    /// <summary>Writes the size for the layout this window is in, then frees what it owns.</summary>
    /// <param name="e">The close request.</param>
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        try { Remember(); }
        catch (Exception) { /* convenience state: a failed save never stops a window closing */ }
        _vm.Dispose();
        _vm.Subject.Dispose();
    }

    private void Remember()
    {
        var current = _state.Current;
        var maximized = WindowState == WindowState.Maximized;
        var entry = new ViewerWindowEntry(_normalSize.Width, _normalSize.Height,
            _normalPosition?.X, _normalPosition?.Y, maximized);
        var twoD = _vm.Layout == ViewerLayout.TwoD;
        _state.Save(current with
        {
            TwoD = twoD ? entry : current.TwoD,
            ThreeD = twoD ? current.ThreeD : entry,
            Backdrop = _vm.TwoD.Backdrop.ToString(),
            BackdropColor = _vm.TwoD.BackdropColor,
        });
    }
}
