using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.Controls;
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
/// <para>New viewers center on the opening window's display when it is available, otherwise on the
/// native opening display with a primary-display fallback. Remembered sizes and maximized state
/// survive; old screen coordinates do not override centering.</para>
/// </remarks>
public sealed class DesktopViewerHost : IViewerHost
{
    private readonly IFilePickerService _picker;
    private readonly IClipboardService _clipboard;
    private readonly IViewerWindowState _state;
    private readonly IImageBridge _bridge;
    private readonly Func<Window?> _mainWindow;
    private readonly IChildWindows? _children;

    /// <summary>Creates the host.</summary>
    /// <param name="picker">Save dialogs.</param>
    /// <param name="clipboard">The clipboard.</param>
    /// <param name="state">The remembered window size.</param>
    /// <param name="bridge">Turns art into bitmaps.</param>
    /// <param name="children">The registry the shell lists open viewers from; null keeps none.</param>
    public DesktopViewerHost(IFilePickerService picker, IClipboardService clipboard, IViewerWindowState state,
        IImageBridge bridge, IChildWindows? children = null)
        : this(picker, clipboard, state, bridge, DefaultMainWindow, children)
    {
    }

    /// <summary>Creates the host with a stated way to find the opening window.</summary>
    /// <param name="picker">Save dialogs.</param>
    /// <param name="clipboard">The clipboard.</param>
    /// <param name="state">The remembered window size.</param>
    /// <param name="bridge">Turns art into bitmaps.</param>
    /// <param name="children">The registry the shell lists open viewers from; null keeps none.</param>
    /// <param name="mainWindow">Finds a live opening window, or null to use the native opening display.
    /// The default host prefers the active viewer, then the shell, then any visible window.</param>
    public DesktopViewerHost(IFilePickerService picker, IClipboardService clipboard, IViewerWindowState state,
        IImageBridge bridge, Func<Window?> mainWindow, IChildWindows? children = null)
    {
        _children = children;
        _picker = picker;
        _clipboard = clipboard;
        _state = state;
        _bridge = bridge;
        _mainWindow = mainWindow;
    }

    /// <summary>Raised with each window just before it is shown, so a test can reach it.</summary>
    public event Action<ViewerWindow>? Opened;

    /// <summary>
    /// A viewer opened over a full screen window stays above it.
    /// </summary>
    /// <remarks>Viewers are deliberately un-owned (an owned window dies with the shell), so nothing
    /// ties their z-order to the main window's. A full screen window covers the display and can sit
    /// above a window opened beside it, so while any window is full screen the new viewer is topmost,
    /// and goes back to an ordinary window once none is.</remarks>
    private static void KeepAboveFullScreen(Window viewer)
    {
        var windows = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows;
        if (windows is null || !windows.Any(w => !ReferenceEquals(w, viewer) && w.WindowState == WindowState.FullScreen))
            return;
        viewer.Topmost = true;
        viewer.Activate();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) =>
        {
            if (windows.Any(w => !ReferenceEquals(w, viewer) && w.WindowState == WindowState.FullScreen)) return;
            viewer.Topmost = false;
            timer.Stop();
        };
        viewer.Closed += (_, _) => timer.Stop();
        timer.Start();
    }

    /// <summary>Lists the viewer with the shell until it closes.</summary>
    private void Register(ViewerWindow window, ViewerViewModel vm)
    {
        if (_children is null) return;
        // "Shown" must survive the click on the badge itself: pressing it activates the MAIN window,
        // deactivating the viewer a moment before the command runs. A viewer that was in front until
        // just now still counts as in front, so the click puts it away instead of raising it again.
        var leftFront = DateTime.MinValue;
        window.Deactivated += (_, _) => leftFront = DateTime.UtcNow;
        var entry = new ChildWindowEntry
        {
            Title = () => string.IsNullOrWhiteSpace(vm.Title) ? "Viewer" : vm.Title,
            Hint = () => vm.LiveBadge,
            Activate = () =>
            {
                if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                window.Activate();
            },
            IsShown = () => window.WindowState != WindowState.Minimized
                && (window.IsActive || window.Topmost || DateTime.UtcNow - leftFront < TimeSpan.FromMilliseconds(600)),
            Hide = () => window.WindowState = WindowState.Minimized,
        };
        var token = _children.Register(entry);
        window.Activated += (_, _) => _children.Touch(entry);
        window.Closed += (_, _) => token.Dispose();
    }

    private static Window? DefaultMainWindow() => DesktopWindowContext.Current;

    /// <inheritdoc />
    public void Open(ViewerSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ViewerViewModel? vm = null;
        ViewerWindow? window = null;
        try
        {
            var remembered = _state.Current;
            var dialogs = new DialogService();
            // Tiles report into THIS window's layer: the shell or launch window may be gone.
            if (subject is GallerySubject g) g.Gallery.Dialogs = dialogs;
            vm = new ViewerViewModel(subject, _picker, _clipboard, dialogs, _bridge) { InWindow = true };
            if (Enum.TryParse<ViewerBackdrop>(remembered.Backdrop, out var backdrop)) vm.TwoD.Backdrop = backdrop;
            vm.TwoD.BackdropColor = remembered.BackdropColor;
            vm.Preview.Wireframe = remembered.Wireframe;

            var entry = vm.Layout == ViewerLayout.TwoD ? remembered.TwoD : remembered.ThreeD;
            window = new ViewerWindow(vm, dialogs, _state, entry);

            // CenterScreen calculates physical frame size at the opening screen's DPI before show.
            // Show(owner) would make the viewer die with the shell.
            window.Center(_mainWindow());

            Opened?.Invoke(window);
            window.Show();
            KeepAboveFullScreen(window);
            Register(window, vm);
        }
        catch
        {
            if (window is not null)
            {
                try { window.Close(); }
                finally { window.DisposeContent(); }
            }
            else
            {
                try { vm?.Dispose(); }
                finally { subject.Dispose(); }
            }
            throw;
        }
    }
}
