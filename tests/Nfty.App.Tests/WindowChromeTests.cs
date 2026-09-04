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
    /// its vertical centring — so it sat inset from the edge with a hit rect far shorter than the
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
}
