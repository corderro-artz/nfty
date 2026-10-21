using System.IO.Compression;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Tests;

/// <summary>The streamed batch: each model is built, written and let go before the next is asked for.</summary>
public class ModelBatchExportTests
{
    private static string Temp(string prefix) => Directory.CreateTempSubdirectory(prefix).FullName;

    /// <summary>Hands out a fresh picture per open and records the order of opens and releases.</summary>
    private sealed class Pictures(params string[] rows)
    {
        public List<string> Log { get; } = new();
        public List<Image<Rgba32>> Opened { get; } = new();

        public ModelInput Open(int i)
        {
            Log.Add($"open {i}");
            var art = Art.FromAscii(rows[i]);
            Opened.Add(art);
            return new ModelInput($"ignored-{i}", art, null);
        }

        public void Release(ModelInput input)
        {
            Log.Add("release");
            input.Art.Dispose();
        }
    }

    [Fact]
    public void Each_model_is_written_and_released_before_the_next_is_opened()
    {
        var dir = Path.Combine(Temp("nfty-stream-"), "out");
        var pics = new Pictures("##", "#", "###");
        var onDiskAtOpen = new List<int>();
        ModelInput Open(int i)
        {
            onDiskAtOpen.Add(Directory.Exists(dir) ? Directory.GetFiles(dir).Length : 0);
            return pics.Open(i);
        }

        var result = ModelBatch.Export(new[] { "a", "b", "c" }, Open, pics.Release, new RiseOptions(),
            ModelDestination.Folder(dir));

        Assert.Equal(new[] { "open 0", "release", "open 1", "release", "open 2", "release" }, pics.Log);
        Assert.Equal(new[] { 0, 1, 2 }, onDiskAtOpen);     // the previous model is on disk, not held
        Assert.Equal(new[] { "a.glb", "b.glb", "c.glb" }, result.FileNames);
        Assert.True(File.Exists(Path.Combine(dir, "c.glb")));
    }

    [Fact]
    public void The_streamed_zip_holds_the_same_bytes_as_building_first_and_sorts_entries_ordinally()
    {
        using var art = Art.FromAscii("##");
        var root = Temp("nfty-stream-zip-");
        string built = Path.Combine(root, "built.zip"), streamed = Path.Combine(root, "streamed.zip");
        var options = new RiseOptions();
        ModelBatch.WriteZip(built, ModelBatch.Build(new[]
        {
            new ModelInput("b", art, null), new ModelInput("C", art, null), new ModelInput("a", art, null),
        }, options).Files);

        var pics = new Pictures("##", "##", "##");
        var result = ModelBatch.Export(new[] { "b", "C", "a" }, pics.Open, pics.Release, options,
            ModelDestination.Zip(streamed));

        Assert.Equal(File.ReadAllBytes(built), File.ReadAllBytes(streamed));
        using (var zip = ZipFile.OpenRead(streamed))
            Assert.Equal(new[] { "C.glb", "a.glb", "b.glb" }, zip.Entries.Select(e => e.FullName));
        Assert.Equal(3, result.FileNames.Count);
        Assert.Empty(result.Replaced);

        var again = new Pictures("##", "##", "##");
        var second = ModelBatch.Export(new[] { "b", "C", "a" }, again.Open, again.Release, options,
            ModelDestination.Zip(streamed));
        Assert.Equal(new[] { "streamed.zip" }, second.Replaced);
    }

    [Fact]
    public void Progress_counts_every_model_including_the_skipped()
    {
        var reports = new List<int>();
        var pics = new Pictures("##", "~~", "#");
        var result = ModelBatch.Export(new[] { "a", "b", "c" }, pics.Open, pics.Release, new RiseOptions(),
            ModelDestination.Folder(Path.Combine(Temp("nfty-stream-p-"), "out")), new Inline(reports.Add));
        Assert.Equal(new[] { 1, 2, 3 }, reports);
        Assert.Equal("b", result.Skipped.Single().Name);
        Assert.Equal(SkipReason.PartialAlpha, result.Skipped.Single().Reason);
    }

    [Fact]
    public void A_canceled_zip_leaves_nothing_behind_and_an_existing_zip_untouched()
    {
        var root = Temp("nfty-stream-cancel-");
        string zip = Path.Combine(root, "models.zip");
        File.WriteAllBytes(zip, new byte[] { 7, 7, 7 });
        using var cts = new CancellationTokenSource();
        var pics = new Pictures("#", "#", "#");
        Assert.ThrowsAny<OperationCanceledException>(() => ModelBatch.Export(new[] { "a", "b", "c" }, pics.Open,
            pics.Release, new RiseOptions(), ModelDestination.Zip(zip), new Inline(n => { if (n == 1) cts.Cancel(); }),
            cts.Token));
        Assert.Equal(new[] { "open 0", "release" }, pics.Log);
        Assert.Equal(new byte[] { 7, 7, 7 }, File.ReadAllBytes(zip));
        Assert.Equal(new[] { "models.zip" }, Directory.GetFiles(root).Select(Path.GetFileName));
    }

    [Fact]
    public void A_canceled_folder_keeps_the_models_finished_before_the_stop_and_no_partial_file()
    {
        var dir = Path.Combine(Temp("nfty-stream-cancel-dir-"), "out");
        using var cts = new CancellationTokenSource();
        var pics = new Pictures("#", "#", "#");
        Assert.ThrowsAny<OperationCanceledException>(() => ModelBatch.Export(new[] { "a", "b", "c" }, pics.Open,
            pics.Release, new RiseOptions(), ModelDestination.Folder(dir), new Inline(n => { if (n == 2) cts.Cancel(); }),
            cts.Token));
        Assert.Equal(new[] { "a.glb", "b.glb" }, Directory.GetFiles(dir).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_folder_export_names_the_files_it_replaced()
    {
        var dir = Path.Combine(Temp("nfty-stream-repl-"), "out");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "b.bin"), new byte[] { 1 });
        var pics = new Pictures("#", "#");
        var result = ModelBatch.Export(new[] { "a", "b" }, pics.Open, pics.Release,
            new RiseOptions { Format = ModelFormat.GltfWithBin }, ModelDestination.Folder(dir));
        Assert.Equal(new[] { "b.bin" }, result.Replaced);
    }

    [Fact]
    public void A_zip_of_nothing_that_rises_is_not_written()
    {
        var root = Temp("nfty-stream-none-");
        var pics = new Pictures("~", "..");
        var result = ModelBatch.Export(new[] { "a", "b" }, pics.Open, pics.Release, new RiseOptions(),
            ModelDestination.Zip(Path.Combine(root, "m.zip")));
        Assert.Empty(result.FileNames);
        Assert.Equal(2, result.Skipped.Count);
        Assert.Empty(Directory.GetFiles(root));
    }

    [Fact]
    public void A_release_runs_even_when_the_build_throws()
    {
        var pics = new Pictures("#");
        var dir = Path.Combine(Temp("nfty-stream-throw-"), "out");
        Assert.Throws<ArgumentException>(() => ModelBatch.Export(new[] { "a" },
            i => { var input = pics.Open(i); return input with { Depth = Nfty.Core.Model.DepthMap.Flat(9, 9) }; },
            pics.Release, new RiseOptions(), ModelDestination.Folder(dir)));
        Assert.Equal(new[] { "open 0", "release" }, pics.Log);
    }

    /// <summary>Reports on the calling thread (<see cref="Progress{T}"/> posts, which a test cannot wait on).</summary>
    private sealed class Inline(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}
