using Avalonia;

namespace Nfty.Desktop;

internal static class Program
{
    /// <summary>The path the process was started with (<c>nfty book.cbk</c>, or a file dropped on the
    /// executable), or null. Opened by <see cref="App"/> once the shell is on screen.</summary>
    internal static string? StartupPath { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        StartupPath = Nfty.App.Services.StartupPath.FromArguments(args);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .LogToTrace();
}
