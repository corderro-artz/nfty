using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Nfty.App.Views;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The 3D tile opens with the model centered in it. Reported as "sits slightly right, hard to rotate".
/// Measured off the tile's own bitmap: the ink's bounding box against the bitmap's middle.
/// </summary>
public class ModelTileCenteringTests
{
    [AvaloniaTheory]
    [InlineData("recipe", 1180, 720)]
    [InlineData("variant", 1180, 720)]
    [InlineData("set", 1180, 720)]
    [InlineData("recipe", 1067, 526)]
    public void The_model_opens_centered_in_its_tile(string source, double width, double height)
    {
        IDisposable? owner = null;
        var vm = source switch
        {
            "recipe" => RisePages.DemoRecipeVm(),
            "variant" => RisePages.DemoVariantVm(),
            _ => Set(out owner),
        };
        using var own = owner;
        using var page = RisePages.Show(vm, width, height);
        var view = page.View.GetVisualDescendants().OfType<ModelPreviewView>().Single();
        view.RepaintNow();
        var bmp = (WriteableBitmap)view.GetVisualDescendants().OfType<Image>().Single(i => i.Name == "PART_Surface").Source!;
        var (left, right, top, bottom) = InkBox(bmp);
        double w = bmp.PixelSize.Width, h = bmp.PixelSize.Height;
        double dx = (left + right) / 2.0 - (w - 1) / 2.0;
        double dy = (top + bottom) / 2.0 - (h - 1) / 2.0;
        Assert.True(Math.Abs(dx) <= 1.5, $"ink x {left}..{right} in {w} px: off center by {dx:0.0}");
        Assert.True(Math.Abs(dy) <= 1.5, $"ink y {top}..{bottom} in {h} px: off center by {dy:0.0}");
    }

    private static Nfty.App.ViewModels.RiseViewModel Set(out IDisposable owner)
    {
        var (vm, o) = RisePages.DemoSetVm();
        owner = o;
        return vm;
    }

    private static (int L, int R, int T, int B) InkBox(WriteableBitmap bmp)
    {
        using var fb = bmp.Lock();
        int w = fb.Size.Width, h = fb.Size.Height;
        var px = new int[w * h];
        for (int y = 0; y < h; y++)
            System.Runtime.InteropServices.Marshal.Copy(fb.Address + y * fb.RowBytes, px, y * w, w);
        int bg = px[0];
        int l = w, r = -1, t = h, b = -1;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            if (px[y * w + x] == bg) continue;
            l = Math.Min(l, x); r = Math.Max(r, x); t = Math.Min(t, y); b = Math.Max(b, y);
        }
        return (l, r, t, b);
    }
}
