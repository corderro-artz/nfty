using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>
/// Ownership, streaming, progress and cancellation — the seams a GUI drives, as opposed to
/// the CLI's fire-once-and-exit use.
/// </summary>
public class GenerationLifecycleTests
{
    private static LoadedIngredient Ing(string id, params string[] variantIds) => new()
    {
        Manifest = new IngredientManifest(id, id, LayerKind.Custom, null,
            variantIds.Select(v => new Variant(v, v, 1)).ToList()),
        VariantImages = variantIds.ToDictionary(v => v, _ => new Image<Rgba32>(2, 2, new Rgba32(10, 20, 30, 255))),
    };

    private static LoadedCookBook Book() => new()
    {
        Manifest = new CookBookManifest("cb", "VaporPets", new Dimensions(2, 2),
            new Collection("VaporPets", "d", "VP"), new Dictionary<string, double> { ["cat"] = 1 }),
        Recipes = new[]
        {
            new LoadedRecipe
            {
                Manifest = new RecipeManifest("cat", "Cat", new[] { "bg", "body" },
                    Array.Empty<IncompatibilityRule>()),
                Ingredients = new[] { Ing("bg", "a", "b"), Ing("body", "x", "y") },
            },
        },
    };

    [Fact]
    public void Disposing_the_set_disposes_every_asset_image()
    {
        var set = Generator.Generate(Book(), new GenerateOptions(4, "seed-1"));
        var images = set.Assets.Select(a => a.Image).ToList();

        set.Dispose();

        foreach (var img in images)
            Assert.Throws<ObjectDisposedException>(() => img[0, 0]);
    }

    [Fact]
    public void Disposing_a_single_asset_disposes_its_image()
    {
        using var set = Generator.Generate(Book(), new GenerateOptions(1, "seed-1"));
        var asset = set.Assets[0];

        asset.Dispose();

        Assert.Throws<ObjectDisposedException>(() => asset.Image[0, 0]);
    }

    [Fact]
    public void Streaming_yields_only_what_is_enumerated()
    {
        // Taking 2 of a 4-asset request must not roll (or allocate) the other two.
        var taken = Generator.GenerateStreaming(Book(), new GenerateOptions(4, "seed-1")).Take(2).ToList();

        Assert.Equal(2, taken.Count);
        foreach (var a in taken) a.Dispose();
    }

    [Fact]
    public void Streaming_matches_the_materialising_generate()
    {
        using var set = Generator.Generate(Book(), new GenerateOptions(4, "seed-1"));
        var streamed = Generator.GenerateStreaming(Book(), new GenerateOptions(4, "seed-1")).ToList();

        Assert.Equal(set.Assets.Select(a => a.Dna), streamed.Select(a => a.Dna));
        foreach (var a in streamed) a.Dispose();
    }

    [Fact]
    public void Generate_reports_progress_for_each_asset()
    {
        var seen = new List<GenerationProgress>();
        var progress = new SynchronousProgress<GenerationProgress>(seen.Add);

        using var set = Generator.Generate(Book(), new GenerateOptions(4, "seed-1"), progress: progress);

        Assert.Equal(4, seen.Count);
        Assert.Equal(new[] { 1, 2, 3, 4 }, seen.Select(p => p.Completed));
        Assert.All(seen, p => Assert.Equal(4, p.Total));
        Assert.Equal(1.0, seen[^1].Fraction);
    }

    [Fact]
    public void Cancellation_stops_generation()
    {
        using var cts = new CancellationTokenSource();
        var progress = new SynchronousProgress<GenerationProgress>(p =>
        {
            if (p.Completed == 2) cts.Cancel();
        });

        Assert.Throws<OperationCanceledException>(
            () => Generator.Generate(Book(), new GenerateOptions(4, "seed-1"),
                progress: progress, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task GenerateAsync_matches_the_sync_result_for_the_same_seed()
    {
        using var sync = Generator.Generate(Book(), new GenerateOptions(4, "seed-1"));
        using var async = await Generator.GenerateAsync(Book(), new GenerateOptions(4, "seed-1"));

        Assert.Equal(sync.Assets.Select(a => a.Dna), async.Assets.Select(a => a.Dna));
    }

    [Fact]
    public async Task GenerateAsync_honours_cancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Generator.GenerateAsync(Book(), new GenerateOptions(4, "seed-1"),
                cancellationToken: cts.Token));
    }

    /// <summary>
    /// <see cref="Progress{T}"/> posts to the captured SynchronizationContext, which makes
    /// callbacks arrive after the assertion in a console test run. This reports inline.
    /// </summary>
    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
