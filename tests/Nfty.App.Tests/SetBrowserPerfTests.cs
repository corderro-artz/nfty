using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Converters;
using Nfty.App.ViewModels;
using Nfty.Core.Diagnostics;
using Nfty.Core.Generation;
using Nfty.Core.Output;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// Where the time goes when a 500-asset Set is opened and scrolled.
/// </summary>
/// <remarks>
/// <para>These are measurements, not assertions about wall-clock — a test that fails when a build
/// agent is busy is a test people delete. The two that DO assert are assertions about
/// <em>work done</em>: how many thumbnails get decoded, and whether re-chunking on a resize
/// discards the rows it just built. Both are machine-independent and both were real regressions.</para>
///
/// <para>Run with <c>dotnet test --filter FullyQualifiedName~SetBrowserPerfTests -l "console;verbosity=detailed"</c>
/// to see the table.</para>
/// </remarks>
public class SetBrowserPerfTests
{
    private const int Assets = 500;
    private readonly ITestOutputHelper _out;

    /// <summary>Creates the fixture.</summary>
    /// <param name="output">xUnit's console sink; the report goes here.</param>
    public SetBrowserPerfTests(ITestOutputHelper output) => _out = output;

    private static LoadedSet BigSet(out string dir)
    {
        dir = Directory.CreateTempSubdirectory().FullName;
        // EnforceUniqueDna off: the fixture book's space is far smaller than 500 and this is a
        // measurement of the BROWSER, not of the generator's uniqueness guarantee.
        using var set = Generator.Generate(CoreTestBook.Tiny(),
            new GenerateOptions(Assets, "perf", EnforceUniqueDna: false));
        SetWriter.Write(set, dir, pack: false);
        return SetReader.Read(dir);
    }

