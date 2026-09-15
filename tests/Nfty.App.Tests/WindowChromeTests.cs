using Avalonia.Headless;
using System.Runtime.InteropServices;
using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>The custom window chrome — the part no headless frame could ever prove, and which a
/// manual smoke test duly found broken.
///
/// The frame's 12px margin is the shadow gutter: room for the drop shadow to render inside the
/// transparent window surface. A maximized window has no desktop to float in, so it must go to zero
/// — and it did not, because the margin was an INLINE attribute on the element. In Avalonia a local
/// value outranks every Style setter, so `Window[WindowState=Maximized] Border.frame { Margin=0 }`
/// could never win and a maximized window kept a visible gap on all four edges.
///
/// That is the class of bug this file exists for: a style that reads correctly and is inert.</summary>
public class WindowChromeTests
{
    private sealed class StubTheme : IThemeService
    { public bool IsDark { get; private set; } public void Toggle() => IsDark = !IsDark; }

    private static ShellViewModel Shell() =>
        new(new FakeNav(), new FakeDialogs(), new StubTheme(), new StatusService());

    private static Border Frame(Visual root) =>
        root.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("frame"));

    /// <summary>The margin must come from the STYLE, not from the element. This is the actual defect:
    /// an inline Margin is a local value, and no Style can override a local value — so the maximized
    /// rule was dead from the day it was written.</summary>
    [AvaloniaFact]
    public void The_shadow_gutter_is_a_style_setter_not_an_inline_value()
    {
        var view = new Views.ShellChromeView { DataContext = Shell() };
        var window = new Window { Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var frame = Frame(view);
        Assert.Equal(new Thickness(12), frame.Margin);

        // A local value would beat any Style. If Margin is ever moved back onto the element, this is
        // what catches it: the diagnostic reports where the value actually came from.
        var diagnostic = frame.GetDiagnostic(Layoutable.MarginProperty);
        Assert.NotEqual(BindingPriority.LocalValue, diagnostic.Priority);
    }

    /// <summary>And the override itself: a Style CAN now reach the margin, which is what makes the
    /// maximized rule work. Applied directly rather than by maximizing a headless window, whose
    /// WindowState the platform backend does not necessarily honour.</summary>
    [AvaloniaFact]
    public void A_style_can_now_zero_the_gutter_which_is_what_maximizing_does()
    {
        var view = new Views.ShellChromeView { DataContext = Shell() };
        var window = new Window { Content = view, Width = 1180, Height = 720 };
        window.Styles.Add(new Style(x => x.OfType<Border>().Class("frame"))
        {
            Setters =
            {
                new Setter(Layoutable.MarginProperty, new Thickness(0)),
                new Setter(Border.CornerRadiusProperty, new CornerRadius(0)),
            },
        });
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var frame = Frame(view);
        Assert.Equal(new Thickness(0), frame.Margin);
        Assert.Equal(new CornerRadius(0), frame.CornerRadius);
    }

    /// <summary>The border draws the app's accent, and — the part that was visibly broken — the frame
    /// itself does NOT clip. A Border that both strokes a CornerRadius and clips to its own bounds
    /// cuts the outer half of that stroke exactly at the arcs, so the border vanished at all four
    /// corners while the straight edges stayed full thickness. The clipping belongs to an inner
    /// border.</summary>
    [AvaloniaFact]
    public void The_frame_strokes_the_accent_and_leaves_clipping_to_an_inner_border()
    {
        var view = new Views.ShellChromeView { DataContext = Shell() };
        var window = new Window { Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var frame = Frame(view);

        Assert.False(frame.ClipToBounds);   // the whole point: it must not clip its own stroke
        Assert.Equal(new Thickness(1), frame.BorderThickness);

        var accent = (ISolidColorBrush)Application.Current!.FindResource(
            ThemeVariant.Light, "AccentBrush")!;
        Assert.Equal(accent.Color, ((ISolidColorBrush)frame.BorderBrush!).Color);

        // The inner one clips, and is a pixel tighter so it hugs the inside of the stroke.
        var clip = view.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("frame-clip"));
        Assert.True(clip.ClipToBounds);
        Assert.True(clip.CornerRadius.TopLeft < frame.CornerRadius.TopLeft);
    }

    /// <summary>The shell renders at BaseScale, and zoom multiplies ON TOP of it rather than replacing
    /// it. Reproducing the mockups at exactly 1.0 left everything correct in logical pixels and too
    /// small in physical ones on a large high-DPI display.</summary>
    [AvaloniaFact]
    public void The_shell_renders_at_the_base_scale_and_zoom_composes_with_it()
    {
        var vm = Shell();
        Assert.Equal(1.2, ShellViewModel.BaseScale);
        Assert.Equal(1.2, vm.ChromeScale);

        // 100% means the base scale, which is why the status bar can keep reading "100%".
        Assert.Equal(100, vm.Zoom);
        Assert.Equal(1.0, vm.ZoomScale);
        Assert.Equal(1.2, vm.EffectivePageScale, 6);

        // ...and the two compose multiplicatively. Zoom must NOT fold BaseScale in itself: the chrome
        // transform is an ancestor of the page transform, so doing it in both squares the factor.
        vm.ZoomOutCommand.Execute(null);            // 90%
        Assert.Equal(0.9, vm.ZoomScale, 6);
        Assert.Equal(1.08, vm.EffectivePageScale, 6);
    }

    /// <summary>The base scale reaches the visual tree, not just the ViewModel — and the chrome is
    /// INSIDE it (the titlebar and status bar read small too), while the zoom transform stays a
    /// separate, inner one so the window buttons and grip keep the window's edge.</summary>
    [AvaloniaFact]
    public void The_base_scale_is_applied_to_the_whole_shell_and_zoom_is_a_separate_inner_transform()
    {
        var vm = Shell();
        var view = new Views.ShellChromeView { DataContext = vm };
        var window = new Window { Content = view, Width = 1416, Height = 864 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var transforms = view.GetVisualDescendants().OfType<LayoutTransformControl>()
            .Select(t => (t, s: t.LayoutTransform as ScaleTransform))
            .Where(x => x.s is not null)
            .ToList();
        Assert.Equal(2, transforms.Count);

        var chrome = transforms[0];   // outermost
        var page = transforms[1];
        Assert.Equal(1.2, chrome.s!.ScaleX, 6);
        Assert.Equal(1.0, page.s!.ScaleX, 6);

        // The titlebar is inside the base-scale transform; if it were a sibling, "everything 20%
        // bigger" would have skipped the chrome the user was actually complaining about.
        Assert.Contains(chrome.t, view.FindControl<Border>("Titlebar")!.GetVisualAncestors());
        Assert.DoesNotContain(page.t, view.FindControl<Border>("Titlebar")!.GetVisualAncestors());
    }

    /// <summary>The resize grip is pinned to the frame's real corner with a full-square hit area. It
    /// used to be the last child of the zoom StackPanel, inheriting that panel's 8px right margin and
    /// its vertical centering — so it sat inset from the edge with a hit rect far shorter than the
    /// corner it appeared to occupy, and hovering it often missed.</summary>
    [AvaloniaFact]
    public void The_resize_grip_fills_the_corner_rather_than_floating_near_it()
    {
        var view = new Views.ShellChromeView { DataContext = Shell() };
        var window = new Window { Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var grip = view.ResizeGripArea;

        Assert.Equal(HorizontalAlignment.Right, ((Control)grip).HorizontalAlignment);
        Assert.Equal(VerticalAlignment.Bottom, ((Control)grip).VerticalAlignment);
        Assert.Equal(new Thickness(0), ((Control)grip).Margin);   // flush to the corner
        Assert.True(grip.Bounds.Width >= 20 && grip.Bounds.Height >= 20,
            $"hit area is {grip.Bounds.Width}x{grip.Bounds.Height}; too small to hit reliably");

        // Hit-testable across the whole rectangle, not just the glyph's thin strokes.
        Assert.NotNull(((Border)grip).Background);
    }

    /// <summary>
    /// The lock badge must not touch the window-button divider.
    /// </summary>
    /// <remarks>
    /// The badge is the titlebar's last content column and the window buttons are the next one, and
    /// that column opens with a 1px hairline carrying no left inset — so with no margin on the badge
    /// its border and that hairline share an edge, and the pair reads as one control with a stripe
    /// drawn down it. Measured off a laid-out frame in the titlebar's own coordinate space, because
    /// what matters is the gap on screen and neither control's own properties state it.
    ///
    /// A floor of 8 rather than "more than zero": one pixel of daylight satisfies "not touching" and
    /// still looks like a mistake, and this is the check that would otherwise pass while the defect
    /// came back.
    /// </remarks>
    [AvaloniaFact]
    public void The_lock_badge_does_not_touch_the_window_button_divider()
    {
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        var session = new CookBookSession();
        using var explorer = new ExplorerViewModel(ExplorerViewModelTests.TwoRecipeBook(), nav, dialogs,
            new ImageBridge(), ExplorerViewModelTests.EditorFactory(nav),
            ExplorerViewModelTests.CookFactory(dialogs), session, new FilePickerService(),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), new StatusService());

        var shell = new ShellViewModel(nav, dialogs, new StubTheme(), new StatusService());
        nav.To(explorer);   // the badge only exists while an Explorer is the current page

        var view = new Views.ShellChromeView { DataContext = shell };
        var window = new Window { Content = view, Width = 1416, Height = 864 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var titlebar = view.GetVisualDescendants().OfType<Border>().First(b => b.Name == "Titlebar");
        var badge = view.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("lockflag"));

        // The divider is the first child of the strip that ends in the close button — found by that
        // relationship rather than by index, so re-ordering the buttons does not silently retarget it.
        var strip = view.GetVisualDescendants().OfType<StackPanel>()
            .First(sp => sp.Children.OfType<Button>().Any(b => b.Classes.Contains("danger")));
        var divider = strip.Children.OfType<Border>().First();

        double badgeRight = badge.TranslatePoint(new Point(badge.Bounds.Width, 0), titlebar)!.Value.X;
        double dividerLeft = divider.TranslatePoint(new Point(0, 0), titlebar)!.Value.X;

        Assert.True(dividerLeft - badgeRight >= 8,
            $"the badge ends at {badgeRight} and the divider starts at {dividerLeft}");
    }

    /// <summary>
    /// The brand mark's ink is centered in its tile — measured off a RENDERED frame — and both of
    /// its inks are actually drawn.
    /// </summary>
    /// <remarks>
    /// <para>The mark is the application icon's symbol: a live layer in the accent over a dimmed one
    /// in the foreground ink (<see cref="Views.BrandMarkView"/>). Every property involved is already
    /// correct — the tile is centered, the canvas is centered, the paths are where the markup says —
    /// and what a mark gets wrong lives entirely in where the pixels land, which is the one thing the
    /// markup does not state. So the frame is captured and the ink inside the tile is measured.</para>
    ///
    /// <para>BOTH inks are required, not merely some ink. The two layers are painted from different
    /// brushes, and a <c>DynamicResource</c> that fails to resolve does not throw — the property
    /// silently keeps its default, which for a <c>Path</c> is no paint at all. Half a mark would
    /// otherwise still centre acceptably and pass.</para>
    ///
    /// <para>The tile's own hairline is the accent too, at 40% over a near-black ground, so a
    /// brightness floor separates the live layer from the edge around it; probing that floor by
    /// removing the accent path finds no saturated red at all.</para>
    ///
    /// <para>The tolerance is 1px because that is what anti-aliasing costs on a diagonal. The stack
    /// is not quite symmetric about its own middle — the dimmed layer carries a stroke and the live
    /// one is filled — which spends about a third of a pixel of it.</para>
    /// </remarks>
    [AvaloniaFact]
    public void The_brand_mark_is_centered_in_its_tile()
    {
        var view = new Views.ShellChromeView { DataContext = Shell() };
        var window = new Window { RequestedThemeVariant = ThemeVariant.Dark, Content = view, Width = 1416, Height = 864 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // The tile is found through the mark's own control rather than by shape, so nothing else
        // round and 24px wide can be measured by accident.
        var mark = view.GetVisualDescendants().OfType<Views.BrandMarkView>().First();
        var tile = mark.GetVisualDescendants().OfType<Border>().First();

        using var frame = window.CaptureRenderedFrame()!;

        // Only the tile is copied out, not the whole 1416x864 frame: the rectangle is what the test
        // is about, and reading it directly means no decode step and no image library.
        // BOTH corners are translated, rather than an origin plus Bounds.Size. Bounds is in the
        // tile's OWN coordinate space, which the shell's ChromeScale has not been applied to yet -
        // using it samples a 24px window out of a tile that renders 28.8px wide, and a crop that
        // clips one side biases the very center this test measures.
        var topLeft = tile.TranslatePoint(new Point(0, 0), view)!.Value;
        var bottomRight = tile.TranslatePoint(new Point(tile.Bounds.Width, tile.Bounds.Height), view)!.Value;
        double scale = window.RenderScaling;
        var rect = new PixelRect((int)(topLeft.X * scale), (int)(topLeft.Y * scale),
                                 (int)((bottomRight.X - topLeft.X) * scale),
                                 (int)((bottomRight.Y - topLeft.Y) * scale));
        int stride = rect.Width * 4;
        var pixels = Marshal.AllocHGlobal(stride * rect.Height);
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        bool sawAccent = false, sawInk = false;
        try
        {
            frame.CopyPixels(rect, pixels, stride * rect.Height, stride);

            // RGBA: red leads. Checked against the frame rather than assumed from the format name -
            // read as Bgra this finds no saturated red at all, which is how the order was caught.
            for (int y = 0; y < rect.Height; y++)
            for (int x = 0; x < rect.Width; x++)
            {
                int i = y * stride + x * 4;
                byte r = Marshal.ReadByte(pixels, i);
                byte g = Marshal.ReadByte(pixels, i + 1);
                byte b = Marshal.ReadByte(pixels, i + 2);

                // The live layer is saturated; the dimmed one is the near-neutral cream of FgBrush.
                bool accent = r > 150 && r - g > 70 && r - b > 50;
                bool ink = r > 150 && g > 140 && b > 130 && r - g < 40;
                if (!accent && !ink) continue;

                sawAccent |= accent;
                sawInk |= ink;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }
        }
        finally { Marshal.FreeHGlobal(pixels); }

        Assert.True(sawAccent, "no accent ink found inside the brand tile - the live layer is missing");
        Assert.True(sawInk, "no foreground ink found inside the brand tile - the dimmed layer is missing");

        // Both centers are put back into VIEW coordinates before comparing. The crop's own origin is
        // an integer pixel and the tile's is not (ChromeScale is 1.2), so measuring the ink against
        // the CROP's midpoint charges the mark for up to a pixel of truncation that is the crop's.
        double inkX = rect.X + (minX + maxX) / 2.0, inkY = rect.Y + (minY + maxY) / 2.0;
        double tileX = (topLeft.X + bottomRight.X) / 2.0 * scale - 0.5;
        double tileY = (topLeft.Y + bottomRight.Y) / 2.0 * scale - 0.5;

        Assert.True(Math.Abs(inkX - tileX) <= 1 && Math.Abs(inkY - tileY) <= 1,
            $"the mark's ink centers at ({inkX}, {inkY}) in a tile centered ({tileX}, {tileY})");
    }
}
