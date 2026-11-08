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

    /// <summary>Creates the host with a stated way to find the opening window.</summary>
    /// <param name="picker">Save dialogs.</param>
    /// <param name="clipboard">The clipboard.</param>
    /// <param name="state">The remembered window size.</param>
    /// <param name="bridge">Turns art into bitmaps.</param>
    /// <param name="mainWindow">Finds a live opening window, or null to use the native opening display.
    /// The default host prefers the active viewer, then the shell, then any visible window.</param>
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
