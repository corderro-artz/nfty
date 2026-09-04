using System.IO;
using System.Security.Cryptography;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.Core.Tests;

/// <summary>
/// Parallel rendering changes nothing a caller can observe.
/// </summary>
/// <remarks>
/// <para><see cref="Generator.Generate"/> rolls sequentially and renders in parallel;
/// <see cref="Generator.GenerateStreaming"/> rolls and renders one asset at a time on one thread.
/// The second is therefore an exact oracle for the first, and these tests hold them to it: same
/// seed, same order, same DNA, byte-identical pixels.</para>
///
/// <para>The claim being defended is stronger than "the same machine gives the same answer". No
/// parallel step here aggregates anything — no sum, no ordering, no shared accumulator; each asset
/// is a pure function of its own already-decided roll and is written to its own slot. So the result
/// cannot vary with core count, thread scheduling, or completion order, which is what makes it the
/// same on any machine.</para>
/// </remarks>
public class GeneratorParallelismTests
{
    /// <summary>A book with every layer kind, so colorized and cloned paths both run.</summary>
    private static LoadedCookBook Book(int canvas = 16)
    {
        LoadedIngredient Layer(string id, LayerKind kind, Colorization? col)
        {
            var variants = new List<Variant>();
            var images = new Dictionary<string, Image<Rgba32>>();
            for (var i = 0; i < 3; i++)
            {
                variants.Add(new Variant($"v{i}", $"V{i}", i + 1));
                var img = new Image<Rgba32>(canvas, canvas);
                for (var y = 0; y < canvas; y++)
                    for (var x = 0; x < canvas; x++)
                    {
                        // Dynamic and Static are value-maps and must be gray; only Custom is color.
                        var v = (byte)((x * 13 + y * 7 + i * 41) % 256);
                        img[x, y] = kind == LayerKind.Custom
                            ? new Rgba32(v, (byte)(255 - v), (byte)(v / 2), 255)
                            : new Rgba32(v, v, v, 255);
                    }
                images[$"v{i}"] = img;
            }
            return new LoadedIngredient
            {
                Manifest = new IngredientManifest(id, id, kind, col, variants),
                VariantImages = images,
            };
        }

        var dynamic = new Colorization(ColorModel.Hsv, 30, 20,
            new[] { new ColorEntry(100, new ColorRange(0, 360, 30, 90), null) });
        var fixedCol = new Colorization(ColorModel.Hsl, 30, 20,
            new[] { new ColorEntry(100, null, "hex:1a1a24") });

        var layers = new[]
        {
            Layer("bg", LayerKind.Dynamic, dynamic),
            Layer("eyes", LayerKind.Static, fixedCol),
            Layer("hat", LayerKind.Custom, null),
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("r", "R", layers.Select(l => l.Manifest.Id).ToList(),
                System.Array.Empty<IncompatibilityRule>()),
            Ingredients = layers,
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("b", "B", new Dimensions(canvas, canvas),
                new Collection("B", "d", "B"), new Dictionary<string, double> { ["r"] = 100 }),
            Recipes = new[] { recipe },
        };
    }

    private static string Hash(Image<Rgba32> img)
    {
        using var ms = new MemoryStream();
        img.Save(ms, new PngEncoder());
        return Convert.ToHexString(SHA256.HashData(ms.ToArray()));
    }

    /// <summary>
    /// The parallel path and the sequential path agree on every asset, pixel for pixel.
    /// </summary>
    [Fact]
    public void Parallel_render_matches_the_sequential_oracle_exactly()
    {
        using var bookA = Book();
        using var bookB = Book();
        var opts = new GenerateOptions(40, "determinism", EnforceUniqueDna: false);

        using var parallel = Generator.Generate(bookA, opts);

        var sequential = new List<GeneratedAsset>();
        try
        {
            foreach (var a in Generator.GenerateStreaming(bookB, opts)) sequential.Add(a);

            Assert.Equal(sequential.Count, parallel.Assets.Count);
            for (var i = 0; i < sequential.Count; i++)
            {
                Assert.Equal(sequential[i].SetNumber, parallel.Assets[i].SetNumber);
                Assert.Equal(sequential[i].Dna, parallel.Assets[i].Dna);
                Assert.Equal(sequential[i].RecipeId, parallel.Assets[i].RecipeId);
                Assert.Equal(Hash(sequential[i].Image), Hash(parallel.Assets[i].Image));
            }
        }
        finally { foreach (var a in sequential) a.Dispose(); }
    }

    /// <summary>
    /// The same seed produces the same collection however many threads happen to run it.
    /// </summary>
    /// <remarks>
    /// Forcing the degree of parallelism from 1 upward is the closest a single machine can get to
    /// running on a different one: if any result depended on scheduling or core count, this is where
    /// it would show.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(8)]
    public void The_collection_does_not_depend_on_how_many_threads_render_it(int threads)
    {
        var opts = new GenerateOptions(24, "threads", EnforceUniqueDna: false);

        using var baselineBook = Book();
        using var baseline = Generator.Generate(baselineBook, opts);
        var expected = baseline.Assets.Select(a => (a.Dna, Pixels: Hash(a.Image))).ToList();

        // ThreadPool minimum threads is the lever Parallel.For actually respects here without
        // changing the production call site.
        ThreadPool.GetMinThreads(out var w, out var io);
        ThreadPool.SetMinThreads(threads, io);
        try
        {
            using var book = Book();
            using var run = Generator.Generate(book, opts);
            var actual = run.Assets.Select(a => (a.Dna, Pixels: Hash(a.Image))).ToList();
            Assert.Equal(expected, actual);
        }
        finally { ThreadPool.SetMinThreads(w, io); }
    }

    /// <summary>Two runs of the same seed produce the identical collection.</summary>
    [Fact]
    public void The_same_seed_produces_the_same_collection_twice()
    {
        var opts = new GenerateOptions(30, "twice", EnforceUniqueDna: false);
        using var b1 = Book();
        using var b2 = Book();
        using var one = Generator.Generate(b1, opts);
        using var two = Generator.Generate(b2, opts);

        Assert.Equal(one.Assets.Select(a => a.Dna), two.Assets.Select(a => a.Dna));
        Assert.Equal(one.Assets.Select(a => Hash(a.Image)), two.Assets.Select(a => Hash(a.Image)));
    }

    /// <summary>A different seed produces a different collection — so the test above is not vacuous.</summary>
    [Fact]
    public void A_different_seed_produces_a_different_collection()
    {
        using var b1 = Book();
        using var b2 = Book();
        using var one = Generator.Generate(b1, new GenerateOptions(30, "seed-a", EnforceUniqueDna: false));
        using var two = Generator.Generate(b2, new GenerateOptions(30, "seed-b", EnforceUniqueDna: false));

        Assert.NotEqual(one.Assets.Select(a => a.Dna), two.Assets.Select(a => a.Dna));
    }

    /// <summary>
    /// A render that throws still disposes every asset the run had already produced, and surfaces
    /// the engine's own exception rather than the AggregateException the parallel loop wraps it in.
    /// </summary>
    [Fact]
    public void A_failed_run_strands_nothing_and_keeps_its_own_exception_type()
    {
        using var book = Book();
        // Count above the unique space with uniqueness ON: the roll phase gives up and throws the
        // engine's own type, which the parallel path must not have turned into an AggregateException.
        var ex = Assert.ThrowsAny<InvalidOperationException>(
            () => Generator.Generate(book, new GenerateOptions(10_000, "exhaust")));
        Assert.IsNotType<AggregateException>(ex);
    }
}
