using System.Diagnostics;
using Nfty.Core.Formats;

namespace Nfty.App.Services;

/// <summary>Which program a group of file types opens in, once registered.</summary>
public enum AssociationTarget
{
    /// <summary>Not registered: nfty is not offered for these files.</summary>
    Off,

    /// <summary>The nfty app: the file opens in the shell, as a Recent row would.</summary>
    App,

    /// <summary>The pop-out viewer alone, with no app shell.</summary>
    Viewer,
}

/// <summary>The three groups the Settings card offers, one choice each.</summary>
public enum AssociationGroup
{
    /// <summary>nfty's own archives: <c>.cbk</c>, <c>.rcp</c>, <c>.igt</c>, <c>.set</c>, <c>.tin</c>,
    /// <c>.ktn</c>, <c>.mnu</c>. No other program claims these extensions.</summary>
    NftyFiles,

    /// <summary>Pictures and 3D models: <c>.png</c>, <c>.jpg</c>, <c>.jpeg</c>, <c>.glb</c>, <c>.gltf</c>.
    /// Shared with every other image program; viewer only.</summary>
    PicturesAndModels,

    /// <summary>A Set's per-asset <c>.json</c> metadata. Shared with every program that reads JSON;
    /// viewer only.</summary>
    Metadata,
}

/// <summary>
/// One file type nfty can register for.
/// </summary>
/// <param name="Extension">The extension, with its dot, lower case.</param>
/// <param name="ProgId">The ProgID nfty registers it under; several extensions may share one.</param>
/// <param name="FriendlyName">What Explorer calls the type.</param>
/// <param name="Group">Which Settings choice governs it.</param>
/// <param name="NftyOwned">True for an extension no other program claims, whose default nfty may
/// set; false for a shared one, which Windows lets only the user make default.</param>
/// <param name="OpensInApp">True when the app shell opens it; false when only the viewer does (a
/// Menu), so the app target still registers the viewer command for it.</param>
public sealed record AssociatedType(string Extension, string ProgId, string FriendlyName,
    AssociationGroup Group, bool NftyOwned, bool OpensInApp);

/// <summary>
/// Every file type nfty registers for, and which group governs it. The inventory is the viewer's own
/// <see cref="Nfty.App.Viewer.ViewerOpener.Extensions"/> - every extension the viewer opens is here,
/// which a test holds - plus the routing the Landing page's open-by-path does for the app.
/// </summary>
public static class FileTypeCatalog
{
    /// <summary>Every registered type, in the order the Settings card lists them.</summary>
    public static readonly IReadOnlyList<AssociatedType> All = new AssociatedType[]
    {
        new(Archives.CookBookExtension, "nfty.CookBook", "nfty CookBook", AssociationGroup.NftyFiles, true, true),
        new(Archives.RecipeExtension, "nfty.Recipe", "nfty Recipe", AssociationGroup.NftyFiles, true, true),
        new(Archives.IngredientExtension, "nfty.Ingredient", "nfty Ingredient", AssociationGroup.NftyFiles, true, true),
        new(Archives.SetExtension, "nfty.Set", "nfty Set", AssociationGroup.NftyFiles, true, true),
        new(Archives.SealedExtension, "nfty.Sealed", "nfty sealed export", AssociationGroup.NftyFiles, true, true),
        new(Archives.KitchenExtension, "nfty.Kitchen", "nfty Kitchen", AssociationGroup.NftyFiles, true, true),
        new(Archives.MenuExtension, "nfty.Menu", "nfty Menu", AssociationGroup.NftyFiles, true, false),
        new(".png", "nfty.Viewer.Image", "Picture (nfty viewer)", AssociationGroup.PicturesAndModels, false, false),
        new(".jpg", "nfty.Viewer.Image", "Picture (nfty viewer)", AssociationGroup.PicturesAndModels, false, false),
        new(".jpeg", "nfty.Viewer.Image", "Picture (nfty viewer)", AssociationGroup.PicturesAndModels, false, false),
        new(".glb", "nfty.Viewer.Model", "3D model (nfty viewer)", AssociationGroup.PicturesAndModels, false, false),
        new(".gltf", "nfty.Viewer.Model", "3D model (nfty viewer)", AssociationGroup.PicturesAndModels, false, false),
        new(".json", "nfty.Viewer.Metadata", "Asset metadata (nfty viewer)", AssociationGroup.Metadata, false, false),
    };

