using Avalonia;

namespace Nfty.Desktop;

internal static class Program
{
    /// <summary>The path the process was started with (<c>nfty book.cbk</c>, or a file dropped on the
    /// executable), or null. Opened by <see cref="App"/> once the shell is on screen.</summary>
    internal static string? StartupPath { get; private set; }

    /// <summary>True for <c>nfty --viewer &lt;path&gt;</c>: the path opens in the viewer alone, with no
    /// app shell. The command the Windows "open with the nfty viewer" registration runs.</summary>
    internal static bool ViewerOnly { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        StartupPath = Nfty.App.Services.StartupPath.FromArguments(args);
        ViewerOnly = StartupPath is not null && Nfty.App.Services.StartupPath.WantsViewer(args);
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (DllNotFoundException ex) when (MissingLinuxLibrary(ex) is { } sentence)
        {
            // Avalonia 12's X11 backend opens libICE/libSM for session management unconditionally
            // (X11PlatformOptions.EnableSessionManagement=false does not skip it), so a minimal
            // distro without them used to die with a .NET stack trace and SIGABRT. Say what to
            // install instead. Desktop installs (GNOME, KDE, Xfce) carry these already.
            Console.Error.WriteLine(sentence);
            return 1;
        }
    }

    /// <summary>The product sentence for a missing X11 library, or null when the exception is not
    /// one of the libraries nfty's Linux install notes list.</summary>
    internal static string? MissingLinuxLibrary(DllNotFoundException ex)
    {
        if (OperatingSystem.IsWindows()) return null;
        string text = ex.Message;   // read for the library's NAME only, never shown
        string? lib = new[] { "libICE", "libSM", "libX11", "libfontconfig", "libXrandr", "libXi", "libXcursor" }
            .FirstOrDefault(name => text.Contains(name, StringComparison.Ordinal));
        if (lib is null) return null;
        return $"nfty cannot start: the system library {lib} is not installed.\n"
             + "On Debian or Ubuntu:  sudo apt install libice6 libsm6 libx11-6 libfontconfig1\n"
             + "On Fedora:            sudo dnf install libICE libSM libX11 fontconfig\n"
             + "On Arch:              sudo pacman -S libice libsm libx11 fontconfig\n"
             + "The command line (cli/Nfty.Cli) needs none of these.";
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .LogToTrace();
}
