namespace Nfty.App.Services;

/// <summary>Which of the resolution rules gave the store its home. Reported rather than inferred so
/// a panel can say <em>where</em> state is being kept, not just that it is.</summary>
public enum StoreLocation
{
    /// <summary>Rule 1 — beside the executable. The normal case for a downloaded, unzipped app.</summary>
    BesideExecutable,

    /// <summary>Rule 2 — in the current working directory.</summary>
    WorkingDirectory,

    /// <summary>Rule 3 — in the open Kitchen's folder.</summary>
    Kitchen,

    /// <summary>A folder the user picked, or one a caller pinned with <see cref="StateStore.At"/>.
    /// Not part of the discovery order: it is the exit from <see cref="InMemory"/>, and it wins for
    /// the rest of the session.</summary>
    Chosen,

    /// <summary>Rule 4 — nothing above was usable, so state lives in memory for this session only.
    /// A state with an exit, not an error: see <see cref="IStateStore.Choose"/>.</summary>
    InMemory,
}

/// <summary>Where the store ended up, and how it got there.</summary>
/// <param name="Location">Which rule produced it.</param>
/// <param name="Directory">The <c>.nfty</c> folder, or null when nothing on disk was usable.</param>
public sealed record StoreResolution(StoreLocation Location, string? Directory)
{
    /// <summary>Nothing is being persisted — this session's state is all there is.</summary>
    public bool IsInMemory => Directory is null;

    /// <summary>A sentence to show the user. The in-memory case says so plainly, and says what to do
    /// about it, because an app quietly failing to save is the outcome this whole path exists to
    /// prevent.</summary>
    public string Description => Location switch
    {
        StoreLocation.BesideExecutable => $"Saved beside the app, in {Directory}",
        StoreLocation.WorkingDirectory => $"Saved in the current folder, {Directory}",
        StoreLocation.Kitchen => $"Saved in this Kitchen, {Directory}",
        StoreLocation.Chosen => $"Saved in the folder you chose, {Directory}",
        _ => "Nowhere writable was found, so this is kept for this session only. "
             + "Choose a folder to keep it.",
    };
}

/// <summary>Whether a folder offered to <see cref="IStateStore.Choose"/> was taken, and if not, why
/// not — a refusal always carries its reason, because a choice accepted now and lost later is worse
/// than one refused at the point of choosing.</summary>
/// <param name="Accepted">Whether the store now lives there.</param>
/// <param name="Reason">Why it does not, or null when it does.</param>
public sealed record StoreChoice(bool Accepted, string? Reason);

/// <summary>
/// The <c>.nfty</c> folder — everything this app writes about itself, in the app's own space rather
/// than a per-user profile directory. It is downloaded and run, not installed, so nothing it writes
/// should leave the folder it was unzipped into. Dot-prefixed so it sorts to the top and reads as
/// "not for you", the convention <c>.git</c> already set.
///
/// <para><b>Its location is DISCOVERED, never recorded.</b> This is the rule <c>Kitchen</c> already
/// follows for membership, for the same reason: a recorded pointer goes stale the moment anything
/// moves, and then the app is lying about itself. First hit wins:</para>
///
/// <list type="number">
///   <item><description><c>.nfty/</c> beside the executable.</description></item>
///   <item><description><c>.nfty/</c> in the current working directory.</description></item>
///   <item><description><c>.nfty/</c> in the open Kitchen's folder, once a Kitchen is open.</description></item>
///   <item><description>In memory — and <see cref="StoreResolution.Description"/> says so.</description></item>
/// </list>
///
/// <para>An <em>existing</em> <c>.nfty/</c> is honoured even where a new one could not be created,
/// which is what lets one order answer both "where should I write?" and "where did I write last
/// time?" without a pointer file.</para>
///
/// <para>Everything here is convenience state: a corrupt file reads as absent, a failed save is
/// swallowed, and neither ever blocks or crashes the app — the discipline <c>RecentsService</c>
/// already applied, now applied to where the file lives as well as to what is in it.</para>
/// </summary>
public interface IStateStore
{
    /// <summary>Where state is being kept, and how that was decided.</summary>
    StoreResolution Resolution { get; }

    /// <summary>Raised when <see cref="Resolution"/> changes — a Kitchen opened into a writable
    /// folder, or the user chose one.</summary>
    event Action? Changed;

    /// <summary>Re-runs the discovery order. Called automatically when the open Kitchen changes, so
    /// rule 3 can take effect without anything having to remember to ask.</summary>
    void Resolve();

