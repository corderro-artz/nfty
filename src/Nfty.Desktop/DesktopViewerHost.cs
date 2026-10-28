using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Nfty.App.Services;
using Nfty.App.Viewer;
using Nfty.App.ViewModels;

namespace Nfty.Desktop;

/// <summary>
/// Opens each pop-out viewer in its own operating-system window.
/// </summary>
/// <remarks>
/// <para><b>The window is NOT owned by the main window.</b> An owned window closes with its owner, and
/// a viewer is meant to outlive the shell: it is dragged to a second screen and left there. The
/// application keeps running until the LAST window closes (<see cref="App.Shutdown"/>), so closing
/// the main window leaves open viewers alone.</para>
/// <para>The main window is used for one thing only: to find the screens, when it still exists. If
/// it is gone, or was never there, the viewer asks its own platform for them and otherwise opens
/// centered.</para>
/// </remarks>
public sealed class DesktopViewerHost : IViewerHost
{
    private readonly IFilePickerService _picker;
    private readonly IClipboardService _clipboard;
    private readonly IViewerWindowState _state;
    private readonly IImageBridge _bridge;
    private readonly Func<Window?> _mainWindow;

    /// <summary>Creates the host.</summary>
    /// <param name="picker">Save dialogs.</param>
    /// <param name="clipboard">The clipboard.</param>
    /// <param name="state">The remembered window size.</param>
    /// <param name="bridge">Turns art into bitmaps.</param>
    public DesktopViewerHost(IFilePickerService picker, IClipboardService clipboard, IViewerWindowState state,
        IImageBridge bridge)
        : this(picker, clipboard, state, bridge, DefaultMainWindow)
    {
    }

    /// <summary>Creates the host with a stated way to find the main window, which a test supplies.</summary>
    /// <param name="picker">Save dialogs.</param>
    /// <param name="clipboard">The clipboard.</param>
    /// <param name="state">The remembered window size.</param>
    /// <param name="bridge">Turns art into bitmaps.</param>
    /// <param name="mainWindow">Finds the main window, or null when there is none.</param>
    public DesktopViewerHost(IFilePickerService picker, IClipboardService clipboard, IViewerWindowState state,
        IImageBridge bridge, Func<Window?> mainWindow)
    {
        _picker = picker;
        _clipboard = clipboard;
        _state = state;
        _bridge = bridge;
        _mainWindow = mainWindow;
    }

    /// <summary>Raised with each window just before it is shown, so a test can reach it.</summary>
    public event Action<ViewerWindow>? Opened;

    private static Window? DefaultMainWindow() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    /// <inheritdoc />
    public void Open(ViewerSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        var remembered = _state.Current;
        var dialogs = new DialogService();
        var vm = new ViewerViewModel(subject, _picker, _clipboard, dialogs, _bridge);
        if (Enum.TryParse<ViewerBackdrop>(remembered.Backdrop, out var backdrop)) vm.TwoD.Backdrop = backdrop;
        vm.TwoD.BackdropColor = remembered.BackdropColor;

        var entry = vm.Layout == ViewerLayout.TwoD ? remembered.TwoD : remembered.ThreeD;
        var window = new ViewerWindow(vm, dialogs, _state, entry);

        // A closed main window has no live screens to ask; the new window's own platform is the
        // fallback, and with neither the window simply opens centered.
        var main = _mainWindow();
        var screens = (main is { IsVisible: true } ? main.Screens : null) ?? window.Screens;
        window.Place(entry, screens?.All.Select(s => s.WorkingArea).ToList() ?? new List<PixelRect>());

        Opened?.Invoke(window);
        window.Show();
    }
}
