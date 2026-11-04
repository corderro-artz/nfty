using System;
using System.IO;
using Nfty.App.Services;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The Linux secret store's conversation with <c>secret-tool</c>, against a stand-in script that
/// records exactly what it was handed. A real keyring is not here (CI and WSL run none), so this
/// proves the half that is nfty's: the secret travels on STDIN and never on the command line, where
/// every process on the machine could read it. The round trip through a real GNOME Keyring or
/// KWallet is a desktop check, recorded as such in the Linux readiness notes.
/// </summary>
public class SecretToolStoreTests
{
    private const string Script = """
        #!/bin/sh
        dir=$(dirname "$0")
        for a in "$@"; do printf '%s\n' "$a" >> "$dir/args.log"; done
        printf -- '--\n' >> "$dir/args.log"
        case "$1" in
          store) cat > "$dir/secret" ;;
          lookup) if [ -f "$dir/secret" ]; then cat "$dir/secret"; else exit 1; fi ;;
          clear) rm -f "$dir/secret" ;;
        esac
        """;

    [Fact]
    public void The_secret_goes_on_stdin_and_never_into_the_arguments()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(), "needs /bin/sh to run the stand-in");

        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var tool = Path.Combine(dir, "secret-tool");
            File.WriteAllText(tool, Script.Replace("\r\n", "\n") + "\n");
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(tool, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            var store = new SecretToolStore(tool);
            Assert.True(store.IsAvailable);
            Assert.Null(store.UnavailableReason);

            const string secret = "eyJhbGciOi.pinata-jwt-under-test";
            Assert.Null(store.Read("pinata"));
            store.Write("pinata", secret);

            // What the tool received on stdin, byte for byte - and the secret appears nowhere in argv.
            Assert.Equal(secret, File.ReadAllText(Path.Combine(dir, "secret")));
            var args = File.ReadAllText(Path.Combine(dir, "args.log"));
            Assert.DoesNotContain(secret, args, StringComparison.Ordinal);
            Assert.Contains("store\n--label=nfty pinata\nservice\nnfty\naccount\npinata\n", args, StringComparison.Ordinal);

            Assert.Equal(secret, store.Read("pinata"));
            store.Delete("pinata");
            Assert.Null(store.Read("pinata"));
            Assert.DoesNotContain(secret, File.ReadAllText(Path.Combine(dir, "args.log")), StringComparison.Ordinal);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_missing_tool_says_what_to_install()
    {
        var store = new SecretToolStore(Path.Combine(Path.GetTempPath(), "nfty-no-such-" + Guid.NewGuid().ToString("n")));
        Assert.False(store.IsAvailable);
        Assert.Contains("libsecret-tools", store.UnavailableReason, StringComparison.Ordinal);
    }
}
