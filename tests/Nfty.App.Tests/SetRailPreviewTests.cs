using System;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Output;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// THE RAIL SHOWS THE ASSET IT IS DESCRIBING.
/// </summary>
/// <remarks>
/// <para>A grid of a hundred tiles is SCANNED rather than read, and the rail already followed the
/// pointer — but it answered with an id, a hash and a table, so working out what you were actually
/// looking at meant going back to the tile you had just left. The preview binds to
/// <c>Shown</c>, which is already "the one under the pointer, or the one that is kept", so hover,
/// right-click-to-keep and click-through-to-the-inspector all move it for free rather than each
/// needing their own wiring.</para>
///
/// <para>It binds straight through to <c>Shown.Thumbnail</c> rather than mirroring it onto a
/// property here. A row's image arrives off the thread pool and raises its own change; a snapshot
/// taken when <c>Shown</c> moved would show the placeholder forever on any tile whose decode had not
/// landed yet.</para>
/// </remarks>
public class SetRailPreviewTests
{
    /// <summary>A tiny cooked Set on disk, the way every other Set-browser test builds one.</summary>
    private static LoadedSet CookedSet(out string dir)
    {
        dir = Directory.CreateTempSubdirectory().FullName;
        using var set = Generator.Generate(CoreTestBook.Tiny(), new GenerateOptions(2, "seed1"));
        SetWriter.Write(set, dir, pack: false);
        return SetReader.Read(dir);
    }

    private static void PumpUntil(Func<bool> done, int timeoutMs = 5000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && sw.ElapsedMilliseconds < timeoutMs)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static Image Preview(Visual view) => view.GetVisualDescendants()
        .OfType<Border>().First(b => b.Classes.Contains("railshot"))
        .GetVisualDescendants().OfType<Image>().First();

