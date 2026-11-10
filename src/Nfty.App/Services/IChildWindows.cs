namespace Nfty.App.Services;

/// <summary>One open secondary window (a pop-out viewer, a gallery) the shell can bring forward.</summary>
public sealed class ChildWindowEntry
{
    /// <summary>The window's title now; a function because a viewer's title can change.</summary>
    public required Func<string> Title { get; init; }

    /// <summary>A short hint beside the title (<c>live</c>, <c>snapshot</c>), or null.</summary>
    public Func<string?>? Hint { get; init; }

    /// <summary>Raises the window above everything else, restoring it if minimized.</summary>
    public required Action Activate { get; init; }

    /// <summary>Whether the window is up in front of the reader (not minimized, and on top or the one just
    /// left by the click on the badge). Null = unknown, which the badge reads as "not shown".</summary>
    public Func<bool>? IsShown { get; init; }

    /// <summary>Puts the window away (minimizes it), or null when the head cannot.</summary>
    public Action? Hide { get; init; }
}

/// <summary>
/// The open secondary windows, so the main window can list them and bring one forward. Needed most
/// in full screen, where nothing on the desktop can reach a window hidden behind the shell.
/// </summary>
/// <remarks>A head with no second window (a single-view platform) never registers anything and the
/// shell's badge stays empty.</remarks>
public interface IChildWindows
{
    /// <summary>The windows open now, oldest first.</summary>
    IReadOnlyList<ChildWindowEntry> Windows { get; }

    /// <summary>Raised when a window is added or removed.</summary>
    event Action? Changed;

    /// <summary>Adds a window; dispose the result when the window closes.</summary>
    /// <param name="entry">The window.</param>
    /// <returns>Removes the entry again.</returns>
    IDisposable Register(ChildWindowEntry entry);

    /// <summary>Brings the next window forward, cycling in the order of <see cref="Windows"/>.</summary>
    /// <returns>The window activated, or null when none is open.</returns>
    ChildWindowEntry? ActivateNext();

    /// <summary>The window used last: the one most recently raised or focused, else the newest.</summary>
    ChildWindowEntry? Last { get; }

    /// <summary>Records that a window was just raised or focused, so it becomes <see cref="Last"/>.</summary>
    /// <param name="entry">The window.</param>
    void Touch(ChildWindowEntry entry);

    /// <summary>The badge's left click: puts <see cref="Last"/> away when it is shown, else raises it.</summary>
    /// <returns>The window toggled, or null when none is open.</returns>
    ChildWindowEntry? ToggleLast();
}

/// <inheritdoc cref="IChildWindows"/>
public sealed class ChildWindows : IChildWindows
{
    private readonly List<ChildWindowEntry> _windows = [];
    private int _next;
    private ChildWindowEntry? _last;

    /// <inheritdoc />
    public ChildWindowEntry? Last => _last is not null && _windows.Contains(_last) ? _last : _windows.LastOrDefault();

    /// <inheritdoc />
    public void Touch(ChildWindowEntry entry)
    {
        if (_windows.Contains(entry)) _last = entry;
    }

    /// <inheritdoc />
    public ChildWindowEntry? ToggleLast()
    {
        if (Last is not { } entry) return null;
        if (entry.Hide is not null && entry.IsShown?.Invoke() == true) entry.Hide();
        else { entry.Activate(); _last = entry; }
        return entry;
    }

    /// <inheritdoc />
    public IReadOnlyList<ChildWindowEntry> Windows => _windows;

    /// <inheritdoc />
    public event Action? Changed;

    /// <inheritdoc />
    public IDisposable Register(ChildWindowEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _windows.Add(entry);
        Changed?.Invoke();
        return new Removal(this, entry);
    }

    /// <inheritdoc />
    public ChildWindowEntry? ActivateNext()
    {
        if (_windows.Count == 0) return null;
        if (_next >= _windows.Count) _next = 0;
        var entry = _windows[_next];
        _next = (_next + 1) % _windows.Count;
        entry.Activate();
        _last = entry;
        return entry;
    }

    private sealed class Removal(ChildWindows owner, ChildWindowEntry entry) : IDisposable
    {
        private bool _done;
        public void Dispose()
        {
            if (_done) return;
            _done = true;
            if (owner._windows.Remove(entry)) owner.Changed?.Invoke();
        }
    }
}
