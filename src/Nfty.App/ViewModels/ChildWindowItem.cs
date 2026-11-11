using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

/// <summary>One row of the titlebar's other-windows menu: a window's title, its live/snapshot hint,
/// and the commands that raise, put away or close that one window.</summary>
public sealed partial class ChildWindowItem
{
    private readonly ChildWindowEntry _entry;
    private readonly Action? _activated;
    private readonly Action? _changed;

    /// <summary>Wraps a registered window, reading its title and hint now.</summary>
    /// <param name="entry">The window.</param>
    /// <param name="activated">Called after the window is raised, so the menu can fold.</param>
    /// <param name="changed">Called after the window was put away, so the row can redraw its state.</param>
    public ChildWindowItem(ChildWindowEntry entry, Action? activated = null, Action? changed = null)
    {
        _entry = entry;
        _activated = activated;
        _changed = changed;
        Title = entry.Title();
        Hint = entry.Hint?.Invoke() ?? "";
        IsMinimized = entry.IsMinimized?.Invoke() == true;
    }

    /// <summary>The window's title.</summary>
    public string Title { get; }

    /// <summary>A short hint (<c>live</c>, <c>snapshot</c>), empty when there is none.</summary>
    public string Hint { get; }

    /// <summary>Whether the window was minimized when the row was read; the row dims its title.</summary>
    public bool IsMinimized { get; }

    /// <summary>Whether the head can put this window away.</summary>
    public bool CanHide => _entry.Hide is not null;

    /// <summary>Whether the head can close this window.</summary>
    public bool CanClose => _entry.Close is not null;

    /// <summary>Raises the window above the shell, in the state it was last in.</summary>
    [RelayCommand]
    private void Activate()
    {
        _entry.Activate();
        _activated?.Invoke();
    }

    /// <summary>Puts this one window away (minimizes it); the others stay as they are.</summary>
    [RelayCommand]
    private void Hide()
    {
        _entry.Hide?.Invoke();
        _changed?.Invoke();
    }

    /// <summary>Closes this one window. The registry drops it when its window reports closed.</summary>
    [RelayCommand]
    private void Close() => _entry.Close?.Invoke();
}