    [AvaloniaFact]
    public void The_preview_follows_the_hover_and_falls_back_to_the_selection()
    {
        var loaded = CookedSet(out var dir);
        var vm = new SetBrowserViewModel(loaded);
        try
        {
            // Opens on the first asset, which is what the rail describes with nothing hovered.
            Assert.Same(vm.Items[0], vm.Shown);

            vm.HoveredItem = vm.Items[1];
            Assert.Same(vm.Items[1], vm.Shown);
            Assert.True(vm.IsShowingHover);

            // Moving off the tiles hands the rail back to the kept asset rather than blanking it.
            vm.HoveredItem = null;
            Assert.Same(vm.Items[0], vm.Shown);
            Assert.False(vm.IsShowingHover);

            // And right-click-to-keep moves both the selection and what the rail shows.
            vm.SelectedItem = vm.Items[1];
            Assert.Same(vm.Items[1], vm.Shown);
        }
        finally { vm.Dispose(); Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// THE IMAGE IS REALLY DRAWN, and it is the shown asset's own. Asserting on
    /// <c>Shown</c> alone would pass on a rail that never got a preview at all.
    /// </summary>
    [AvaloniaFact]
    public void The_rail_draws_the_shown_assets_own_image()
    {
        var loaded = CookedSet(out var dir);
        var vm = new SetBrowserViewModel(loaded);
        var view = new Views.SetBrowserView { DataContext = vm };
        var window = new Window { Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var preview = Preview(view);

            PumpUntil(() => preview.Source is not null);
            Assert.NotNull(preview.Source);
            Assert.Same(vm.Items[0].Thumbnail, preview.Source);

            vm.HoveredItem = vm.Items[1];
            Dispatcher.UIThread.RunJobs();
            PumpUntil(() => ReferenceEquals(preview.Source, vm.Items[1].Thumbnail));

            Assert.Same(vm.Items[1].Thumbnail, preview.Source);
        }
        finally { vm.Dispose(); window.Close(); Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// THE SAME NUMBER, SAID TWO WAYS. A percentage compares traits to each other; odds are what
    /// "how rare is this one" actually asks, and 4.17% against 2.08% reads as a near-miss where
    /// "1 in 24" against "1 in 48" does not.
    /// </summary>
    /// <remarks>
    /// The odds are DERIVED from the percentage rather than stored beside it, so the two cannot
    /// drift — and they are rounded to whole assets, because you cannot own a fraction of one.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(50.0, false, "50%")]
    [InlineData(50.0, true, "1 in 2")]
    [InlineData(4.17, true, "1 in 24")]
    [InlineData(2.08, true, "1 in 48")]
    [InlineData(100.0, true, "1 in 1")]
    // A trait no asset carries has no odds, and infinity is not an answer anyone wants on a card.
    [InlineData(0.0, true, "—")]
    public void A_share_reads_as_a_percentage_or_as_odds(double pct, bool odds, string expected)
    {
        var text = Nfty.App.Converters.RarityUnitConverter.Instance.Convert(
            new object?[] { pct, odds }, typeof(string), null,
            System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(expected, text);
    }

    /// <summary>The toggle drives the column's header as well as its cells — a column of "1 in 24"
    /// under a "%" heading is two statements about one number.</summary>
    [AvaloniaFact]
    public void The_column_header_follows_the_unit()
    {
        var loaded = CookedSet(out var dir);
        var vm = new SetBrowserViewModel(loaded);
        try
        {
            Assert.False(vm.ShowRarityAsOdds);
            Assert.Equal("%", vm.RarityUnitLabel);

            vm.ShowRarityOddsCommand.Execute(null);
            Assert.True(vm.ShowRarityAsOdds);
            Assert.Equal("ODDS", vm.RarityUnitLabel);

            vm.ShowRarityPercentCommand.Execute(null);
            Assert.Equal("%", vm.RarityUnitLabel);
        }
        finally { vm.Dispose(); Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// WITH NO SOURCE BOOK, THE COMBINED CHANCE IS THE PRODUCT OF EVERY ROW IN THE TABLE, recipe
    /// share included — and it says so with a <c>~</c>.
    /// </summary>
    /// <remarks>
    /// This is the question a rarity table cannot answer by being read: the lock is 1 in 4 and the
    /// bands are 1 in 3 and nothing there says what the whole asset is worth. Without the book it is
    /// built from exactly the numbers shown above it, so a reader can check it by hand — and it
    /// assumes the layers roll independently, which is what the tilde and the tooltip are for.
    /// </remarks>
    [AvaloniaFact]
    public void The_combined_chance_is_the_product_of_the_assets_traits()
    {
        var loaded = CookedSet(out var dir);
        var vm = new SetBrowserViewModel(loaded);
        try
        {
            double p = 1.0;
            foreach (var r in vm.Shown!.Item.Rarity) p *= r.RarityPct / 100.0;
            long expected = (long)Math.Round(1.0 / p, MidpointRounding.AwayFromZero);

            vm.ShowRarityOddsCommand.Execute(null);
            Assert.Equal($"this combo ~1 in {expected:N0}", vm.CombinedText);
            Assert.StartsWith("Estimated:", vm.CombinedTip, StringComparison.Ordinal);

            // And the same number the other way up, under the same toggle as everything else.
            vm.ShowRarityPercentCommand.Execute(null);
            Assert.StartsWith("this combo ~", vm.CombinedText, StringComparison.Ordinal);
            Assert.EndsWith("%", vm.CombinedText, StringComparison.Ordinal);
        }
        finally { vm.Dispose(); Directory.Delete(dir, recursive: true); }
    }

    /// <summary>It is rarer than any single trait it is built from — the assertion that catches a
    /// product computed the wrong way up, which would otherwise look plausible.</summary>
    [AvaloniaFact]
    public void The_whole_asset_is_rarer_than_its_rarest_trait()
    {
        var loaded = CookedSet(out var dir);
        var vm = new SetBrowserViewModel(loaded);
        try
        {
            vm.ShowRarityOddsCommand.Execute(null);
            var rarest = vm.Shown!.Item.Rarity.Where(r => r.RarityPct > 0)
                .OrderBy(r => r.RarityPct).First();

            long combined = long.Parse(
                vm.CombinedText.Replace("this combo ~1 in ", "", StringComparison.Ordinal)
                  .Replace(",", "", StringComparison.Ordinal),
                System.Globalization.CultureInfo.InvariantCulture);

            Assert.True(combined >= Math.Round(100.0 / rarest.RarityPct),
                $"the whole asset ({combined}) came out commoner than its rarest trait");
        }
        finally { vm.Dispose(); Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// ONE TOGGLE, EVERY RARITY ON THE SCREEN — the column header, the rarest-trait line and the
    /// asset's own combined chance, which sit in two different panels.
    /// </summary>
    /// <remarks>
    /// That is why the control is in the page header rather than bolted to the rarity table: a
    /// toggle inside one panel says it belongs to that panel, and this one does not. A reader who
    /// switches to odds and then finds one figure still in percent has been told the screen is
    /// inconsistent about its own numbers.
    /// </remarks>
    [AvaloniaFact]
    public void One_toggle_changes_every_rarity_on_the_screen()
    {
        var loaded = CookedSet(out var dir);
        var vm = new SetBrowserViewModel(loaded);
        try
        {
            Assert.Equal("%", vm.RarityUnitLabel);
            Assert.Contains("%", vm.RarestText, StringComparison.Ordinal);
            Assert.Contains("%", vm.CombinedText, StringComparison.Ordinal);
            Assert.DoesNotContain("1 in ", vm.RarestText, StringComparison.Ordinal);
            Assert.DoesNotContain("1 in ", vm.CombinedText, StringComparison.Ordinal);

            vm.ShowRarityOddsCommand.Execute(null);

            Assert.Equal("ODDS", vm.RarityUnitLabel);
            Assert.Contains("1 in ", vm.RarestText, StringComparison.Ordinal);
            Assert.Contains("1 in ", vm.CombinedText, StringComparison.Ordinal);
            Assert.DoesNotContain("%", vm.RarestText, StringComparison.Ordinal);
            Assert.DoesNotContain("%", vm.CombinedText, StringComparison.Ordinal);
        }
        finally { vm.Dispose(); Directory.Delete(dir, recursive: true); }
    }

    /// <summary>The control lives in the PAGE header, beside Export — not inside the rarity panel it
    /// used to sit in, because it governs a figure in the panel above that one too.</summary>
    [AvaloniaFact]
    public void The_toggle_is_in_the_page_header()
    {
        var loaded = CookedSet(out var dir);
        var vm = new SetBrowserViewModel(loaded);
        var view = new Views.SetBrowserView { DataContext = vm };
        var window = new Window { Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var tray = view.GetVisualDescendants().OfType<Border>()
                .Where(b => b.Classes.Contains("seg")).ToList();
            var only = Assert.Single(tray);          // exactly one, not one per panel

            // Above the rail's own rarity table, and on the same line as Export.
            var export = view.GetVisualDescendants().OfType<Button>()
                .First(b => b.Classes.Contains("tbtn"));
            var trayTop = only.TranslatePoint(default, view)!.Value.Y;
            var exportTop = export.TranslatePoint(default, view)!.Value.Y;

            Assert.True(Math.Abs(trayTop - exportTop) < 20,
                $"the toggle at {trayTop:0} is not on Export's line at {exportTop:0}");
            Assert.True(trayTop < 80, $"the toggle at {trayTop:0} is not in the page header");
        }
        finally { vm.Dispose(); window.Close(); Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// RAREST is the rarest TRAIT, not a score. Multiplying the shares would assume the layers roll
    /// independently, which incompatibility rules and absent-percents both break; a rarity score is
    /// a convention this project has never adopted and would have to invent. The rarest trait needs
    /// no assumption — it is a fact already in the table, and the one a reader scans it to find.
    /// </summary>
    [AvaloniaFact]
    public void The_rarest_line_names_the_rarest_trait_in_the_active_unit()
    {
        var loaded = CookedSet(out var dir);
        var vm = new SetBrowserViewModel(loaded);
        try
        {
            var rarity = vm.Shown!.Item.Rarity;
            var rarest = rarity.Where(r => r.RarityPct > 0).OrderBy(r => r.RarityPct).First();

            Assert.Contains(rarest.Value, vm.RarestText, StringComparison.Ordinal);
            Assert.Contains("%", vm.RarestText, StringComparison.Ordinal);

            vm.ShowRarityOddsCommand.Execute(null);
            Assert.Contains(rarest.Value, vm.RarestText, StringComparison.Ordinal);
            Assert.Contains("1 in ", vm.RarestText, StringComparison.Ordinal);
        }
        finally { vm.Dispose(); Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// The identity block is ONE HEIGHT for every asset. The rail changes as the pointer crosses the
    /// grid, which is the worst possible place for a reflow — the "hovering / selected" line beside
    /// the preview is reserved for the same reason, and a preview that sized itself to its art would
    /// have undone both.
    /// </summary>
    [AvaloniaFact]
    public void The_preview_is_the_same_size_whatever_it_is_showing()
    {
        var loaded = CookedSet(out var dir);
        var vm = new SetBrowserViewModel(loaded);
        var view = new Views.SetBrowserView { DataContext = vm };
        var window = new Window { Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var shot = view.GetVisualDescendants().OfType<Border>()
                .First(b => b.Classes.Contains("railshot"));
            var first = shot.Bounds;
            Assert.True(first.Width > 0 && first.Height > 0);

            foreach (var item in vm.Items)
            {
                vm.HoveredItem = item;
                Dispatcher.UIThread.RunJobs();
                PumpUntil(() => item.Thumbnail is not null);
                Assert.Equal(first, shot.Bounds);
            }
        }
        finally { vm.Dispose(); window.Close(); Directory.Delete(dir, recursive: true); }
    }
}