    /// <summary>The types one group governs.</summary>
    /// <param name="group">The group.</param>
    /// <returns>Its types, in catalog order.</returns>
    public static IEnumerable<AssociatedType> Of(AssociationGroup group) => All.Where(t => t.Group == group);

    /// <summary>A group's extensions as one line: <c>.png .jpg .jpeg .glb .gltf</c>.</summary>
    /// <param name="group">The group.</param>
    /// <returns>The line.</returns>
    public static string ExtensionsOf(AssociationGroup group) => string.Join(" ", Of(group).Select(t => t.Extension));

    /// <summary>Whether a group may open in the app shell; the other two open in the viewer only.</summary>
    /// <param name="group">The group.</param>
    /// <returns>True for nfty's own archives.</returns>
    public static bool AllowsApp(AssociationGroup group) => group == AssociationGroup.NftyFiles;
}

/// <summary>What each group should open in.</summary>
/// <param name="NftyFiles">nfty's own archives.</param>
/// <param name="PicturesAndModels">Pictures and 3D models.</param>
/// <param name="Metadata">Asset metadata JSON.</param>
public sealed record AssociationChoice(AssociationTarget NftyFiles, AssociationTarget PicturesAndModels,
    AssociationTarget Metadata)
{
    /// <summary>Nothing registered.</summary>
    public static AssociationChoice None { get; } = new(AssociationTarget.Off, AssociationTarget.Off, AssociationTarget.Off);

    /// <summary>What the card proposes before anything is registered: nfty's files in the app,
    /// pictures and models in the viewer, JSON left alone (most JSON files are not asset metadata).</summary>
    public static AssociationChoice Suggested { get; } =
        new(AssociationTarget.App, AssociationTarget.Viewer, AssociationTarget.Off);

    /// <summary>One group's target.</summary>
    /// <param name="group">The group.</param>
    /// <returns>Its target.</returns>
    public AssociationTarget For(AssociationGroup group) => group switch
    {
        AssociationGroup.NftyFiles => NftyFiles,
        AssociationGroup.PicturesAndModels => PicturesAndModels,
        _ => Metadata,
    };

    /// <summary>Whether anything at all is chosen.</summary>
    public bool Any => NftyFiles != AssociationTarget.Off || PicturesAndModels != AssociationTarget.Off
                       || Metadata != AssociationTarget.Off;
}

/// <summary>How the registration on this machine reads.</summary>
public enum AssociationState
{
    /// <summary>Nothing of nfty's is registered.</summary>
    NotRegistered,

    /// <summary>Registered, complete, and pointing at this copy of nfty.</summary>
    Registered,

    /// <summary>Something is registered but incomplete, inconsistent, or pointing at another copy
    /// (the app was moved); Apply repairs it, Remove clears it.</summary>
    NeedsRepair,
}

/// <summary>The registration as read from the machine.</summary>
/// <param name="State">Registered, not, or broken.</param>
/// <param name="Current">What each group is registered to now (Off where nothing is).</param>
/// <param name="Detail">One sentence saying what was found.</param>
public sealed record AssociationStatus(AssociationState State, AssociationChoice Current, string Detail);

/// <summary>
/// Registers nfty as a program that opens its file types. Windows only; elsewhere
/// <see cref="IsSupported"/> is false and the Settings card does not show the page at all.
/// </summary>
public interface IFileAssociations
{
    /// <summary>Whether this platform has file associations nfty can write.</summary>
    bool IsSupported { get; }

    /// <summary>Reads what is registered now. Never writes.</summary>
    /// <returns>The state.</returns>
    AssociationStatus Inspect();

    /// <summary>Replaces whatever nfty registered with <paramref name="choice"/>, pointing at this copy.</summary>
    /// <param name="choice">What each group opens in; a group set to Off is not registered.</param>
    void Apply(AssociationChoice choice);

    /// <summary>Removes exactly what nfty registered, and nothing else.</summary>
    void Remove();

    /// <summary>Opens the system page where the user picks default programs.</summary>
    void OpenDefaultApps();
}

/// <summary>The platform with no associations nfty writes: every call is refused.</summary>
public sealed class UnsupportedFileAssociations : IFileAssociations
{
    /// <inheritdoc />
    public bool IsSupported => false;

    /// <inheritdoc />
    public AssociationStatus Inspect() =>
        new(AssociationState.NotRegistered, AssociationChoice.None, "File associations are a Windows setting.");

