using Avalonia;
using Avalonia.Headless;
using Nfty.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Nfty.App.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Avalonia.Application>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
