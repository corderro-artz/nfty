using Avalonia.Controls;
using Nfty.App.Services;
using Nfty.App.ViewModels;

namespace Nfty.Desktop;

/// <summary>Keeps a desktop dialog's work alive until cancellation has actually unwound.</summary>
internal sealed class DesktopDialogLifetime
{
    private readonly Window _window;
    private readonly IDialogService _dialogs;
    private Task? _closing;
    private bool _closeReady;

    internal DesktopDialogLifetime(Window window, IDialogService dialogs)
    {
        _window = window;
        _dialogs = dialogs;
        window.Closing += OnClosing;
    }

    /// <summary>Dismisses an ordinary card, or requests cancellation and waits for a running one.</summary>
    internal async Task DismissActiveAsync()
    {
        if (_dialogs.Active is not { } active) return;
        if (active is IUnsavedWork { IsBusy: true })
        {
            if (active is ICancellableDialog running) await running.CancelAndWaitAsync();
            return;
        }
        _dialogs.Close(active, null);
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeReady || !_dialogs.OpenDialogs.OfType<IUnsavedWork>().Any(d => d.IsBusy)) return;
        e.Cancel = true;
        if (_closing is not { IsCompleted: false }) _closing = CloseWhenReadyAsync();
    }

    private async Task CloseWhenReadyAsync()
    {
        while (_dialogs.OpenDialogs.OfType<IUnsavedWork>().FirstOrDefault(d => d.IsBusy) is { } busy)
        {
            // An unknown busy card can refuse close, but cannot be declared finished on its behalf.
            if (busy is not ICancellableDialog cancellable) return;
            await cancellable.CancelAndWaitAsync();
        }
        _closeReady = true;
        _window.Close();
    }
}
