using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace Nfty.Desktop;

/// <summary>Finds a live window for native services even after the shell has closed.</summary>
internal static class DesktopWindowContext
{
    internal static Window? Current
    {
        get
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                return null;
            return desktop.Windows.FirstOrDefault(w => w.IsVisible && w.IsActive)
                ?? (desktop.MainWindow is { IsVisible: true } main ? main : null)
                ?? desktop.Windows.FirstOrDefault(w => w.IsVisible);
        }
    }
}
