using System.Diagnostics;
using Nfty.Core.Formats;

namespace Nfty.App.Services;

/// <summary>The mode a file type is registered for; Windows still chooses its effective default.</summary>
public enum AssociationTarget
{
    /// <summary>Not registered: nfty is not offered for these files.</summary>
    Off,

    /// <summary>The nfty app: the file opens in the shell, as a Recent row would.</summary>
    App,

    /// <summary>The pop-out viewer alone, with no app shell.</summary>
    Viewer,
}

/// <summary>Catalog categories, also used by the older all-type registration presets.</summary>
public enum AssociationGroup
{
    /// <summary>nfty's own archives: <c>.cbk</c>, <c>.rcp</c>, <c>.igt</c>, <c>.set</c>, <c>.tin</c>,
    /// <c>.ktn</c>, <c>.mnu</c>. Their per-user class defaults are set only when absent or already owned by this type.</summary>
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
/// <param name="Group">Its catalog category and legacy group preset.</param>
/// <param name="NftyOwned">True for an archive whose empty or own per-user class default nfty may set; false for shared types.
/// Windows effective defaults remain subject to the user's protected choice.</param>
/// <param name="OpensInApp">True when the app shell opens it; false when only the viewer does (a
/// Menu), so the app target still registers the viewer command for it.</param>
public sealed record AssociatedType(string Extension, string ProgId, string FriendlyName,
    AssociationGroup Group, bool NftyOwned, bool OpensInApp)
{
    /// <summary>An independent handler for this extension. Earlier releases shared picture/model
    /// handlers; keeping their names readable while giving new registrations their own command
    /// prevents repairing one type from changing an unselected sibling's program.</summary>
    public string RegistrationProgId => FileTypeCatalog.All.Count(t => t.ProgId == ProgId) > 1
        ? ProgId + Extension : ProgId;
}

/// <summary>
/// Every file type nfty registers for, and its routing/category. The inventory is the viewer's own
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

/// <summary>A registration choice with legacy group presets and independent extension overrides.</summary>
/// <param name="NftyFiles">nfty's own archives.</param>
/// <param name="PicturesAndModels">Pictures and 3D models.</param>
/// <param name="Metadata">Asset metadata JSON.</param>
public sealed record AssociationChoice(AssociationTarget NftyFiles, AssociationTarget PicturesAndModels,
    AssociationTarget Metadata)
{
    private IReadOnlyDictionary<string, AssociationTarget> _extensions =
        new Dictionary<string, AssociationTarget>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Nothing registered.</summary>
    public static AssociationChoice None { get; } = new(AssociationTarget.Off, AssociationTarget.Off, AssociationTarget.Off);

    /// <summary>The legacy all-type suggested preset: nfty's files in the app,
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
    public bool Any => FileTypeCatalog.All.Any(t => For(t) != AssociationTarget.Off);

    /// <summary>One extension's actual target; files the shell cannot edit use the viewer.</summary>
    /// <param name="type">The catalog entry.</param>
    /// <returns>Its target.</returns>
    public AssociationTarget For(AssociatedType type)
    {
        var target = _extensions.TryGetValue(type.Extension, out var chosen) ? chosen : For(type.Group);
        return target == AssociationTarget.App && !type.OpensInApp ? AssociationTarget.Viewer : target;
    }

    /// <summary>A choice with only the named extensions changed.</summary>
    /// <param name="extensions">Supported extensions, with their dots.</param>
    /// <param name="target">The requested target; unsupported App routes become Viewer.</param>
    /// <returns>The independent choice.</returns>
    /// <exception cref="ArgumentException">An extension or target is unsupported.</exception>
    public AssociationChoice With(IEnumerable<string> extensions, AssociationTarget target)
    {
        if (!Enum.IsDefined(target)) throw new ArgumentException("Unknown association target.", nameof(target));
        var next = new Dictionary<string, AssociationTarget>(_extensions, StringComparer.OrdinalIgnoreCase);
        foreach (string extension in extensions)
        {
            var type = FileTypeCatalog.All.FirstOrDefault(t => t.Extension.Equals(extension, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException("Unsupported file extension.", nameof(extensions));
            next[type.Extension] = target;
        }
        return this with { _extensions = next };
    }

    /// <summary>Equality compares resolved per-extension choices, including older group presets.</summary>
    /// <param name="other">The other choice.</param>
    /// <returns>Whether all targets agree.</returns>
    public bool Equals(AssociationChoice? other) => other is not null
        && FileTypeCatalog.All.All(t => For(t) == other.For(t));

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var type in FileTypeCatalog.All) hash.Add(For(type));
        return hash.ToHashCode();
    }
}

/// <summary>How the registration on this machine reads.</summary>
public enum AssociationState
{
    /// <summary>Nothing of nfty's is registered.</summary>
    NotRegistered,

