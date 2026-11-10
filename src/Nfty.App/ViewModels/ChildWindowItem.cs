using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

/// <summary>One row of the titlebar's other-windows menu: a window's title, its live/snapshot hint,
/// and the command that raises it.</summary>
public sealed partial class ChildWindowItem
{
    private readonly ChildWindowEntry _entry;
    private readonly Action? _activated;

    /// <summary>Wraps a registered window, reading its title and hint now.</summary>
    /// <param name="entry">The window.</param>
    /// <param name="activated">Called after the window is raised, so the menu can fold.</param>
    public ChildWindowItem(ChildWindowEntry entry, Action? activated = null)
    {
        _entry = entry;
        _activated = activated;
        Title = entry.Title();
        Hint = entry.Hint?.Invoke() ?? "";
    }

    /// <summary>The window's title.</summary>
    public string Title { get; }

    /// <summary>A short hint (<c>live</c>, <c>snapshot</c>), empty when there is none.</summary>
    public string Hint { get; }

    /// <summary>Raises the window above the shell.</summary>
    [RelayCommand]
    private void Activate()
    {
        _entry.Activate();
        _activated?.Invoke();
    }
}
