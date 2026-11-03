using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Nfty.App.Services;

/// <summary>
/// The operating system's own store for secrets: Windows Credential Manager, the Secret Service on
/// Linux (through <c>secret-tool</c>), the macOS keychain.
/// </summary>
/// <remarks>
/// <para><b>There is no plain-file fallback, on purpose.</b> When the platform's store cannot be
/// reached, <see cref="IsAvailable"/> is false, <see cref="UnavailableReason"/> says why and what to
/// install, and the Settings card refuses to save rather than writing the key somewhere a backup, a
/// sync folder or another user's process could read it. A key that cannot be kept safely is asked
/// for again; it is not kept unsafely.</para>
/// <para><b>A secret never travels on a command line.</b> <c>secret-tool</c> is handed the secret
/// on standard input; the macOS store is reached through Security.framework directly rather than
/// <c>security add-generic-password -w</c>, which would put it in every process listing.</para>
/// <para>Nothing here logs, prints or puts a secret in an exception message.</para>
/// </remarks>
public interface ISecretStore
{
    /// <summary>Whether this machine's store can be used.</summary>
    bool IsAvailable { get; }

    /// <summary>Why it cannot, and what to do about it; null when it can.</summary>
    string? UnavailableReason { get; }

    /// <summary>Reads a secret.</summary>
    /// <param name="target">Its name, e.g. <c>nfty/pinata</c>.</param>
    /// <returns>The secret, or null when none is saved.</returns>
    /// <exception cref="InvalidOperationException">The store is unavailable or failed.</exception>
    string? Read(string target);

    /// <summary>Saves a secret, replacing any saved under the same name.</summary>
    /// <param name="target">Its name.</param>
    /// <param name="secret">The secret.</param>
    /// <exception cref="InvalidOperationException">The store is unavailable or failed.</exception>
    void Write(string target, string secret);

    /// <summary>Forgets a secret. Forgetting one that is not there is not an error.</summary>
    /// <param name="target">Its name.</param>
    /// <exception cref="InvalidOperationException">The store is unavailable or failed.</exception>
    void Delete(string target);
}

/// <summary>Picks this machine's store.</summary>
public static class SecretStores
{
    /// <summary>The store for the operating system the app is running on.</summary>
    /// <returns>A store; possibly an unavailable one that says why.</returns>
    public static ISecretStore ForThisMachine()
    {
        if (OperatingSystem.IsWindows()) return new WindowsCredentialStore();
        if (OperatingSystem.IsMacOS()) return new MacKeychainStore();
        if (OperatingSystem.IsLinux()) return new SecretToolStore();
        return new UnavailableSecretStore("Secure storage is unavailable on this operating system.");
    }
}

/// <summary>A store that cannot be used, and says why. Never falls back to a file.</summary>
/// <param name="reason">Why.</param>
public sealed class UnavailableSecretStore(string reason) : ISecretStore
{
    /// <inheritdoc />
    public bool IsAvailable => false;
    /// <inheritdoc />
    public string? UnavailableReason { get; } = reason;
    /// <inheritdoc />
    public string? Read(string target) => throw new InvalidOperationException(UnavailableReason);
    /// <inheritdoc />
    public void Write(string target, string secret) => throw new InvalidOperationException(UnavailableReason);
    /// <inheritdoc />
    public void Delete(string target) => throw new InvalidOperationException(UnavailableReason);
}

/// <summary>Windows Credential Manager: a generic credential per target, kept for this user.</summary>
public sealed class WindowsCredentialStore : ISecretStore
{
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    [StructLayout(LayoutKind.Sequential)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public uint LastWrittenLow;
        public uint LastWrittenHigh;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true)]
    private static extern bool CredWrite(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredFree")]
    private static extern void CredFree(IntPtr buffer);

    /// <inheritdoc />
    public bool IsAvailable => OperatingSystem.IsWindows();

    /// <inheritdoc />
    public string? UnavailableReason => IsAvailable ? null : "Windows Credential Manager is only on Windows.";

    /// <inheritdoc />
    public string? Read(string target)
    {
        Guard();
        if (!CredRead(target, CredTypeGeneric, 0, out var ptr))
        {
            int error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound) return null;
            throw Fail("read", error);
        }
        try
        {
            var cred = Marshal.PtrToStructure<Credential>(ptr);
            if (cred.CredentialBlob == IntPtr.Zero || cred.CredentialBlobSize == 0) return null;
            var bytes = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.UTF8.GetString(bytes);
        }
        finally { CredFree(ptr); }
    }

    /// <inheritdoc />
    public void Write(string target, string secret)
    {
        Guard();
        ArgumentException.ThrowIfNullOrEmpty(secret);
        var bytes = Encoding.UTF8.GetBytes(secret);
        IntPtr name = Marshal.StringToHGlobalUni(target);
        IntPtr user = Marshal.StringToHGlobalUni(Environment.UserName);
        IntPtr blob = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var cred = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = name,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = CredPersistLocalMachine,
                UserName = user,
            };
            if (!CredWrite(ref cred, 0)) throw Fail("save", Marshal.GetLastWin32Error());
        }
        finally
        {
            // Overwrite the copy before freeing it: the managed array is the caller's, this one is ours.
            Marshal.Copy(new byte[bytes.Length], 0, blob, bytes.Length);
            Array.Clear(bytes);
            Marshal.FreeHGlobal(blob);
            Marshal.FreeHGlobal(name);
            Marshal.FreeHGlobal(user);
        }
    }

    /// <inheritdoc />
    public void Delete(string target)
    {
        Guard();
        if (!CredDelete(target, CredTypeGeneric, 0))
        {
            int error = Marshal.GetLastWin32Error();
            if (error != ErrorNotFound) throw Fail("forget", error);
        }
    }

    private void Guard()
    {
        if (!IsAvailable) throw new InvalidOperationException(UnavailableReason);
    }

    private static InvalidOperationException Fail(string what, int error) =>
        new($"Windows Credential Manager could not {what} the key: {new Win32Exception(error).Message}");
}