    /// <summary>Registered, complete, and pointing at this copy of nfty.</summary>
    Registered,

    /// <summary>Something is registered but incomplete, inconsistent, or pointing at another copy
    /// (the app was moved); Register repairs the selected types; Unregister removes their owned values.</summary>
    NeedsRepair,
}

/// <summary>The registration as read from the machine.</summary>
/// <param name="State">Registered, not, or broken.</param>
/// <param name="Current">The per-extension registration (Off where nothing is).</param>
/// <param name="Detail">One sentence saying what was found.</param>
public sealed record AssociationStatus(AssociationState State, AssociationChoice Current, string Detail)
{
    /// <summary>Independent registration and effective-default evidence for every supported type.</summary>
    public IReadOnlyList<FileTypeRegistration> Types { get; init; } = [];
}

/// <summary>A Windows effective-default query result; failure is unknown, not a negative answer.</summary>
/// <param name="IsKnown">Whether Windows answered the query.</param>
/// <param name="ProgId">The chosen handler, or null when Windows reports no association.</param>
public sealed record AssociationDefault(bool IsKnown, string? ProgId)
{
    /// <summary>The default could not be queried.</summary>
    public static AssociationDefault Unknown { get; } = new(false, null);
}

/// <summary>One type's registration, distinct from what Windows currently opens it with.</summary>
/// <param name="Type">The catalog entry.</param>
/// <param name="State">Whether its registration is complete for this copy.</param>
/// <param name="Target">The registered mode, or Off.</param>
/// <param name="Default">Windows' query result.</param>
/// <param name="DefaultTarget">App/Viewer only when the queried ProgID and command identify this copy.</param>
/// <param name="Detail">The registration explanation.</param>
public sealed record FileTypeRegistration(AssociatedType Type, AssociationState State,
    AssociationTarget Target, AssociationDefault Default, AssociationTarget? DefaultTarget, string Detail);

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
    /// <param name="choice">The group preset and per-extension overrides; Off types are not registered.</param>
    void Apply(AssociationChoice choice);

    /// <summary>Changes only selected supported extensions, preserving unselected registrations.</summary>
    /// <param name="extensions">The selected extensions.</param>
    /// <param name="target">App, Viewer, or Off to unregister the selection.</param>
    void SetTargets(IReadOnlyCollection<string> extensions, AssociationTarget target);

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
    public void SetTargets(IReadOnlyCollection<string> extensions, AssociationTarget target) =>
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

