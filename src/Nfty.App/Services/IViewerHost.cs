using Nfty.App.ViewModels;
using Nfty.App.Viewer;

namespace Nfty.App.Services;

/// <summary>
/// Opens the pop-out viewer for a subject. The seam between a page that wants to show something and
/// the head that knows what a window is.
/// </summary>
public interface IViewerHost
{
    /// <summary>Shows a viewer for <paramref name="subject"/>.</summary>
    /// <param name="subject">What to show. The host takes ownership and disposes it when the viewer
    /// closes, so a caller must never hand over an image it still uses.</param>
    void Open(ViewerSubject subject);
}

/// <summary>
/// The viewer for a head with no second window (a single-view platform): the same
/// <see cref="Views.ViewerView"/>, pushed full-screen into the shell's overlay slot above the dialog
/// layer, with a close button.
/// </summary>
/// <remarks>
/// One at a time: a second <see cref="Open"/> disposes the first and takes its place, because a
/// stack of full-screen overlays on a phone is a trap with no way to see what is underneath.
/// Remembered window size does not apply here: the overlay is as big as the screen.
/// </remarks>
public sealed class OverlayViewerHost : IViewerHost
{
    private readonly ShellViewModel _shell;
    private readonly IFilePickerService _picker;
    private readonly IClipboardService _clipboard;
    private readonly IDialogService _dialogs;
    private readonly IImageBridge _bridge;
    private ViewerViewModel? _current;

    /// <summary>Creates the host over the shell it draws into.</summary>
    /// <param name="shell">Owns the overlay slot.</param>
    /// <param name="picker">Save dialogs.</param>
    /// <param name="clipboard">The clipboard.</param>
    /// <param name="dialogs">The app-wide dialogs; the overlay shares the shell's dialog layer.</param>
    /// <param name="bridge">Turns art into bitmaps.</param>
    public OverlayViewerHost(ShellViewModel shell, IFilePickerService picker, IClipboardService clipboard,
        IDialogService dialogs, IImageBridge bridge)
    {
        _shell = shell;
        _picker = picker;
        _clipboard = clipboard;
        _dialogs = dialogs;
        _bridge = bridge;
    }

    /// <summary>The viewer currently shown, or null.</summary>
    public ViewerViewModel? Current => _current;

    /// <inheritdoc />
    public void Open(ViewerSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        Dismiss();
        var vm = new ViewerViewModel(subject, _picker, _clipboard, _dialogs, _bridge) { ShowClose = true };
        vm.CloseRequested += Dismiss;
        _current = vm;
        _shell.Overlay = vm;
    }

    private void Dismiss()
    {
        if (_current is not { } vm) return;
        _current = null;
        if (ReferenceEquals(_shell.Overlay, vm)) _shell.Overlay = null;
        vm.CloseRequested -= Dismiss;
        vm.Dispose();
        vm.Subject.Dispose();
    }
}
