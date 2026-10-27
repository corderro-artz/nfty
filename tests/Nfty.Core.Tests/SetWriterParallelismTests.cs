using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.Core.Tests;

/// <summary>Writing a Set at any thread count lands the same bytes, and the sync twin can be stopped.</summary>
public class SetWriterParallelismTests
{
    private static GeneratedSet MakeSet(int count)
    {
        var assets = new List<GeneratedAsset>();
        for (var n = 1; n <= count; n++)
        {
            var img = new Image<Rgba32>(4, 4);
            for (var y = 0; y < 4; y++)
                for (var x = 0; x < 4; x++) img[x, y] = new Rgba32((byte)(n * 7 + x), (byte)(y * 20), (byte)n, 255);
            assets.Add(new GeneratedAsset
            {
                SetNumber = n, Dna = $"dna-{n:D4}", RecipeId = n % 2 == 0 ? "cat" : "dog",
                RecipeName = n % 2 == 0 ? "Cat" : "Dog", Image = img,
                Traits = new[] { new TraitSelection("bg", "Background", $"v{n % 3}", $"V{n % 3}") },
                ColorRolls = new[] { new ColorRoll("bg", LayerKind.Dynamic, ColorModel.Hsv, n * 10.5, 0.5) },
            });
        }
        return new GeneratedSet("Pets", "desc", "PT", "seed", assets);
    }

    private static Dictionary<string, byte[]> Snapshot(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(dir, f).Replace('\\', '/'), File.ReadAllBytes,
                StringComparer.Ordinal);

    [Fact]
    public void SetWriter_writes_identical_bytes_at_one_and_many_threads()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            using (var a = MakeSet(40)) SetWriter.Write(a, Path.Combine(root.FullName, "one"), false,
                parallelism: Parallelism.Sequential);
            using (var b = MakeSet(40)) SetWriter.Write(b, Path.Combine(root.FullName, "many"), false,
                parallelism: Parallelism.All);

            var one = Snapshot(Path.Combine(root.FullName, "one"));
            var many = Snapshot(Path.Combine(root.FullName, "many"));
            Assert.Equal(one.Keys.Order(StringComparer.Ordinal), many.Keys.Order(StringComparer.Ordinal));
            Assert.True(one.Count >= 40 * 3);
            foreach (var (path, bytes) in one) Assert.True(bytes.SequenceEqual(many[path]), path);
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public void SetWriter_Write_honours_cancellation_and_reports_progress()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            using var set = MakeSet(30);
            var seen = new List<int>();
            SetWriter.Write(set, Path.Combine(root.FullName, "ok"), false,
                progress: new SyncProgress(p => seen.Add(p.Completed)), parallelism: Parallelism.All);
            Assert.Equal(Enumerable.Range(1, 30), seen);

            using var cts = new CancellationTokenSource();
            cts.Cancel();
            using var set2 = MakeSet(30);
            Assert.ThrowsAny<OperationCanceledException>(() =>
                SetWriter.Write(set2, Path.Combine(root.FullName, "stopped"), false,
                    cancellationToken: cts.Token, parallelism: Parallelism.All));
        }
        finally { root.Delete(true); }
    }

    /// <summary>Calls back inline, so ordering is the writer's, not the thread pool's.</summary>
    private sealed class SyncProgress(Action<WriteProgress> on) : IProgress<WriteProgress>
    {
        public void Report(WriteProgress value) => on(value);
    }
}
