using Avalonia;
using Avalonia.Headless;
using Nfty.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Nfty.App.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .UseSkia()
        // v12 stopped implying a text shaper for anyone who selects the render backend by hand.
        .UseHarfBuzz();
}
