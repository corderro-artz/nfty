using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Nfty.App.Services;
using Nfty.App.Viewer;

Console.WriteLine($"App assembly: {typeof(GalleryItem).Assembly.Location}");
using var entered = new ManualResetEventSlim();
using var release = new ManualResetEventSlim();
using var item = new GalleryItem("late", () =>
{
    entered.Set();
    if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("loader release");
    return null;
}, () => throw new InvalidOperationException(), new ThumbnailGate(1));
using var session = HeadlessUnitTestSession.StartNew(typeof(GapBuilder));
bool enteredSecond = false;
Exception? failure = null;
try
{
    session.Dispatch(() =>
    {
        _ = item.Thumbnail;
        if (!entered.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("loader enter");
        if (args.Contains("--dispose")) item.Dispose();
    }, CancellationToken.None).GetAwaiter().GetResult();
    GapBuilder.InResetGap = () =>
    {
        release.Set();
        if (!item.PendingDecode.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("decode finish");
        Console.WriteLine("actual production GalleryItem callback finished inside reset gap");
    };
    session.Dispatch(() => { enteredSecond = true; Dispatcher.UIThread.VerifyAccess(); }, CancellationToken.None)
        .GetAwaiter().GetResult();
}
catch (Exception ex)
{
    failure = ex;
    Console.WriteLine(ex);
}
finally
{
    GapBuilder.InResetGap = null;
    release.Set();
    item.PendingDecode.GetAwaiter().GetResult();
}
Console.WriteLine($"second application delegate entered={enteredSecond}; failure={failure?.GetType().Name ?? "none"}");
return enteredSecond && failure is null ? 0 : 1;

/// <summary>Builds isolated headless applications with a controlled callback before compositor setup.</summary>
public static class GapBuilder
{
    internal static Action? InResetGap;

    /// <summary>Configures rendering and invokes the current probe callback after Skia registration.</summary>
    /// <returns>The builder consumed by the headless session.</returns>
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<Application>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia().UseHarfBuzz();
        var initialize = builder.RenderingSubsystemInitializer!;
        return builder.UseRenderingSubsystem(() =>
        {
            initialize();
            InResetGap?.Invoke();
        });
    }
}