    /// <summary>The value names at a key, including the empty default name.</summary>
    /// <param name="key">The key path.</param>
    /// <returns>The names, empty when the key is absent.</returns>
    IReadOnlyList<string> ValueNames(string key);

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
    public IReadOnlyList<string> ValueNames(string key) =>
        _keys.TryGetValue(Norm(key), out var v) ? v.Keys.ToArray() : [];

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
/// <para><b>What it cannot do:</b> override a protected effective default for any extension.
/// Windows 10 and 11 keep that choice in a hashed <c>UserChoice</c> only the user can set.
/// Writing a class default for an archive does not replace that choice; the card offers Default apps.</para>
/// <para><b>Remove takes back exactly what Apply wrote.</b> Every key Apply CREATED and every value it
/// set goes into a ledger (<c>Software\nfty\Associations</c>); Remove deletes a value only while it
/// still holds what nfty wrote, and a key only once it is empty, so foreign values survive.
/// SetTargets removes only the selected extensions' ledger entries and writes independent handlers;
/// unselected legacy shared handlers and externally modified commands are preserved.
/// Apply and Remove are the explicit all-type operations retained for older callers.</para>
/// </remarks>
public sealed class FileAssociationRegistrar : IFileAssociations
{
    /// <summary>The argument that opens a file in the viewer alone.</summary>
    public const string ViewerSwitch = "--viewer";

    /// <summary>The per-user Default apps deep link used on Windows 11 23H2 or newer.
    /// Older versions conservatively open the general page, including patched older Windows 11.</summary>
    public static string DefaultAppsUri => DefaultAppsUriFor(new Version(10, 0, 22631), AssociationLayout.AppName);

    internal static string DefaultAppsUriFor(Version windowsVersion, string registeredAppName) =>
        windowsVersion >= new Version(10, 0, 22631)
            ? "ms-settings:defaultapps?registeredAppUser=" + Uri.EscapeDataString(registeredAppName)
            : "ms-settings:defaultapps";

    private const string LedgerValue = "Written";

    private readonly IRegistryHive _hive;
    private readonly AssociationLayout _layout;
    private readonly string _exe;
    private readonly Action _notify;
    private readonly Action<string> _launch;
    private readonly Func<string, AssociationDefault> _defaultFor;
    private readonly Version _windowsVersion;

    /// <summary>Creates the registrar.</summary>
    /// <param name="hive">The registry.</param>
    /// <param name="layout">Where to write.</param>
    /// <param name="exePath">The program the commands start: this copy of nfty.</param>
    /// <param name="notify">Tells the shell associations changed; nothing when null.</param>
    /// <param name="launch">Opens a URI; the shell's own handler when null.</param>
    /// <param name="defaultFor">Reads the effective ProgID; unknown when omitted, keeping tests
    /// and private registry roots independent of the real user's defaults.</param>
    /// <param name="windowsVersion">Windows version for choosing a supported Settings link; the
    /// current OS version when omitted. Tests may inject it without opening Windows Settings.</param>
    public FileAssociationRegistrar(IRegistryHive hive, AssociationLayout layout, string exePath,
        Action? notify = null, Action<string>? launch = null, Func<string, AssociationDefault>? defaultFor = null,
        Version? windowsVersion = null)
    {
        ArgumentNullException.ThrowIfNull(hive);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);
        _hive = hive;
        _layout = layout;
        _exe = exePath;
        _notify = notify ?? (() => { });
        _launch = launch ?? (uri => Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose());
        _defaultFor = defaultFor ?? (_ => AssociationDefault.Unknown);
        _windowsVersion = windowsVersion ?? Environment.OSVersion.Version;
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
        string? otherExe = null;
        var current = AssociationChoice.None;
        var types = new List<FileTypeRegistration>();
        bool listed = _hive.GetString(_layout.RegisteredApplications, AssociationLayout.AppName) == _layout.Capabilities;
        foreach (var type in FileTypeCatalog.All)
        {
            string? registeredId = RegisteredProgId(type);
            string? command = registeredId is null ? null : _hive.GetString(CommandKey(registeredId), "");
            var target = TargetOf(command);
            var state = AssociationState.NotRegistered;
            string detail = "Not registered for this extension.";
            if (registeredId is not null)
            {
                bool matches = command is not null && string.Equals(command,
                    CommandFor(_exe, type, target), StringComparison.OrdinalIgnoreCase);
                bool complete = matches && listed
                    && _hive.HasValue(ExtKey(type.Extension) + @"\OpenWithProgids", registeredId)
                    && _hive.GetString(_layout.Capabilities + @"\FileAssociations", type.Extension) == registeredId;
                state = complete ? AssociationState.Registered : AssociationState.NeedsRepair;
                detail = complete ? $"Registered for {type.Extension} in {(target == AssociationTarget.App ? "the app" : "the viewer")}."
                    : !listed && matches
                        ? $"{type.Extension} is offered in Open with, but the shared Default apps listing points elsewhere or is missing. Select All and Register to repair that listing."
                        : $"{type.Extension} needs repair. Register it again to point it at this copy.";
                if (command is not null && !matches)
                {
                    var exe = ExeOf(command);
                    if (!string.Equals(exe, _exe, StringComparison.OrdinalIgnoreCase)) otherExe ??= exe;
                }
                current = current.With([type.Extension], target);
            }
            var chosen = ReadDefault(type.Extension);
            AssociationTarget? defaultTarget = null;
            if (chosen.IsKnown && chosen.ProgId is { } id && IsTypeProgId(type, id))
            {
                string? defaultCommand = _hive.GetString(CommandKey(id), "");
                var mode = TargetOf(defaultCommand);
                if (mode != AssociationTarget.Off && string.Equals(defaultCommand,
                        CommandFor(_exe, type, mode), StringComparison.OrdinalIgnoreCase)) defaultTarget = mode;
            }
            types.Add(new(type, state, target, chosen, defaultTarget, detail));
        }
        AssociationStatus Status(AssociationState state, string detail) => new(state, current, detail) { Types = types };
        if (types.All(t => t.State == AssociationState.NotRegistered))
            return anyLedger ? Status(AssociationState.NeedsRepair, "Part of an earlier registration is left over. Unregister clears it.")
                : Status(AssociationState.NotRegistered, "Nothing is registered. Select file types, then Register.");
        if (otherExe is not null)
            return Status(AssociationState.NeedsRepair,
                $"Registered to another copy of nfty ({otherExe}). Select its file types and Register to point them at this one.");
        if (types.FirstOrDefault(t => t.State == AssociationState.NeedsRepair) is { } broken)
            return Status(AssociationState.NeedsRepair, broken.Detail);
        int count = types.Count(t => t.State == AssociationState.Registered);
        return Status(AssociationState.Registered, $"{count} file {(count == 1 ? "type is" : "types are")} registered with this copy of nfty.");
    }

