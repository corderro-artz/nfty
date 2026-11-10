using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// New canvases start sprite-sized, and a large one is warned about in a line that is always laid
/// out, so the warning appearing moves nothing on the card.
/// </summary>
public class CanvasAdviceTests
{
    [Theory]
    [InlineData(512, 512, false)]
    [InlineData(513, 512, true)]
    [InlineData(1000, 1000, true)]
    [InlineData(2048, 64, false)]
    [InlineData(0, 4000, false)]
    public void Large_means_more_pixels_than_512_square(long w, long h, bool large) =>
        Assert.Equal(large, CanvasAdvice.IsLarge(w, h));

    [AvaloniaFact]
    public void Both_forms_open_at_32_and_unwarned()
    {
        var book = new NewCookBookViewModel(new FakeDialogs());
        Assert.Equal((32, 32), (book.Width, book.Height));
        Assert.Equal(0, book.LargeCanvasOpacity);
        var ing = new NewIngredientViewModel(new FakeDialogs());
        Assert.Equal((32, 32), (ing.CanvasWidth, ing.CanvasHeight));
        Assert.Equal(0, ing.LargeCanvasOpacity);
    }

    [AvaloniaFact]
    public void A_large_canvas_inks_the_warning_and_moves_nothing()
    {
        var vm = new NewCookBookViewModel(new FakeDialogs());
        var view = new Views.NewCookBookView { DataContext = vm };
        var window = new Window { Content = view, Width = 1067, Height = 526 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var before = view.DesiredSize;
            vm.Width = 1000;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, vm.LargeCanvasOpacity);
            Assert.Equal(before, view.DesiredSize);
        }
        finally { window.Close(); }
    }
}
