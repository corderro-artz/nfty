using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Nfty.App.Views;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// A theme switch repaints every canvas ground. The canvas host reads its inks from the theme when it
/// builds the backdrop, and its geometry cache never saw a theme change, so switching to light kept
/// the dark ground on the editor, the Rise page and the viewer alike.
/// </summary>
public class CanvasThemeSwitchTests
{
    [AvaloniaFact]
    public void Switching_to_light_repaints_the_canvas_ground()
    {
        var backdrop = new Panel();
        var surface = new Panel();
        var image = new Image();
        var window = new Window { Content = new Grid { Children = { backdrop, surface, image } }, RequestedThemeVariant = ThemeVariant.Dark };
        window.Show();
        var host = new PixelCanvasHost(image, surface, backdrop, () => null,
            key => backdrop.TryFindResource(key, backdrop.ActualThemeVariant, out var v) ? v as IBrush : null);
        host.RebuildBackdrop();
        Assert.Equal(Color.Parse("#0a0b10"), ((ISolidColorBrush)backdrop.Background!).Color);

        window.RequestedThemeVariant = ThemeVariant.Light;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Color.Parse("#f1ece4"), ((ISolidColorBrush)backdrop.Background!).Color);
        window.Close();
    }

    [AvaloniaFact]
    public void The_inspector_checker_follows_the_theme()
    {
        var border = new Border();
        border.Bind(Border.BackgroundProperty, border.GetResourceObservable("CheckerBrush"));
        var window = new Window { Content = border, RequestedThemeVariant = ThemeVariant.Dark };
        window.Show();
        var dark = border.Background;

        window.RequestedThemeVariant = ThemeVariant.Light;
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(dark);
        Assert.NotSame(dark, border.Background);
        window.Close();
    }
}