    /// <inheritdoc />
    public void Apply(AssociationChoice choice) =>
        throw new PlatformNotSupportedException("File associations are a Windows setting.");

    /// <inheritdoc />
    public void Remove() => throw new PlatformNotSupportedException("File associations are a Windows setting.");

    /// <inheritdoc />
    public void OpenDefaultApps() =>
        throw new PlatformNotSupportedException("File associations are a Windows setting.");
}

/// <summary>Where the registrar writes, relative to the current user's registry root. The real
/// layout is <see cref="Windows"/>; a test passes one rooted under a throwaway key.</summary>
/// <param name="Classes">The per-user classes root (<c>Software\Classes</c>).</param>
/// <param name="App">nfty's own key, holding <c>Capabilities</c> and the ledger (<c>Software\nfty</c>).</param>
/// <param name="RegisteredApplications">The key Default apps reads (<c>Software\RegisteredApplications</c>).</param>
public sealed record AssociationLayout(string Classes, string App, string RegisteredApplications)
{
    /// <summary>The name nfty registers under in <c>RegisteredApplications</c>, and the one
    /// <c>ms-settings:defaultapps?registeredAppUser=</c> takes.</summary>
    public const string AppName = "nfty";

    /// <summary>The real per-user layout. No key under HKEY_LOCAL_MACHINE: nothing needs admin.</summary>
    public static AssociationLayout Windows { get; } =
        new(@"Software\Classes", @"Software\nfty", @"Software\RegisteredApplications");

    /// <summary>The key holding the ledger of what nfty wrote.</summary>
    public string Ledger => App + @"\Associations";

    /// <summary>nfty's capabilities key.</summary>
    public string Capabilities => App + @"\Capabilities";
}

/// <summary>
/// The registry as the registrar needs it: string and multi-string values under the current user,
/// addressed by a path relative to that root. The value name <c>""</c> is a key's default value.
/// </summary>
public interface IRegistryHive
{
    /// <summary>Whether the key exists.</summary>
    /// <param name="key">The key path.</param>
    /// <returns>True when present.</returns>
    bool KeyExists(string key);

    /// <summary>Whether the key holds the named value.</summary>
    /// <param name="key">The key path.</param>
    /// <param name="name">The value name; <c>""</c> for the default.</param>
    /// <returns>True when present.</returns>
    bool HasValue(string key, string name);

    /// <summary>A string value, or null when the key or value is absent.</summary>
    /// <param name="key">The key path.</param>
    /// <param name="name">The value name; <c>""</c> for the default.</param>
    /// <returns>The value.</returns>
    string? GetString(string key, string name);

    /// <summary>Creates a key (and its parents) when absent.</summary>
    /// <param name="key">The key path.</param>
    void CreateKey(string key);

    /// <summary>Writes a string value, creating the key (and its parents) when absent.</summary>
    /// <param name="key">The key path.</param>
    /// <param name="name">The value name; <c>""</c> for the default.</param>
    /// <param name="value">The value.</param>
    void SetString(string key, string name, string value);

    /// <summary>A multi-string value; empty when absent.</summary>
    /// <param name="key">The key path.</param>
    /// <param name="name">The value name.</param>
    /// <returns>The strings.</returns>
    IReadOnlyList<string> GetMultiString(string key, string name);

    /// <summary>Writes a multi-string value, creating the key when absent.</summary>
    /// <param name="key">The key path.</param>
    /// <param name="name">The value name.</param>
    /// <param name="values">The strings.</param>
    void SetMultiString(string key, string name, IReadOnlyList<string> values);

    /// <summary>Deletes one value; nothing when absent.</summary>
    /// <param name="key">The key path.</param>
    /// <param name="name">The value name; <c>""</c> for the default.</param>
    void DeleteValue(string key, string name);

    /// <summary>Deletes a key that holds no values and no subkeys; a key that holds anything, or
    /// is absent, is left alone.</summary>
    /// <param name="key">The key path.</param>
    void DeleteKeyIfEmpty(string key);
}

/// <summary>A registry in memory, for tests and for reasoning about the registrar without a machine.</summary>
public sealed class InMemoryRegistryHive : IRegistryHive
{
    private readonly Dictionary<string, Dictionary<string, object>> _keys = new(StringComparer.OrdinalIgnoreCase);

    private static string Norm(string key) => key.Trim('\\');

