using System.Runtime.InteropServices;
using System.Runtime.Versioning;
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

    private const int ShcneAssocChanged = 0x08000000;
    private const uint ShcnfIdList = 0x0000;

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
