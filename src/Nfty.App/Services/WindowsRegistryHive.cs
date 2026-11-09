using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;

namespace Nfty.App.Services;

/// <summary>
/// <see cref="IRegistryHive"/> over HKEY_CURRENT_USER, through the in-box
/// <see cref="Microsoft.Win32.Registry"/>. Per user only: nothing here opens HKEY_LOCAL_MACHINE, so
/// registering never needs admin.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsRegistryHive : IRegistryHive
{
    private static RegistryKey Root => Registry.CurrentUser;

    private static string? Name(string name) => name.Length == 0 ? null : name;

    /// <inheritdoc />
    public bool KeyExists(string key)
    {
        using var k = Root.OpenSubKey(key);
        return k is not null;
    }

    /// <inheritdoc />
    public bool HasValue(string key, string name)
    {
        using var k = Root.OpenSubKey(key);
        return k is not null && (name.Length == 0
            ? k.GetValue(null) is not null
            : k.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ValueNames(string key)
    {
        using var k = Root.OpenSubKey(key);
        return k?.GetValueNames() ?? [];
    }

    /// <inheritdoc />
    public string? GetString(string key, string name)
    {
        using var k = Root.OpenSubKey(key);
        if (k is null) return null;
        return k.GetValue(Name(name)) switch
        {
            string s => s,
            // A value of another kind (REG_NONE, which other programs use under OpenWithProgids) is
            // present, not absent.
            null when name.Length > 0 && k.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase) => "",
            null => null,
            var other => other.ToString(),
        };
    }

    /// <inheritdoc />
    public void CreateKey(string key)
    {
        using var _ = Root.CreateSubKey(key, writable: true);
    }

    /// <inheritdoc />
    public void SetString(string key, string name, string value)
    {
        using var k = Root.CreateSubKey(key, writable: true);
        k.SetValue(Name(name), value, RegistryValueKind.String);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetMultiString(string key, string name)
    {
        using var k = Root.OpenSubKey(key);
        return k?.GetValue(Name(name)) as string[] ?? Array.Empty<string>();
    }

    /// <inheritdoc />
    public void SetMultiString(string key, string name, IReadOnlyList<string> values)
    {
        using var k = Root.CreateSubKey(key, writable: true);
        k.SetValue(Name(name), values.ToArray(), RegistryValueKind.MultiString);
    }

    /// <inheritdoc />
    public void DeleteValue(string key, string name)
    {
        using var k = Root.OpenSubKey(key, writable: true);
        k?.DeleteValue(name, throwOnMissingValue: false);
    }

    /// <inheritdoc />
    public void DeleteKeyIfEmpty(string key)
    {
        using (var k = Root.OpenSubKey(key))
        {
            if (k is null || k.ValueCount > 0 || k.SubKeyCount > 0) return;
        }
        Root.DeleteSubKey(key, throwOnMissingSubKey: false);
    }

    /// <summary>Tells the shell file associations changed, so Explorer's icons and Open with refresh.</summary>
    public static void NotifyAssociationsChanged() => SHChangeNotify(ShcneAssocChanged, ShcnfIdList, IntPtr.Zero, IntPtr.Zero);

    /// <summary>Asks the shell for the effective ProgID, including Windows' protected user choice.
    /// This reads only; it never writes UserChoice or infers a default from an executable path.</summary>
    /// <param name="extension">The file extension, including its dot.</param>
    /// <returns>The known handler, a known absence, or Unknown when Windows cannot answer.</returns>
    public static AssociationDefault QueryDefault(string extension)
    {
        const uint progId = 20; // ASSOCSTR_PROGID (Windows 10+).
        const int noAssociation = unchecked((int)0x80070483);
        uint count = 0;
        int result = AssocQueryStringW(0, progId, extension, null, null, ref count);
        if (result == noAssociation) return new(true, null);
        // S_FALSE is the documented size-query success. Bound the allocation even if a malformed
        // external registration reports an implausible size.
        if (result != 1 || count is 0 or > 32768) return AssociationDefault.Unknown;
        var text = new StringBuilder((int)count);
        result = AssocQueryStringW(0, progId, extension, null, text, ref count);
        return result == 0 ? new(true, text.Length == 0 ? null : text.ToString()) : AssociationDefault.Unknown;
    }

    private const int ShcneAssocChanged = 0x08000000;
    private const uint ShcnfIdList = 0x0000;

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int AssocQueryStringW(uint flags, uint str, string association, string? extra,
        StringBuilder? text, ref uint characters);
}