/// <summary>
/// The Secret Service (GNOME Keyring, KWallet) through <c>secret-tool</c> from libsecret. The secret
/// goes in on standard input and comes back on standard output; it is never an argument.
/// </summary>
public sealed class SecretToolStore : ISecretStore
{
    private readonly string _tool;

    /// <summary>Creates the store over <c>secret-tool</c> on the PATH.</summary>
    /// <param name="tool">The executable; tests may point it elsewhere.</param>
    public SecretToolStore(string tool = "secret-tool") => _tool = tool;

    /// <inheritdoc />
    public bool IsAvailable => Locate() is not null;

    /// <inheritdoc />
    public string? UnavailableReason => IsAvailable ? null
        : "Secure storage is unavailable: secret-tool was not found. Install libsecret-tools "
          + "(Debian/Ubuntu) or libsecret (Fedora/Arch) and make sure a keyring is running.";

    private string? Locate()
    {
        if (Path.IsPathRooted(_tool)) return File.Exists(_tool) ? _tool : null;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (dir.Length == 0) continue;
            var candidate = Path.Combine(dir, _tool);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>The target split into the attribute pair secret-tool keys an item by.</summary>
    private static string[] Attributes(string target) => ["service", "nfty", "account", target];

    /// <inheritdoc />
    public string? Read(string target)
    {
        var (code, output) = Run(["lookup", .. Attributes(target)], stdin: null);
        if (code != 0) return null;   // secret-tool exits 1 when nothing matches
        return output.Length == 0 ? null : output;
    }

    /// <inheritdoc />
    public void Write(string target, string secret)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);
        var (code, _) = Run(["store", "--label=nfty " + target, .. Attributes(target)], stdin: secret);
        if (code != 0)
            throw new InvalidOperationException(
                "secret-tool could not save the key. Is a keyring (GNOME Keyring, KWallet) running and unlocked?");
    }

    /// <inheritdoc />
    public void Delete(string target) => Run(["clear", .. Attributes(target)], stdin: null);

    private (int Code, string Output) Run(string[] args, string? stdin)
    {
        var exe = Locate() ?? throw new InvalidOperationException(UnavailableReason);
        var info = new ProcessStartInfo(exe)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args) info.ArgumentList.Add(a);
        try
        {
            using var process = Process.Start(info) ?? throw new InvalidOperationException(UnavailableReason);
            if (stdin is not null) process.StandardInput.Write(stdin);
            process.StandardInput.Close();
            string output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, output);
        }
        catch (Win32Exception)
        {
            throw new InvalidOperationException(UnavailableReason);
        }
    }
}

/// <summary>
/// The macOS login keychain, through Security.framework: a generic password with service
/// <c>nfty</c> and the target as its account. Reached by P/Invoke, never by the <c>security</c>
/// tool, whose <c>-w</c> puts the secret on a command line.
/// </summary>
public sealed class MacKeychainStore : ISecretStore
{
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const int ErrSecSuccess = 0;
    private const int ErrSecItemNotFound = -25300;
    private const uint Utf8 = 0x08000100;