    /// <summary>
    /// Offers the store a folder to live in — the exit from
    /// <see cref="StoreLocation.InMemory"/>. An ordinary folder the user can already write to;
    /// nothing has to be created first, and this makes the <c>.nfty/</c> inside it and moves the
    /// session's state in.
    ///
    /// <para>Writability is settled by actually writing a file, not by inspecting attributes or the
    /// path — on Windows those lie often enough to matter, and a folder accepted on a guess loses
    /// the user's work silently at some later point.</para>
    /// </summary>
    /// <param name="directory">The folder to put <c>.nfty/</c> in.</param>
    /// <returns>Accepted, or refused with the reason.</returns>
    StoreChoice Choose(string directory);

    /// <summary>Reads a file out of the store.</summary>
    /// <param name="fileName">Name within <c>.nfty/</c>, e.g. <c>recents.json</c>.</param>
    /// <returns>Its contents, or null when it is absent or unreadable — the two are deliberately
    /// indistinguishable, because a caller can do nothing different about them.</returns>
    string? Read(string fileName);

    /// <summary>Writes a file into the store, or holds it in memory when there is nowhere to write.
    /// A failure is swallowed: this is convenience state and must never surface as an error.</summary>
    /// <param name="fileName">Name within <c>.nfty/</c>.</param>
    /// <param name="contents">What to write.</param>
    void Write(string fileName, string contents);
}

/// <inheritdoc cref="IStateStore"/>
public sealed class StateStore : IStateStore
{
    /// <summary>The folder's name.</summary>
    public const string FolderName = ".nfty";

    private readonly IKitchenSession? _kitchen;
    private readonly string? _beside;
    private readonly string? _working;
    private readonly Func<string, bool> _canCreate;

    /// <summary>State held for the session when there is nowhere to write it. Ordinal so the flush
    /// order is the same on every machine.</summary>
    private readonly Dictionary<string, string> _memory = new(StringComparer.Ordinal);

    private bool _pinned;

    /// <summary>Creates the store and resolves it immediately.</summary>
    /// <param name="kitchen">The open-Kitchen session, so rule 3 can apply and re-apply. Null is a
    /// normal state — nothing requires a Kitchen.</param>
    /// <param name="beside">Rule 1's root. Defaults to <see cref="AppContext.BaseDirectory"/>;
    /// tests MUST pass a temp directory, because the default is the folder the app was unzipped
    /// into and a test that wrote there would leave state in the developer's own build output.</param>
    /// <param name="working">Rule 2's root. Defaults to the process working directory, with the
    /// same warning.</param>
    public StateStore(IKitchenSession? kitchen = null, string? beside = null, string? working = null)
        : this(kitchen, beside, working, null) { }

    /// <summary>The real constructor. <paramref name="canCreate"/> is a test seam: it replaces the
    /// write-probe so a test can express "nothing here is writable" without ACL surgery, which is
    /// how the resolution ORDER gets tested. Production always passes null and probes for real, and
    /// <see cref="Choose"/> ignores the seam entirely — a refusal has to be a genuine one.</summary>
    internal StateStore(IKitchenSession? kitchen, string? beside, string? working,
        Func<string, bool>? canCreate)
    {
        _kitchen = kitchen;
        _beside = beside ?? AppContext.BaseDirectory;
        _working = working ?? Directory.GetCurrentDirectory();
        _canCreate = canCreate ?? DefaultCanCreate;
        if (_kitchen is not null) _kitchen.Changed += Resolve;
        Resolve();
    }

    private StateStore(string? directory)
    {
        _canCreate = DefaultCanCreate;
        Resolution = directory is null
            ? new StoreResolution(StoreLocation.InMemory, null)
            : new StoreResolution(StoreLocation.Chosen, Path.GetFullPath(directory));
        _pinned = true;
    }

    /// <summary>A store pinned to one folder, used exactly as given — no discovery, and no
    /// <c>.nfty</c> nesting, so the caller decides the whole path.
    ///
    /// <para>This is how a test keeps off both the real profile directory and the real
    /// <see cref="AppContext.BaseDirectory"/> in one move, and how a caller that already knows where
    /// state belongs skips the search.</para></summary>
    /// <param name="directory">The folder to use.</param>
    /// <returns>A store rooted there.</returns>
    public static StateStore At(string directory) => new(directory);

    /// <summary>A store that never touches the filesystem: everything written lives for the object's
    /// lifetime and no discovery runs.
    ///
    /// <para>Pinned, so a Kitchen opening later cannot promote it to a folder — a caller asking for
    /// memory is asking for isolation, not for a head start on finding one. This is what a component
    /// falls back to when it is constructed without a store, so a test can never reach the real one
    /// by omission.</para></summary>
    /// <returns>A store held entirely in memory.</returns>
    public static StateStore InMemory() => new((string?)null);