    /// <summary>Every key present, for a test to inspect.</summary>
    public IEnumerable<string> Keys => _keys.Keys;

    /// <summary>Every key and value as sorted lines, so two states of the hive compare as text.</summary>
    /// <returns>One line per key, and one per value under it.</returns>
    public IReadOnlyList<string> Dump() => _keys
        .SelectMany(k => new[] { k.Key }.Concat(k.Value.Select(v =>
            $"{k.Key} | {v.Key} = {(v.Value is string[] a ? string.Join(";", a) : v.Value)}")))
        .Select(l => l.ToLowerInvariant())
        .OrderBy(l => l, StringComparer.Ordinal)
        .ToList();

    /// <inheritdoc />
    public bool KeyExists(string key) => _keys.ContainsKey(Norm(key));

    /// <inheritdoc />
    public bool HasValue(string key, string name) =>
        _keys.TryGetValue(Norm(key), out var v) && v.ContainsKey(name);

    /// <inheritdoc />
    public string? GetString(string key, string name) =>
        _keys.TryGetValue(Norm(key), out var v) && v.TryGetValue(name, out var o) ? o as string : null;

    /// <inheritdoc />
    public void CreateKey(string key) => Create(key);

    /// <inheritdoc />
    public void SetString(string key, string name, string value) => Create(key)[name] = value;

    /// <inheritdoc />
    public IReadOnlyList<string> GetMultiString(string key, string name) =>
        _keys.TryGetValue(Norm(key), out var v) && v.TryGetValue(name, out var o) && o is string[] s ? s : Array.Empty<string>();

    /// <inheritdoc />
    public void SetMultiString(string key, string name, IReadOnlyList<string> values) => Create(key)[name] = values.ToArray();

    /// <inheritdoc />
    public void DeleteValue(string key, string name)
    {
        if (_keys.TryGetValue(Norm(key), out var v)) v.Remove(name);
    }

    /// <inheritdoc />
    public void DeleteKeyIfEmpty(string key)
    {
        var k = Norm(key);
        if (!_keys.TryGetValue(k, out var v) || v.Count > 0) return;
        var prefix = k + "\\";
        if (_keys.Keys.Any(other => other.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) return;
        _keys.Remove(k);
    }

    private Dictionary<string, object> Create(string key)
    {
        var parts = Norm(key).Split('\\');
        Dictionary<string, object>? last = null;
        for (int i = 1; i <= parts.Length; i++)
        {
            var path = string.Join('\\', parts.Take(i));
            if (!_keys.TryGetValue(path, out last)) _keys[path] = last = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }
        return last!;
    }
}

/// <summary>
/// Registers nfty's file types per user, over any <see cref="IRegistryHive"/>. The Windows build
/// uses <see cref="WindowsRegistryHive"/> under HKEY_CURRENT_USER; tests use a memory hive or a
/// throwaway key.
/// </summary>
/// <remarks>
/// <para><b>What it writes, all under HKCU (no admin):</b> a ProgID per type
/// (<c>Classes\nfty.CookBook</c>: friendly name, <c>DefaultIcon</c> = the exe, <c>shell\open\command</c>);
/// the ProgID under each extension's <c>OpenWithProgids</c>, so nfty is offered in Open with;
/// <c>Software\nfty\Capabilities\FileAssociations</c> and the <c>RegisteredApplications</c> value, so nfty
/// appears in Windows Default apps. For nfty's OWN extensions only, the extension's default value is
/// set to nfty's ProgID - and only when it is empty or already nfty's: another program's default is
/// never overwritten.</para>
/// <para><b>What it cannot do:</b> make itself the default for <c>.png</c>, <c>.jpg</c>, <c>.json</c>
/// or a model. Windows 10 and 11 keep that choice in a hashed <c>UserChoice</c> only the user can set,
/// which is why the card has a Default apps button rather than pretending.</para>
/// <para><b>Remove takes back exactly what Apply wrote.</b> Every key Apply CREATED and every value it
/// set goes into a ledger (<c>Software\nfty\Associations</c>); Remove deletes a value only while it
/// still holds what nfty wrote, and a key only once it is empty - so a key another program has since
/// put something in survives. Apply always removes first, so changing a target or repairing a moved
/// exe never leaves a stale command behind.</para>
/// </remarks>
public sealed class FileAssociationRegistrar : IFileAssociations
{
    /// <summary>The argument that opens a file in the viewer alone.</summary>
    public const string ViewerSwitch = "--viewer";

