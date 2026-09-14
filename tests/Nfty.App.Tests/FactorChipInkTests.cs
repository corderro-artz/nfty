using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The digit inside a factor badge sits in the middle of it.
/// </summary>
/// <remarks>
/// <para>The badge states a <c>Width</c> — 24, which is what makes the CookBook card's slot columns
/// line up and stops a two-character count drawing a wider box than a one-character one. Its inner
/// <c>TextBlock</c> stated no alignment, and a bare TextBlock is <c>Stretch</c>: it was arranged
/// across the whole 22px content box and drew its text at the LEFT of it. So every number in the
/// strip sat against its own left border with about fifteen pixels of empty ground beside it.
/// Measured on the hero at 1180px: a <c>×</c> stood 6px from the digit after it and 16px from the
/// digit before it, in a strip whose two gaps are declared equal.</para>
///
/// <para>It was invisible while the chip hugged its glyph (the first <c>Border.fchip</c> rule is
/// <c>Padding 6,3</c> and no width); the later rule that fixes the width is what turned "a box
/// sized to the text" into "a box the text sits in", and the markup still reads the same either
/// way. Which is why this measures INK off a rendered frame rather than reading the setters back —
/// a test that asserted <c>HorizontalAlignment == Center</c> would restate the fix.</para>
/// </remarks>
public class FactorChipInkTests
{
    /// <summary>The horizontal extent of everything inside <paramref name="rect"/> that is not the
    /// background — the modal color of the region, which on a washed badge is the wash.</summary>
    /// <param name="frame">A captured frame.</param>
    /// <param name="rect">The region to scan, in device pixels.</param>
    /// <returns>The first and last inked column, relative to the rect.</returns>
    private static (int Left, int Right) InkColumns(WriteableBitmap frame, PixelRect rect)
    {
        int stride = rect.Width * 4;
        var buffer = Marshal.AllocHGlobal(stride * rect.Height);
        try
        {
            frame.CopyPixels(rect, buffer, stride * rect.Height, stride);
            var bytes = new byte[stride * rect.Height];
            Marshal.Copy(buffer, bytes, 0, bytes.Length);

            var counts = new Dictionary<int, int>();
            for (int i = 0; i < bytes.Length; i += 4)
            {
                int key = bytes[i] | (bytes[i + 1] << 8) | (bytes[i + 2] << 16);
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
            int ground = counts.OrderByDescending(p => p.Value).First().Key;
            int gb = ground & 0xFF, gg = (ground >> 8) & 0xFF, gr = (ground >> 16) & 0xFF;

            int left = int.MaxValue, right = int.MinValue;
            for (int y = 0; y < rect.Height; y++)
            for (int x = 0; x < rect.Width; x++)
            {
                int i = y * stride + x * 4;
                int d = Math.Max(Math.Abs(bytes[i] - gb),
                        Math.Max(Math.Abs(bytes[i + 1] - gg), Math.Abs(bytes[i + 2] - gr)));
                if (d <= 40) continue;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
            }
            return left == int.MaxValue ? (0, -1) : (left, right);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    /// <summary>Asserts every <c>.fchip</c> under <paramref name="root"/> carries its ink in the
    /// middle of its own box, measured off <paramref name="frame"/>.</summary>
    /// <param name="frame">The captured frame.</param>
    /// <param name="root">The laid-out view the chips live in.</param>
    /// <param name="scale">The window's render scaling.</param>
    /// <param name="least">How many chips the caller expects to find.</param>
    private static void AssertCentered(WriteableBitmap frame, Visual root, double scale, int least)
    {
        var chips = root.GetVisualDescendants().OfType<Border>()
            .Where(b => b.Classes.Contains("fchip") && b.Bounds.Width > 0).ToList();
        Assert.True(chips.Count >= least, $"found {chips.Count} factor badges, expected at least {least}");

        foreach (var chip in chips)
        {
            // The interior, inside the 1px border - the border is ink by any measure and would put
            // an equal mark on both ends of every scan.
            var a = chip.TranslatePoint(new Point(2, 2), root)!.Value;
            var b = chip.TranslatePoint(new Point(chip.Bounds.Width - 2, chip.Bounds.Height - 2), root)!.Value;
            var rect = new PixelRect((int)Math.Round(a.X * scale), (int)Math.Round(a.Y * scale),
                                     (int)Math.Round((b.X - a.X) * scale), (int)Math.Round((b.Y - a.Y) * scale));

            var (left, right) = InkColumns(frame, rect);
            string text = chip.GetVisualDescendants().OfType<TextBlock>().First().Text ?? "";
            Assert.True(right >= left, $"badge '{text}' has no ink in it at all");

            double inkCenter = (left + right + 1) / 2.0;
            double boxCenter = rect.Width / 2.0;

            // One device pixel of slack: a 7px glyph cannot be exactly centered in a 22px box, and
            // the layout rounds. Left-flush is off by about eight, so this is not a close call.
            Assert.True(Math.Abs(inkCenter - boxCenter) <= 1.0,
                $"badge '{text}' draws its ink at {inkCenter:F1} of a {rect.Width}px box "
                + $"(center {boxCenter:F1}); columns {left}..{right}");
        }
    }

    /// <summary>The Recipe hero's own strip — the screen the misalignment was reported from.</summary>
    [AvaloniaFact]
    public void The_recipe_hero_centers_every_factor_badge()
    {
        var (book, recipe) = VisualCapture.RecipeWithRules();
        using var vm = new RecipeDetailViewModel(recipe, book, new ImageBridge(), _ => { });
        var view = new Views.RecipeDetailView { DataContext = vm };
        var window = new Window
        {
            RequestedThemeVariant = ThemeVariant.Dark,
            Content = view,
            Width = 1180,
            Height = 720,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            using var frame = window.CaptureRenderedFrame()!;
            AssertCentered(frame, view, window.RenderScaling, least: 2);
        }
        finally { window.Close(); book.Dispose(); }
    }

    /// <summary>
    /// The CookBook card's slot grid, where the same class carries a TWO-character <c>+N</c>.
    /// </summary>
    /// <remarks>
    /// <para>This card is where the fix came from rather than where it was needed: its template
    /// carried an inline <c>HorizontalAlignment="Center"</c> on the badge's TextBlock, so the card
    /// was right the whole time and only the Recipe hero - the same class, the same 24px box, no
    /// inline - drew its numbers against the left border. The inline is gone now and the style says
    /// it once, which is what makes this test worth having: it fails if the shared rule is removed.
    /// <c>HorizontalAlignment</c> is the load-bearing half (it shrinks the box to the glyph and
    /// places it); <c>TextAlignment</c> is belt and braces for anything that arranges the box wider
    /// than its text, the two-character <c>+N</c> included.</para>
    /// </remarks>
    [AvaloniaFact]
    public void The_cookbook_cards_badges_are_centered_including_the_overflow_one()
    {
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        var session = new CookBookSession();
        using var book = DnaSpaceLayoutTests.ManyRecipes();
        using var explorer = new ExplorerViewModel(book, nav, dialogs, new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav), ExplorerViewModelTests.CookFactory(dialogs),
            session, new FilePickerService(),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), new StatusService());

        var view = new Views.ExplorerView { DataContext = explorer };
        var window = new Window
        {
            RequestedThemeVariant = ThemeVariant.Dark,
            Content = view,
            Width = 1920,
            Height = 1080,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var card = view.GetVisualDescendants().OfType<Views.CookBookDetailView>().First();
            Assert.Contains(card.GetVisualDescendants().OfType<Border>(),
                b => b.Classes.Contains("more"));

            using var frame = window.CaptureRenderedFrame()!;
            AssertCentered(frame, view, window.RenderScaling, least: 12);
        }
        finally { window.Close(); }
    }
}
