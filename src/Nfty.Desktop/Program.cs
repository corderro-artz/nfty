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
    public static void Main(string[] args)
    {
        StartupPath = Nfty.App.Services.StartupPath.FromArguments(args);
        ViewerOnly = StartupPath is not null && Nfty.App.Services.StartupPath.WantsViewer(args);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .LogToTrace();
}