    /// <summary>The Default apps page for nfty, as Windows documents it for a per-user registration.</summary>
    public const string DefaultAppsUri = "ms-settings:defaultapps?registeredAppUser=" + AssociationLayout.AppName;

    private const string LedgerValue = "Written";

    private readonly IRegistryHive _hive;
    private readonly AssociationLayout _layout;
    private readonly string _exe;
    private readonly Action _notify;
    private readonly Action<string> _launch;

    /// <summary>Creates the registrar.</summary>
    /// <param name="hive">The registry.</param>
    /// <param name="layout">Where to write.</param>
    /// <param name="exePath">The program the commands start: this copy of nfty.</param>
    /// <param name="notify">Tells the shell associations changed; nothing when null.</param>
    /// <param name="launch">Opens a URI; the shell's own handler when null.</param>
    public FileAssociationRegistrar(IRegistryHive hive, AssociationLayout layout, string exePath,
        Action? notify = null, Action<string>? launch = null)
    {
        ArgumentNullException.ThrowIfNull(hive);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);
        _hive = hive;
        _layout = layout;
        _exe = exePath;
        _notify = notify ?? (() => { });
        _launch = launch ?? (uri => Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose());
    }

    /// <inheritdoc />
    public bool IsSupported => true;

    /// <summary>The command line a type's ProgID runs for a target, the path quoted so a space in
    /// either path is safe.</summary>
    /// <param name="exePath">The program.</param>
    /// <param name="type">The type.</param>
    /// <param name="target">App or Viewer.</param>
    /// <returns>The command.</returns>
    public static string CommandFor(string exePath, AssociatedType type, AssociationTarget target) =>
        target == AssociationTarget.App && type.OpensInApp
            ? $"\"{exePath}\" \"%1\""
            : $"\"{exePath}\" {ViewerSwitch} \"%1\"";

    private string ProgIdKey(string progId) => $@"{_layout.Classes}\{progId}";
    private string CommandKey(string progId) => ProgIdKey(progId) + @"\shell\open\command";
    private string ExtKey(string ext) => $@"{_layout.Classes}\{ext}";

    // ================================================================ inspect

    /// <inheritdoc />
    public AssociationStatus Inspect()
    {
        bool anyLedger = _hive.GetMultiString(_layout.Ledger, LedgerValue).Count > 0;
        var problems = new List<string>();
        string? otherExe = null;
        var targets = new Dictionary<AssociationGroup, AssociationTarget>();

        foreach (var group in Enum.GetValues<AssociationGroup>())
        {
            // A Menu always runs the viewer command, so it cannot say whether its group is App or
            // Viewer: the group reads as whatever its app-capable members say.
            var readable = FileTypeCatalog.Of(group).Where(t => t.OpensInApp || !FileTypeCatalog.AllowsApp(group));
            var decided = readable.Select(t => TargetOf(_hive.GetString(CommandKey(t.ProgId), ""))).ToHashSet();
            if (decided.Count > 1) problems.Add($"{FileTypeCatalog.ExtensionsOf(group)} are only partly registered.");
            targets[group] = decided.Count == 1 ? decided.Single()
                : decided.Contains(AssociationTarget.App) ? AssociationTarget.App
                : decided.Contains(AssociationTarget.Viewer) ? AssociationTarget.Viewer : AssociationTarget.Off;
        }

        var current = new AssociationChoice(targets[AssociationGroup.NftyFiles],
            targets[AssociationGroup.PicturesAndModels], targets[AssociationGroup.Metadata]);

        foreach (var group in Enum.GetValues<AssociationGroup>())
        {
            var target = current.For(group);
            if (target == AssociationTarget.Off) continue;
            foreach (var type in FileTypeCatalog.Of(group))
            {
                var command = _hive.GetString(CommandKey(type.ProgId), "");
                if (command is null) { problems.Add($"{type.Extension} is not registered."); continue; }
                if (!string.Equals(command, CommandFor(_exe, type, target), StringComparison.OrdinalIgnoreCase))
                {
                    var exe = ExeOf(command);
                    if (!string.Equals(exe, _exe, StringComparison.OrdinalIgnoreCase)) otherExe ??= exe;
                    else problems.Add($"{type.Extension} opens differently from the rest of its group.");
                }
                if (!_hive.HasValue(ExtKey(type.Extension) + @"\OpenWithProgids", type.ProgId)
                    || _hive.GetString(_layout.Capabilities + @"\FileAssociations", type.Extension) != type.ProgId)
                    problems.Add($"{type.Extension} is missing part of its registration.");
            }
        }
        if (current.Any && _hive.GetString(_layout.RegisteredApplications, AssociationLayout.AppName) is null)
            problems.Add("nfty is not listed in Default apps.");

        if (!current.Any)
            return anyLedger
                ? new(AssociationState.NeedsRepair, current, "Part of an earlier registration is left over. Remove clears it.")
                : new(AssociationState.NotRegistered, current, "Nothing is registered. Choose what nfty opens, then Apply.");
        if (otherExe is not null)
            return new(AssociationState.NeedsRepair, current,
                $"Registered to another copy of nfty ({otherExe}). Repair points it at this one.");
        if (problems.Count > 0)
            return new(AssociationState.NeedsRepair, current, problems[0] + " Repair completes it.");
        int count = Enum.GetValues<AssociationGroup>().Where(g => current.For(g) != AssociationTarget.Off)
            .Sum(g => FileTypeCatalog.Of(g).Count());
        return new(AssociationState.Registered, current,
            $"{count} file {(count == 1 ? "type opens" : "types open")} with this copy of nfty.");
    }