    [DllImport(Security)] private static extern int SecItemAdd(IntPtr attributes, IntPtr result);
    [DllImport(Security)] private static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);
    [DllImport(Security)] private static extern int SecItemDelete(IntPtr query);
    [DllImport(CoreFoundation)] private static extern IntPtr CFDictionaryCreate(IntPtr allocator, IntPtr[] keys, IntPtr[] values, nint count, IntPtr keyCallbacks, IntPtr valueCallbacks);
    [DllImport(CoreFoundation)] private static extern IntPtr CFStringCreateWithBytes(IntPtr allocator, byte[] bytes, nint length, uint encoding, bool external);
    [DllImport(CoreFoundation)] private static extern IntPtr CFDataCreate(IntPtr allocator, byte[] bytes, nint length);
    [DllImport(CoreFoundation)] private static extern nint CFDataGetLength(IntPtr data);
    [DllImport(CoreFoundation)] private static extern IntPtr CFDataGetBytePtr(IntPtr data);
    [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr cf);

    private static readonly Lazy<Symbols?> Loaded = new(Load);

    private sealed record Symbols(IntPtr KeyCallbacks, IntPtr ValueCallbacks, IntPtr Class, IntPtr GenericPassword,
        IntPtr Service, IntPtr Account, IntPtr ValueData, IntPtr ReturnData, IntPtr MatchLimit, IntPtr MatchLimitOne,
        IntPtr True);

    private static Symbols? Load()
    {
        if (!OperatingSystem.IsMacOS()) return null;
        try
        {
            var sec = NativeLibrary.Load(Security);
            var cf = NativeLibrary.Load(CoreFoundation);
            IntPtr Addr(IntPtr lib, string name) => NativeLibrary.GetExport(lib, name);
            IntPtr Value(IntPtr lib, string name) => Marshal.ReadIntPtr(Addr(lib, name));
            return new Symbols(Addr(cf, "kCFTypeDictionaryKeyCallBacks"), Addr(cf, "kCFTypeDictionaryValueCallBacks"),
                Value(sec, "kSecClass"), Value(sec, "kSecClassGenericPassword"), Value(sec, "kSecAttrService"),
                Value(sec, "kSecAttrAccount"), Value(sec, "kSecValueData"), Value(sec, "kSecReturnData"),
                Value(sec, "kSecMatchLimit"), Value(sec, "kSecMatchLimitOne"), Value(cf, "kCFBooleanTrue"));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public bool IsAvailable => Loaded.Value is not null;

    /// <inheritdoc />
    public string? UnavailableReason => IsAvailable ? null : "Secure storage is unavailable: the macOS keychain could not be reached.";

    private Symbols S => Loaded.Value ?? throw new InvalidOperationException(UnavailableReason);

    private static IntPtr Str(string s)
    {
        var b = Encoding.UTF8.GetBytes(s);
        return CFStringCreateWithBytes(IntPtr.Zero, b, b.Length, Utf8, false);
    }

    private IntPtr Query(string target, IEnumerable<(IntPtr Key, IntPtr Value)> extra, List<IntPtr> owned)
    {
        var s = S;
        IntPtr service = Str("nfty"), account = Str(target);
        owned.Add(service);
        owned.Add(account);
        var pairs = new List<(IntPtr, IntPtr)> { (s.Class, s.GenericPassword), (s.Service, service), (s.Account, account) };
        pairs.AddRange(extra);
        var dict = CFDictionaryCreate(IntPtr.Zero, pairs.Select(p => p.Item1).ToArray(),
            pairs.Select(p => p.Item2).ToArray(), pairs.Count, s.KeyCallbacks, s.ValueCallbacks);
        owned.Add(dict);
        return dict;
    }

    private static void Release(List<IntPtr> owned)
    {
        foreach (var p in owned) if (p != IntPtr.Zero) CFRelease(p);
    }

    /// <inheritdoc />
    public string? Read(string target)
    {
        var s = S;
        var owned = new List<IntPtr>();
        try
        {
            var q = Query(target, [(s.ReturnData, s.True), (s.MatchLimit, s.MatchLimitOne)], owned);
            int status = SecItemCopyMatching(q, out var data);
            if (status == ErrSecItemNotFound) return null;
            if (status != ErrSecSuccess) throw new InvalidOperationException($"The keychain could not read the key (status {status}).");
            owned.Add(data);
            var bytes = new byte[CFDataGetLength(data)];
            Marshal.Copy(CFDataGetBytePtr(data), bytes, 0, bytes.Length);
            return Encoding.UTF8.GetString(bytes);
        }
        finally { Release(owned); }
    }

    /// <inheritdoc />
    public void Write(string target, string secret)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);
        Delete(target);
        var s = S;
        var owned = new List<IntPtr>();
        try
        {
            var bytes = Encoding.UTF8.GetBytes(secret);
            var data = CFDataCreate(IntPtr.Zero, bytes, bytes.Length);
            Array.Clear(bytes);
            owned.Add(data);
            var q = Query(target, [(s.ValueData, data)], owned);
            int status = SecItemAdd(q, IntPtr.Zero);
            if (status != ErrSecSuccess) throw new InvalidOperationException($"The keychain could not save the key (status {status}).");
        }
        finally { Release(owned); }
    }

    /// <inheritdoc />
    public void Delete(string target)
    {
        var owned = new List<IntPtr>();
        try
        {
            int status = SecItemDelete(Query(target, [], owned));
            if (status is not ErrSecSuccess and not ErrSecItemNotFound)
                throw new InvalidOperationException($"The keychain could not forget the key (status {status}).");
        }
        finally { Release(owned); }
    }
}

/// <summary>A store held in memory, for tests. The app never uses it: an unavailable OS store is
/// reported as unavailable, not replaced.</summary>
public sealed class InMemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _secrets = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public bool IsAvailable => true;
    /// <inheritdoc />
    public string? UnavailableReason => null;
    /// <inheritdoc />
    public string? Read(string target) => _secrets.GetValueOrDefault(target);
    /// <inheritdoc />
    public void Write(string target, string secret) => _secrets[target] = secret;
    /// <inheritdoc />
    public void Delete(string target) => _secrets.Remove(target);
}
