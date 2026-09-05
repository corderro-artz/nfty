using System;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Imaging;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// A collapsed caret and an expanded one are the same drawing, turned.
/// </summary>
/// <remarks>
/// <para>Fluent draws ONE chevron and rotates it 90 degrees for the expanded state, stretching the
/// glyph to fit its box. So the box's ASPECT decides the glyph's size: in a 10 wide by 13 tall box
/// the collapsed "&gt;" was scaled to the 13 and the expanded "v" to the 10, and a tree column showed
/// right-pointing carets half again the size of the down-pointing ones directly above them. Nothing
/// in the markup says that — both states share one style and one number each for width and height.
/// </para>
///
/// <para>Which is why this measures INK off a rendered frame rather than reading the setters back.
/// A test that asserted "the box is square" would restate the fix; this asserts the thing the fix is
/// for, and would still hold if Fluent switched to two separate glyphs.</para>
/// </remarks>
public class TreeChevronTests
{
    /// <summary>Every light pixel in a rectangle, as a bounding box — the caret is the only ink in
    /// the gutter it sits in, so a brightness floor finds it without knowing its shape.</summary>
    private static (int W, int H) Ink(WriteableBitmap frame, PixelRect rect)
    {
        int stride = rect.Width * 4;
        var pixels = Marshal.AllocHGlobal(stride * rect.Height);
        try
        {
            frame.CopyPixels(rect, pixels, stride * rect.Height, stride);
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            for (int y = 0; y < rect.Height; y++)
            for (int x = 0; x < rect.Width; x++)
            {
                int i = y * stride + x * 4;
                if (Marshal.ReadByte(pixels, i) > 90 && Marshal.ReadByte(pixels, i + 1) > 90
                                                     && Marshal.ReadByte(pixels, i + 2) > 90)
                {
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                }
            }
            return minX == int.MaxValue ? (0, 0) : (maxX - minX + 1, maxY - minY + 1);
        }
        finally { Marshal.FreeHGlobal(pixels); }
    }

    [AvaloniaFact]
    public void A_collapsed_caret_is_the_same_size_as_an_expanded_one()
    {
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        var session = new CookBookSession();
        using var explorer = new ExplorerViewModel(ExplorerViewModelTests.TwoRecipeBook(), nav, dialogs,
            new ImageBridge(), ExplorerViewModelTests.EditorFactory(nav),
            ExplorerViewModelTests.CookFactory(dialogs), session, new FilePickerService(),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), new StatusService());

        var view = new Views.ExplorerView { DataContext = explorer };
        var window = new Window { RequestedThemeVariant = ThemeVariant.Dark, Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Two carets in opposite states. The book opens expanded and its recipes closed, so the tree
        // shows both without the test having to click anything - which also means a change that
        // collapsed the root by default would fail here loudly rather than quietly measuring one
        // state twice.
        var chevrons = view.GetVisualDescendants().OfType<ToggleButton>()
            .Where(t => t.Name == "PART_ExpandCollapseChevron" && t.Bounds.Width > 0)
            .ToList();
        var expanded = chevrons.FirstOrDefault(t => t.IsChecked == true);
        var collapsed = chevrons.FirstOrDefault(t => t.IsChecked != true);
        Assert.NotNull(expanded);
        Assert.NotNull(collapsed);

        using var frame = window.CaptureRenderedFrame()!;
        double scale = window.RenderScaling;

        PixelRect Box(Visual v)
        {
            var a = v.TranslatePoint(new Point(0, 0), view)!.Value;
            var b = v.TranslatePoint(new Point(v.Bounds.Width, v.Bounds.Height), view)!.Value;
            return new PixelRect((int)(a.X * scale), (int)(a.Y * scale),
                                 (int)((b.X - a.X) * scale), (int)((b.Y - a.Y) * scale));
        }

        var (dw, dh) = Ink(frame, Box(expanded!));
        var (cw, ch) = Ink(frame, Box(collapsed!));
        Assert.True(dw > 0 && cw > 0, "no caret ink found");

        // Mirrored, not merely similar: a turned drawing swaps its extents exactly. 1px of slack for
        // anti-aliasing on the diagonal strokes, which is all the difference there should ever be.
        Assert.True(Math.Abs(dw - ch) <= 1 && Math.Abs(dh - cw) <= 1,
            $"expanded ink is {dw}x{dh} and collapsed is {cw}x{ch}; turning one should give the other");
    }
}