    private static AssociationTarget TargetOf(string? command) =>
        command is null ? AssociationTarget.Off
        : command.Contains(" " + ViewerSwitch + " ", StringComparison.Ordinal) ? AssociationTarget.Viewer
        : AssociationTarget.App;

    private static string ExeOf(string command)
    {
        var c = command.TrimStart();
        if (c.StartsWith('"'))
        {
            int end = c.IndexOf('"', 1);
            return end > 1 ? c[1..end] : c;
        }
        int space = c.IndexOf(' ');
        return space > 0 ? c[..space] : c;
    }

    // ================================================================ apply / remove

    /// <inheritdoc />
    public void Apply(AssociationChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        RemoveCore();
        var ledger = new List<string>();
        try
        {
            WriteAll(choice, ledger);
        }
        finally
        {
            // Recorded even when a write failed part-way, so Remove can still take back what landed.
            if (ledger.Count > 0) _hive.SetMultiString(_layout.Ledger, LedgerValue, ledger);
        }
        _notify();
    }

    private void WriteAll(AssociationChoice choice, List<string> ledger)
    {
        if (choice.Any)
        {
            Write(_layout.Capabilities, "ApplicationName", "nfty", ledger);
            Write(_layout.Capabilities, "ApplicationDescription",
                "Pixel-art collections: CookBooks, Sets, and the viewer for pictures and 3D models.", ledger);
            Write(_layout.Capabilities, "ApplicationIcon", $"\"{_exe}\",0", ledger);
            Write(_layout.RegisteredApplications, AssociationLayout.AppName, _layout.Capabilities, ledger);
        }
        var progIdsWritten = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in FileTypeCatalog.All)
        {
            var target = choice.For(type.Group);
            if (target == AssociationTarget.Off) continue;
            if (target == AssociationTarget.App && !FileTypeCatalog.AllowsApp(type.Group)) target = AssociationTarget.Viewer;
            if (progIdsWritten.Add(type.ProgId))
            {
                Write(ProgIdKey(type.ProgId), "", type.FriendlyName, ledger);
                Write(ProgIdKey(type.ProgId), "FriendlyTypeName", type.FriendlyName, ledger);
                Write(ProgIdKey(type.ProgId) + @"\DefaultIcon", "", $"\"{_exe}\",0", ledger);
                Write(CommandKey(type.ProgId), "", CommandFor(_exe, type, target), ledger);
            }
            Write(ExtKey(type.Extension) + @"\OpenWithProgids", type.ProgId, "", ledger);
            Write(_layout.Capabilities + @"\FileAssociations", type.Extension, type.ProgId, ledger);
            if (type.NftyOwned)
            {
                // nfty's own extension: the default goes to nfty's ProgID, but only over nothing or
                // over nfty's own. Another program that claimed .set keeps it.
                var existing = _hive.GetString(ExtKey(type.Extension), "");
                if (string.IsNullOrEmpty(existing) || existing.StartsWith("nfty.", StringComparison.OrdinalIgnoreCase))
                    Write(ExtKey(type.Extension), "", type.ProgId, ledger);
            }
        }
    }

    /// <inheritdoc />
    public void Remove()
    {
        RemoveCore();
        _notify();
    }

    /// <inheritdoc />
    public void OpenDefaultApps() => _launch(DefaultAppsUri);

    private void Write(string key, string name, string value, List<string> ledger)
    {
        var parts = key.Split('\\');
        for (int i = 1; i <= parts.Length; i++)
        {
            var prefix = string.Join('\\', parts.Take(i));
            if (!_hive.KeyExists(prefix))
            {
                ledger.Add($"K\t{prefix}");
                _hive.CreateKey(prefix);
            }
        }
        _hive.SetString(key, name, value);
        ledger.Add($"V\t{key}\t{name}\t{value}");
    }

    private void RemoveCore()
    {
        var entries = _hive.GetMultiString(_layout.Ledger, LedgerValue);
        foreach (var e in entries)
        {
            var f = e.Split('\t');
            if (f.Length == 4 && f[0] == "V" && _hive.GetString(f[1], f[2]) == f[3]) _hive.DeleteValue(f[1], f[2]);
        }
        _hive.DeleteValue(_layout.Ledger, LedgerValue);
        _hive.DeleteKeyIfEmpty(_layout.Ledger);
        // Deepest first: a key is recorded after its parent, so the reverse order empties children
        // before their parents are asked.
        foreach (var e in entries.Reverse())
        {
            var f = e.Split('\t');
            if (f.Length == 2 && f[0] == "K") _hive.DeleteKeyIfEmpty(f[1]);
        }
    }
}