    /// <inheritdoc />
    public StoreResolution Resolution { get; private set; } = new(StoreLocation.InMemory, null);

    /// <inheritdoc />
    public event Action? Changed;

    /// <inheritdoc />
    public void Resolve()
    {
        // A pinned or chosen folder wins for the rest of the session. Without this, a Kitchen
        // opening after the user picked a folder would re-run the order, find rules 1-3 still
        // unusable, and throw their choice away — back to memory, with their swatches in it.
        if (_pinned) return;

        Adopt(
            Locate(_beside) is { } beside ? new StoreResolution(StoreLocation.BesideExecutable, beside)
            : Locate(_working) is { } working ? new StoreResolution(StoreLocation.WorkingDirectory, working)
            : Locate(_kitchen?.Current?.Directory) is { } kitchen ? new StoreResolution(StoreLocation.Kitchen, kitchen)
            : new StoreResolution(StoreLocation.InMemory, null));
    }

    /// <inheritdoc />
    public StoreChoice Choose(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return new StoreChoice(false, "No folder was chosen.");

        string dir;
        try { dir = Path.Combine(Path.GetFullPath(directory), FolderName); }
        catch (Exception ex) { return new StoreChoice(false, $"'{directory}' is not a usable path — {ex.Message}"); }

        // Deliberately no Directory.Exists / attribute check first: those are the path checks this
        // must not rely on. The probe is the whole test.
        if (TryCreate(dir) is { } reason)
            return new StoreChoice(false, $"'{directory}' cannot be written to — {reason}");

        _pinned = true;
        Adopt(new StoreResolution(StoreLocation.Chosen, dir));
        return new StoreChoice(true, null);
    }

    /// <inheritdoc />
    public string? Read(string fileName)
    {
        if (Resolution.Directory is not { } dir)
            return _memory.TryGetValue(fileName, out var held) ? held : null;
        try
        {
            var path = Path.Combine(dir, fileName);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch { return null; }   // unreadable is indistinguishable from absent, on purpose
    }

    /// <inheritdoc />
    public void Write(string fileName, string contents)
    {
        if (Resolution.Directory is not { } dir) { _memory[fileName] = contents; return; }
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, fileName), contents);
        }
        catch { /* convenience state — never surface */ }
    }

    /// <summary>Takes a new resolution, moving anything held in memory into it. The move is what
    /// makes the in-memory state an intermediate rather than a dead end: swatches saved before a
    /// folder existed end up in the folder the moment one does.</summary>
    private void Adopt(StoreResolution found)
    {
        if (found == Resolution) return;

        List<KeyValuePair<string, string>> moving = Resolution.IsInMemory && !found.IsInMemory
            ? _memory.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList()
            : [];

        Resolution = found;
        if (moving.Count > 0)
        {
            _memory.Clear();
            foreach (var (fileName, contents) in moving) Write(fileName, contents);
        }
        Changed?.Invoke();
    }

    /// <summary>The <c>.nfty</c> folder for a candidate root, or null when there is none and none
    /// can be made.</summary>
    private string? Locate(string? root)
    {
        if (string.IsNullOrWhiteSpace(root)) return null;

        string dir;
        try { dir = Path.Combine(Path.GetFullPath(root), FolderName); }
        catch { return null; }

        // Existence short-circuits the writability test, and that ORDER is the rule: a .nfty written
        // when the app sat somewhere writable has to still be found when it no longer is. Probing
        // first would silently relocate the store and lose everything already in it.
        if (Directory.Exists(dir)) return dir;

        return _canCreate(dir) ? dir : null;
    }

    private static bool DefaultCanCreate(string dir) => TryCreate(dir) is null;

    /// <summary>Creates the folder and puts a real file in it. Returns null on success, or why not.
    ///
    /// <para>Actually writing, rather than reading attributes or inspecting the path: on Windows a
    /// folder can look writable and refuse the write, and can look read-only and take it. The only
    /// question that matters is whether a file lands, so that is the question asked.</para></summary>
    private static string? TryCreate(string dir)
    {
        var created = false;
        try
        {
            created = !Directory.Exists(dir);
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, ".probe-" + Guid.NewGuid().ToString("n"));
            File.WriteAllText(probe, FolderName);
            File.Delete(probe);
            return null;
        }
        catch (Exception ex)
        {
            // A half-made .nfty left behind here would be HONOURED on the next launch by the
            // existence rule above, pinning the store to a folder it cannot write to — the one
            // failure the existence rule is not allowed to cause.
            if (created) { try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ } }
            return ex.Message;
        }
    }
}