    [AvaloniaFact]
    public void Where_the_time_goes_opening_and_scrolling_a_500_asset_set()
    {
        Perf.Enable();
        var sw = Stopwatch.StartNew();
        var dir = "";
        try
        {
            LoadedSet loaded;
            using (Perf.Measure("00 cook + write fixture")) loaded = BigSet(out dir);

            SetBrowserViewModel vm;
            using (Perf.Measure("10 SetBrowserViewModel ctor")) vm = new SetBrowserViewModel(loaded);

            var view = new Views.SetBrowserView { DataContext = vm };
            var window = new Window { Content = view, Width = 1392, Height = 926 };
            using (Perf.Measure("20 build view + first layout"))
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
            }

            var list = view.GetVisualDescendants().OfType<ListBox>().First();

            // Two scroll shapes, because they cost two completely different things and only one of
            // them is what a user does.
            //
            // A wheel moves a little at a time, which lets the virtualizing panel RECYCLE its
            // containers. A jump the height of the viewport discards every container and builds new
            // ones. Measured: 40 wheel-sized steps cost 32 ms and 6 MB; ten viewport jumps cost
            // ~750 ms and 104 MB. Quoting the second as "scrolling is slow" would be measuring a
            // gesture nobody performs.
            using (Perf.Measure("30 scroll: 40 wheel-sized steps"))
            {
                for (var i = 0; i < 40; i++)
                {
                    list.Scroll!.Offset = list.Scroll.Offset.WithY(i * 60);
                    Dispatcher.UIThread.RunJobs();
                }
            }

            using (Perf.Measure("31 scroll: 10 viewport jumps (worst case)"))
            {
                for (var i = 0; i < 10; i++)
                {
                    list.Scroll!.Offset = list.Scroll.Offset.WithY(i % 2 == 0 ? 4000 : 0);
                    Dispatcher.UIThread.RunJobs();
                }
            }

            using (Perf.Measure("40 select each of 20 assets"))
            {
                for (var i = 0; i < 20; i++)
                {
                    vm.SelectedItem = vm.Items[i];
                    Dispatcher.UIThread.RunJobs();
                }
            }

            using (Perf.Measure("50 resize width x8"))
            {
                for (var i = 0; i < 8; i++)
                {
                    window.Width = 1392 - i * 40;
                    Dispatcher.UIThread.RunJobs();
                }
            }

            window.Close();
            vm.Dispose();
            _out.WriteLine($"total {sw.Elapsed.TotalMilliseconds:N0} ms for {Assets} assets\n");
            _out.WriteLine(Perf.Report());
        }
        finally
        {
            Perf.Disable();
            if (dir.Length > 0) Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// Opening a Set decodes only the thumbnails that are on screen.
    /// </summary>
    /// <remarks>
    /// This is the assertion that keeps the ViewModel honest: <c>SetItemRow.Thumbnail</c> is lazy so
    /// the decode falls under the ListBox's virtualization, and anything that walks every row —
    /// counting, sorting, a report — would quietly undo that and pay for 500 image decodes on open.
    /// </remarks>
    [AvaloniaFact]
    public void Opening_a_set_decodes_only_the_thumbnails_that_are_on_screen()
    {
        var loaded = BigSet(out var dir);
        using var vm = new SetBrowserViewModel(loaded);
        try
        {
            var view = new Views.SetBrowserView { DataContext = vm };
            var window = new Window { Content = view, Width = 1392, Height = 926 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                // Decoding is off the UI thread now, so realizing a row STARTS a decode rather than
                // finishing one. Pump until they land, or this test measures how fast the machine
                // is instead of how much work the grid asked for.
                var sw = Stopwatch.StartNew();
                while (!vm.Items.Take(8).All(r => r.IsThumbnailDecoded) && sw.ElapsedMilliseconds < 5000)
                {
                    Dispatcher.UIThread.RunJobs();
                    Thread.Sleep(1);
                }
                Dispatcher.UIThread.RunJobs();

                var decoded = vm.Items.Count(r => r.IsThumbnailDecoded);
                Assert.True(decoded > 0, "nothing was realized, so this test proves nothing");
                Assert.True(decoded < Assets / 4,
                    $"{decoded} of {Assets} thumbnails decoded on open — virtualization is not holding");
            }
            finally { window.Close(); }
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// Resizing to a width that still fits the same number of tiles reuses the rows it already built.
    /// </summary>
    /// <remarks>
    /// The grid is a virtualized ListBox of ROWS, chunked by <see cref="RowChunkConverter"/> from the
    /// pane's measured width. Every layout pass hands that converter a new width, and it rebuilt the
    /// whole row list each time — so dragging a window edge threw away and recreated every row
    /// container, and with it every realized tile, dozens of times a second. Same chunk size, same
    /// rows: nothing downstream should be able to tell a resize happened.
    /// </remarks>
    [AvaloniaFact]
    public void A_resize_that_does_not_change_the_row_length_reuses_the_rows()
    {
        var loaded = BigSet(out var dir);
        using var vm = new SetBrowserViewModel(loaded);
        try
        {
            var a = RowChunkConverter.Instance.Convert([vm.Items, 1000d], typeof(object), null,
                System.Globalization.CultureInfo.InvariantCulture);
            var b = RowChunkConverter.Instance.Convert([vm.Items, 1004d], typeof(object), null,
                System.Globalization.CultureInfo.InvariantCulture);

            Assert.Same(a, b);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// Exactly one row is ever flagged selected.
    /// </summary>
    /// <remarks>
    /// The handler used to assert this by brute force — it set <c>IsSelected</c> on all 500 rows on
    /// every click, which is 498 notifications saying "still false". It now touches only the two
    /// rows that can change, which is correct *because* of this invariant rather than in spite of
    /// it, so the invariant is pinned rather than assumed.
    /// </remarks>
    [AvaloniaFact]
    public void Exactly_one_row_is_flagged_selected_at_any_time()
    {
        var loaded = BigSet(out var dir);
        using var vm = new SetBrowserViewModel(loaded);
        try
        {
            Assert.Equal(1, vm.Items.Count(r => r.IsSelected));      // the constructor's default

            foreach (var i in new[] { 12, 499, 0, 250, 12 })
            {
                vm.SelectedItem = vm.Items[i];
                Assert.Equal(1, vm.Items.Count(r => r.IsSelected));
                Assert.True(vm.Items[i].IsSelected);
            }

            vm.SelectedItem = null;
            Assert.Equal(0, vm.Items.Count(r => r.IsSelected));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// How a thumbnail decode scales with the SOURCE image's size.
    /// </summary>
    /// <remarks>
    /// <para>The number that decides whether thumbnail decoding is worth engineering around, and the
    /// reason the rest of this class understates it: the shared fixture's art is 2x2, so decode
    /// measures as noise there and would in any collection of small pixel art. It is not noise in a
    /// real one.</para>
    ///
    /// <para>Measured per decode to the 128px thumbnail width: 2px and 64px sources both ~0.6 ms,
    /// 256px 1.2 ms, 512px 2.4 ms, <b>1000px 7.1 ms</b>. A screen of forty 1000px tiles is therefore
    /// ~280 ms of decoding on the UI thread every time you scroll into fresh rows — which is a stall
    /// you can feel, and the only part of a scroll that grows with the canvas the author chose.</para>
    /// </remarks>
    [AvaloniaFact]
    public void How_a_thumbnail_decode_scales_with_the_source_size()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            foreach (var size in new[] { 2, 64, 256, 512, 1000 })
            {
                var path = Path.Combine(dir, $"{size}.png");
                using (var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(size, size))
                {
                    for (var y = 0; y < size; y++)
                        for (var x = 0; x < size; x++)
                            img[x, y] = new SixLabors.ImageSharp.PixelFormats.Rgba32(
                                (byte)(x * 7), (byte)(y * 11), (byte)(x ^ y), 255);
                    SixLabors.ImageSharp.ImageExtensions.SaveAsPng(img, path);
                }

                for (var i = 0; i < 3; i++) Decode(path).Dispose();          // warm the file and the JIT
                var sw = Stopwatch.StartNew();
                for (var i = 0; i < 40; i++) Decode(path).Dispose();
                _out.WriteLine($"{size,5}px source -> {sw.Elapsed.TotalMilliseconds / 40:N3} ms per decode");
            }
        }
        finally { Directory.Delete(dir, recursive: true); }

        static Avalonia.Media.Imaging.Bitmap Decode(string path)
        {
            using var fs = File.OpenRead(path);
            return Avalonia.Media.Imaging.Bitmap.DecodeToWidth(fs, 128);
        }
    }
}