/// <summary>
/// What the Settings card says when Windows refuses something: product sentences that name what to
/// do next, never an exception's own text.
/// </summary>
public static class AssociationText
{
    /// <summary>The reason line when the registration could not be read.</summary>
    public const string CannotRead =
        "Windows would not let nfty read its file associations. Repair writes them again; Remove clears them.";

    /// <summary>The error card's title when Apply fails.</summary>
    public const string ApplyFailedTitle = "Could not register nfty";

    /// <summary>The error card's sentence when Apply fails.</summary>
    public const string ApplyFailed =
        "Windows refused to let nfty write its file associations for this user. Whatever was written is shown on the card and Remove takes it back. Close other programs that change file types, then try again.";

    /// <summary>The error card's title when Remove fails.</summary>
    public const string RemoveFailedTitle = "Could not remove nfty's registration";

    /// <summary>The error card's sentence when Remove fails.</summary>
    public const string RemoveFailed =
        "Windows refused to let nfty remove its file associations. What is left is shown on the card; press Remove again, or remove nfty in Windows Settings under Apps, Default apps.";

    /// <summary>The error card's title when the Default apps page cannot be opened.</summary>
    public const string DefaultAppsFailedTitle = "Could not open Default apps";

    /// <summary>The error card's sentence when the Default apps page cannot be opened.</summary>
    public const string DefaultAppsFailed =
        "Windows did not open its Default apps page. Open Windows Settings, go to Apps, Default apps, find nfty, and choose its file types there.";

    /// <summary>Whether an exception is one of the ways Windows refuses a registry or shell request,
    /// which the card words itself. Anything else is a defect and is not caught.</summary>
    /// <param name="ex">The exception.</param>
    /// <returns>True for access, security, I/O, shell and platform refusals.</returns>
    public static bool IsRefusal(Exception ex) =>
        ex is UnauthorizedAccessException or System.Security.SecurityException or IOException
            or System.ComponentModel.Win32Exception or PlatformNotSupportedException;
}

/// <summary>Builds the machine's associations.</summary>
public static class FileAssociations
{
    /// <summary>The registrar for this machine and program: Windows writes under HKEY_CURRENT_USER;
    /// every other platform is <see cref="UnsupportedFileAssociations"/>.</summary>
    /// <param name="exePath">This copy of nfty; <see cref="Environment.ProcessPath"/> when null.</param>
    /// <returns>The associations.</returns>
    public static IFileAssociations ForThisMachine(string? exePath = null)
    {
        var exe = exePath ?? Environment.ProcessPath;
        if (OperatingSystem.IsWindows() && !string.IsNullOrEmpty(exe))
            return new FileAssociationRegistrar(new WindowsRegistryHive(), AssociationLayout.Windows, exe,
                WindowsRegistryHive.NotifyAssociationsChanged);
        return new UnsupportedFileAssociations();
    }
}
