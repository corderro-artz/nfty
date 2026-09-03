using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// Everything this app displays is generated pixel art, shown at a size unrelated to its pixel
/// count: an 8×8 variant fills a 320px canvas tile, a 64×64 asset fills a 120px card. Avalonia's
/// default scaling filter blurs every one of those, so the whole app renders bitmaps with
/// <see cref="BitmapInterpolationMode.None"/> — nearest neighbour, hard squares, no invented pixels.
/// </summary>
/// <remarks>
/// The rule cannot be a Style setter: in Avalonia 12 <c>RenderOptions</c> is a Get/Set pair rather
/// than an <c>AvaloniaProperty</c>, so a <c>Setter</c> naming it does not compile. It is set on each
/// view root and inherits down instead — which means it is exactly the kind of rule that gets
/// forgotten on the next view, and exactly why this test reads the setting off the LIVE control
/// rather than off the markup.
///
/// <para>This governs display only. Nothing in <c>Nfty.Core</c> resamples, blurs or anti-aliases a
/// variant: every edit command writes whole pixels, import refuses an image that is not exactly the
/// canvas size rather than scaling it, and colorization is per-pixel. Partial alpha is a separate
/// thing entirely — it is a value the author chooses, not a filter applied to their art.</para>
/// </remarks>
public class PixelPerfectRenderingTests
{
    /// <summary>Every view under <c>Views/</c> whose markup contains an <c>&lt;Image</c>. Derived
    /// from the files rather than hand-listed, so a new view that shows art is covered the moment it
    /// exists — the same reason ThemeResourceTests derives its key list from the markup.</summary>
    public static TheoryData<string> ViewsThatShowImages()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src", "Nfty.App", "Views"), "*.axaml"))
            if (File.ReadAllText(file).Contains("<Image ", StringComparison.Ordinal))
                data.Add(Path.GetFileNameWithoutExtension(file));
        return data;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "nfty.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [AvaloniaTheory]
    [MemberData(nameof(ViewsThatShowImages))]
    public void Every_view_that_shows_art_scales_it_with_nearest_neighbour(string viewName)
    {
        var type = typeof(Views.IngredientEditorView).Assembly
            .GetType($"Nfty.App.Views.{viewName}", throwOnError: true)!;
        var view = (Control)Activator.CreateInstance(type)!;

        // No DataContext: an unbound Image is still an Image, and the setting is inherited from the
        // root regardless of whether anything ever populates its Source. That is the point — this
        // asks what the view WOULD do with art, not what one fixture happens to show.
        var window = new Window { Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var mode = RenderOptions.GetBitmapInterpolationMode(view);
            Assert.True(mode == BitmapInterpolationMode.None,
                $"{viewName} shows art but scales it with {mode} — pixel art must scale with "
                + "nearest neighbour. Add RenderOptions.BitmapInterpolationMode=\"None\" to its root.");

            // And nothing inside may opt back in. The setting is composited during RENDERING rather
            // than stored per-visual — a child reads Unspecified and inherits its parent's at draw
            // time — so the effective mode is the nearest ancestor that states one.
            foreach (var image in view.GetVisualDescendants().OfType<Image>())
                Assert.Equal(BitmapInterpolationMode.None, Effective(image));
        }
        finally { window.Close(); }
    }

    /// <summary>The mode a visual actually draws with: its own, or the nearest ancestor that
    /// states one. <c>Unspecified</c> all the way up means Avalonia's default filter.</summary>
    private static BitmapInterpolationMode Effective(Visual visual)
    {
        for (Visual? v = visual; v is not null; v = v.GetVisualParent())
        {
            var mode = RenderOptions.GetBitmapInterpolationMode(v);
            if (mode != BitmapInterpolationMode.Unspecified) return mode;
        }
        return BitmapInterpolationMode.Unspecified;
    }

    /// <summary>The list must not silently become empty — a derived list that finds nothing passes
    /// every assertion it never makes.</summary>
    [Fact]
    public void The_derived_view_list_is_not_empty()
    {
        Assert.NotEmpty(ViewsThatShowImages());
    }
}
