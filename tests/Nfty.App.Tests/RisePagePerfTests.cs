using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Nfty.App.Rise;
using Nfty.App.ViewModels;
using Nfty.Core.Diagnostics;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// Where the time goes on the Rise page at 64, 256 and 512 pixel art: a single depth click, a drag's
/// previews, the 3D view's repaint. Measured, never asserted as wall-clock; the only assertions are that
/// every scope the page is expected to report really does.
/// </summary>
/// <remarks>Run with <c>dotnet test tests/Nfty.App.Tests --filter FullyQualifiedName~RisePagePerf -l "console;verbosity=detailed"</c>.</remarks>
public class RisePagePerfTests
{
    private readonly ITestOutputHelper _out;

    /// <summary>Creates the fixture.</summary>
    /// <param name="output">xUnit's console sink; the table goes here.</param>
    public RisePagePerfTests(ITestOutputHelper output) => _out = output;

    private static Image<Rgba32> Disc(int size)
    {
        var img = new Image<Rgba32>(size, size);
        float c = (size - 1) / 2f, r = (size / 2f) - 1;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = MathF.Sqrt(((x - c) * (x - c)) + ((y - c) * (y - c)));
                img[x, y] = d <= r
                    ? new Rgba32((byte)(x * 255 / size), (byte)(y * 255 / size), 128, 255)
                    : new Rgba32(0, 0, 0, 0);
            }
        return img;
    }

    private static RiseViewModel Page(int size)
    {
        var body = new LoadedIngredient
        {
            Manifest = new IngredientManifest("body", "Body", LayerKind.Custom, null,
                new[] { new Variant("disc", "Disc", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["disc"] = Disc(size) },
            Depths = new Dictionary<string, DepthMap>(),
        };
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Disc", new Dimensions(size, size),
                new Collection("Disc", "", "D"), new Dictionary<string, double> { ["chest"] = 100 }),
            Recipes = new[]
            {
                new LoadedRecipe
                {
                    Manifest = new RecipeManifest("chest", "Chest", new[] { "body" }, Array.Empty<IncompatibilityRule>()),
                    Ingredients = new[] { body },
                },
            },
        };
        var (s, _) = RiseFixtures.OnDisk(book);
        return RisePages.Vm(RiseFixtures.Variant(s, "disc", () => true));
    }

    /// <summary>Paints, drags, swaps and orbits at the given art size and prints the Perf table.</summary>
    /// <param name="size">The art's side in pixels.</param>
    [AvaloniaTheory]
    [InlineData(64)]
    [InlineData(256)]
    [InlineData(512)]
    public void Where_the_time_goes_painting_depth_and_repainting_the_3D_view(int size)
    {
        var vm = Page(size);
        using var page = RisePages.Show(vm, 1180, 720);
        Perf.Enable();
        try
        {
            Perf.Reset();
            int mid = size / 2;
            for (int i = 0; i < 20; i++)
                vm.ApplyDepthStroke(new[] { (mid - 10 + i, mid) }, DepthSide.Front, lower: false);

            var drag = Enumerable.Range(0, 40).Select(i => (mid - 20 + i, mid + 5)).ToList();
            for (int n = 1; n <= drag.Count; n++)
            {
                vm.PreviewDepthStroke(drag.Take(n).ToList(), DepthSide.Back, lower: false);
                Dispatcher.UIThread.RunJobs();
            }
            vm.ApplyDepthStroke(drag, DepthSide.Back, lower: false);

            vm.SwapPreviewCommand.Execute(null);
            for (int i = 0; i < 10; i++)
            {
                vm.Preview.Orbit(new Vector2(6, 2));
                Dispatcher.UIThread.RunJobs();
            }
            Dispatcher.UIThread.RunJobs();

            var seen = Perf.Snapshot().Select(e => e.Name).ToHashSet();
            foreach (var scope in new[] { "Rise.DepthClick", "Rise.MeshRebuild", "Riser.TryBuild", "ModelPreviewView.Repaint" })
                Assert.Contains(scope, seen);
            _out.WriteLine($"=== Rise page, {size}px art ===");
            _out.WriteLine(Perf.Report());
        }
        finally { Perf.Disable(); }
    }
}