    private static bool IsTypeProgId(AssociatedType type, string id) =>
        id.Equals(type.ProgId, StringComparison.OrdinalIgnoreCase)
        || id.Equals(type.RegistrationProgId, StringComparison.OrdinalIgnoreCase);

    private AssociationDefault ReadDefault(string extension)
    {
        try { return _defaultFor(extension); }
        catch (Exception ex) when (AssociationText.IsRefusal(ex)) { return AssociationDefault.Unknown; }
    }

    private string? RegisteredProgId(AssociatedType type)
    {
        string? capability = _hive.GetString(_layout.Capabilities + @"\FileAssociations", type.Extension);
        if (capability is not null && IsTypeProgId(type, capability)) return capability;
        foreach (string id in new[] { type.RegistrationProgId, type.ProgId }.Distinct(StringComparer.OrdinalIgnoreCase))
            if (_hive.HasValue(ExtKey(type.Extension) + @"\OpenWithProgids", id)
                || (type.NftyOwned && _hive.GetString(ExtKey(type.Extension), "") == id)) return id;
        return null;
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

    /// <inheritdoc />
    public void SetTargets(IReadOnlyCollection<string> extensions, AssociationTarget target)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        // Validate the complete request before the first mutation.
        _ = AssociationChoice.None.With(extensions, target);
        var selected = extensions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selected.Count == 0) return;
        var types = FileTypeCatalog.All.Where(t => selected.Contains(t.Extension)).ToArray();
        var others = FileTypeCatalog.All.Where(t => !selected.Contains(t.Extension)).ToArray();
        bool UnselectedReference(AssociatedType type, string id)
        {
            if (_hive.HasValue(ExtKey(type.Extension) + @"\OpenWithProgids", id)
                || string.Equals(_hive.GetString(_layout.Capabilities + @"\FileAssociations", type.Extension), id, StringComparison.OrdinalIgnoreCase)
                || string.Equals(_hive.GetString(ExtKey(type.Extension), ""), id, StringComparison.OrdinalIgnoreCase)) return true;
            // A protected effective default can outlive a damaged capability/OpenWith entry.
            // Unknown evidence cannot authorize deleting a shared legacy handler either.
            var effective = ReadDefault(type.Extension);
            return !effective.IsKnown || string.Equals(effective.ProgId, id, StringComparison.OrdinalIgnoreCase);
        }
        var ownedIds = types.SelectMany(t => new[] { t.ProgId, t.RegistrationProgId })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(id => !others.Any(t => IsTypeProgId(t, id) && UnselectedReference(t, id)))
            .ToArray();
        var ledger = _hive.GetMultiString(_layout.Ledger, LedgerValue).ToList();
        bool TypeKey(string key) => types.Any(t => Under(key, ExtKey(t.Extension)))
            || ownedIds.Any(id => Under(key, ProgIdKey(id)));
        bool TypeValue(string key, string name) => TypeKey(key)
            || (key.Equals(_layout.Capabilities + @"\FileAssociations", StringComparison.OrdinalIgnoreCase)
                && selected.Contains(name));
        try
        {
            RemoveEntries(ledger, TypeValue, TypeKey);
            bool othersRemain = others.Any(t => RegisteredProgId(t) is not null)
                || _hive.ValueNames(_layout.Capabilities + @"\FileAssociations").Any(n => !selected.Contains(n));
            if (target != AssociationTarget.Off)
            {
                // Shared metadata may describe an unselected registration from another copy.
                // Fill missing fields, but never silently reclaim its existing values.
                void Shared(string key, string name, string value)
                {
                    if (!othersRemain || !_hive.HasValue(key, name)) Write(key, name, value, ledger);
                }
                Shared(_layout.Capabilities, "ApplicationName", "nfty");
                Shared(_layout.Capabilities, "ApplicationDescription",
                    "Pixel-art collections: CookBooks, Sets, and the viewer for pictures and 3D models.");
                Shared(_layout.Capabilities, "ApplicationIcon", $"\"{_exe}\",0");
                Shared(_layout.RegisteredApplications, AssociationLayout.AppName, _layout.Capabilities);
                foreach (var type in types)
                {
                    var mode = target == AssociationTarget.App && !type.OpensInApp ? AssociationTarget.Viewer : target;
                    WriteType(type, mode, type.RegistrationProgId, ledger);
                }
            }
            else if (!FileTypeCatalog.All.Any(t => RegisteredProgId(t) is not null)
                     && _hive.ValueNames(_layout.Capabilities + @"\FileAssociations").Count == 0)
            {
                RemoveEntries(ledger,
                    (key, name) => Under(key, _layout.Capabilities)
                        || (key.Equals(_layout.RegisteredApplications, StringComparison.OrdinalIgnoreCase)
                            && name.Equals(AssociationLayout.AppName, StringComparison.OrdinalIgnoreCase)),
                    key => Under(key, _layout.Capabilities));
            }
        }
        finally { SaveLedger(ledger); }
        _notify();
    }

    private static bool Under(string key, string parent) => key.Equals(parent, StringComparison.OrdinalIgnoreCase)
        || key.StartsWith(parent + "\\", StringComparison.OrdinalIgnoreCase);

    private void RemoveEntries(List<string> ledger, Func<string, string, bool> valueInScope, Func<string, bool> keyInScope)
    {
        foreach (string entry in ledger.ToArray())
        {
            var fields = entry.Split('\t');
            if (fields.Length != 4 || fields[0] != "V" || !valueInScope(fields[1], fields[2])) continue;
            if (_hive.GetString(fields[1], fields[2]) == fields[3]) _hive.DeleteValue(fields[1], fields[2]);
            ledger.Remove(entry); // A later foreign value is preserved, and ownership is relinquished.
        }
        foreach (string entry in ledger.AsEnumerable().Reverse().ToArray())
        {
            var fields = entry.Split('\t');
            if (fields.Length != 2 || fields[0] != "K" || !keyInScope(fields[1])) continue;
            _hive.DeleteKeyIfEmpty(fields[1]);
            ledger.Remove(entry);
        }
    }

    private void SaveLedger(List<string> ledger)
    {
        if (ledger.Any(e => e.StartsWith("V\t", StringComparison.Ordinal)))
            _hive.SetMultiString(_layout.Ledger, LedgerValue, ledger.Distinct(StringComparer.Ordinal).ToArray());
        else
        {
            _hive.DeleteValue(_layout.Ledger, LedgerValue);
            _hive.DeleteKeyIfEmpty(_layout.Ledger);
            foreach (string entry in ledger.AsEnumerable().Reverse())
            {
                var fields = entry.Split('\t');
                if (fields.Length == 2 && fields[0] == "K") _hive.DeleteKeyIfEmpty(fields[1]);
            }
        }
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
            var target = choice.For(type);
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
                if (string.IsNullOrEmpty(existing) || IsTypeProgId(type, existing))
                    Write(ExtKey(type.Extension), "", type.ProgId, ledger);
            }
        }
    }

    private void WriteType(AssociatedType type, AssociationTarget target, string progId, List<string> ledger)
    {
        Write(ProgIdKey(progId), "", type.FriendlyName, ledger);
        Write(ProgIdKey(progId), "FriendlyTypeName", type.FriendlyName, ledger);
        Write(ProgIdKey(progId) + @"\DefaultIcon", "", $"\"{_exe}\",0", ledger);
        Write(CommandKey(progId), "", CommandFor(_exe, type, target), ledger);
        Write(ExtKey(type.Extension) + @"\OpenWithProgids", progId, "", ledger);
        Write(_layout.Capabilities + @"\FileAssociations", type.Extension, progId, ledger);
        if (type.NftyOwned)
        {
            string? existing = _hive.GetString(ExtKey(type.Extension), "");
            if (string.IsNullOrEmpty(existing) || IsTypeProgId(type, existing))
                Write(ExtKey(type.Extension), "", progId, ledger);
        }
    }

    /// <inheritdoc />
    public void Remove()
    {
        RemoveCore();
        _notify();
    }

    /// <inheritdoc />
    public void OpenDefaultApps() => _launch(DefaultAppsUriFor(_windowsVersion, AssociationLayout.AppName));

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
        ledger.RemoveAll(e =>
        {
            var fields = e.Split('\t');
            return fields.Length == 4 && fields[0] == "V"
                && fields[1].Equals(key, StringComparison.OrdinalIgnoreCase)
                && fields[2].Equals(name, StringComparison.OrdinalIgnoreCase);
        });
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
        "Windows would not let nfty read its file associations. Select types and Register to repair them, or Unregister to remove owned values.";

    /// <summary>The error card's title when Apply fails.</summary>
    public const string ApplyFailedTitle = "Could not register nfty";

    /// <summary>The error card's sentence when Apply fails.</summary>
    public const string ApplyFailed =
        "Windows refused to let nfty write its file associations for this user. Whatever was written is shown on the card; Unregister takes back the selected owned values. Close other programs that change file types, then try again.";

    /// <summary>The error card's title when Remove fails.</summary>
    public const string RemoveFailedTitle = "Could not remove nfty's registration";

    /// <summary>The error card's sentence when Remove fails.</summary>
    public const string RemoveFailed =
        "Windows refused to let nfty remove its file associations. What is left is shown on the card; press Unregister again, or remove nfty in Windows Settings under Apps, Default apps.";

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
                WindowsRegistryHive.NotifyAssociationsChanged, defaultFor: WindowsRegistryHive.QueryDefault);
        return new UnsupportedFileAssociations();
    }
}
