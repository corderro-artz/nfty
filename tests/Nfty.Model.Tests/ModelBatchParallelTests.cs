using System.IO.Compression;
using Nfty.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Tests;

/// <summary>
/// The windowed batch: meshes build on worker threads, files land in the destination strictly in
/// ordinal order, and nothing about the bytes depends on how many threads built them.
/// </summary>
public class ModelBatchParallelTests
{
    public static TheoryData<int> Degrees => new() { 1, 2, 3, 8 };

    private static string Temp(string prefix) => Directory.CreateTempSubdirectory(prefix).FullName;

    /// <summary>Distinct shapes per index, a few that cannot rise, and names that do NOT sort in index order.</summary>
    private static string Row(int i) => (i % 7) switch
    {
        3 => "~",                                  // partial alpha: skipped
        5 => "..",                                 // nothing solid: skipped
        _ => new string('#', 1 + i % 5) + (i % 2 == 0 ? "." : "#"),
    };

    private static IReadOnlyList<string> Names(int count) =>
        Enumerable.Range(0, count).Select(i => $"pic-{(i * 7) % count:D2}").ToList();

    /// <summary>A thread-safe source that records how many pictures are open at once.</summary>
    private sealed class Source(int count)
    {
        private int _open;
        private int _max;
        public int Max => Volatile.Read(ref _max);
        public int Released;

        public ModelInput Open(int i)
        {
            var now = Interlocked.Increment(ref _open);
            int seen;
            while (now > (seen = Volatile.Read(ref _max)) && Interlocked.CompareExchange(ref _max, now, seen) != seen) { }
            return new ModelInput($"ignored-{i}", Art.FromAscii(Row(i % count)), null);
        }

        public void Release(ModelInput input)
        {
            input.Art.Dispose();
            Interlocked.Decrement(ref _open);
            Interlocked.Increment(ref Released);
        }
    }

    private sealed class Inline(Action<int> on) : IProgress<int>
    {
        public void Report(int value) => on(value);
    }

    [Theory]
    [MemberData(nameof(Degrees))]
    public void The_windowed_zip_has_the_bytes_of_the_sequential_one_at_any_thread_count(int degree)
    {
        const int count = 30;
        var names = Names(count);
        var root = Temp("nfty-win-zip-");
        var options = new RiseOptions();
        var seq = new Source(count);
        var sequentialWritten = new List<string>();
        var sequential = ModelBatch.Export(names, seq.Open, seq.Release, options,
            ModelDestination.Zip(Path.Combine(root, "seq.zip")), onWritten: sequentialWritten.Add);

        var src = new Source(count);
        var progress = new List<int>();
        var written = new List<string>();
        var windowed = ModelBatch.Export(names, src.Open, src.Release, options,
            ModelDestination.Zip(Path.Combine(root, "win.zip")), new Parallelism(degree),
            new Inline(progress.Add), onWritten: written.Add);

        Assert.True(sequential.Skipped.Count >= 4);
        Assert.Equal(File.ReadAllBytes(Path.Combine(root, "seq.zip")), File.ReadAllBytes(Path.Combine(root, "win.zip")));
        Assert.Equal(sequential.FileNames, windowed.FileNames);
        Assert.Equal(sequentialWritten, written);                       // write order, not completion order
        Assert.Equal(sequential.Skipped.Select(s => s.ToString()), windowed.Skipped.Select(s => s.ToString()));
        Assert.Equal(Enumerable.Range(1, count), progress);              // one at a time, increasing
        Assert.Equal(count, src.Released);
    }

    [Theory]
    [MemberData(nameof(Degrees))]
    public void The_windowed_folder_has_the_files_of_the_sequential_one(int degree)
    {
        const int count = 20;
        var names = Names(count);
        var options = new RiseOptions { Format = ModelFormat.GltfWithBin };
        var a = Path.Combine(Temp("nfty-win-dir-"), "out");
        var b = Path.Combine(Temp("nfty-win-dir-"), "out");
        var s1 = new Source(count);
        var s2 = new Source(count);

        ModelBatch.Export(names, s1.Open, s1.Release, options, ModelDestination.Folder(a));
        ModelBatch.Export(names, s2.Open, s2.Release, options, ModelDestination.Folder(b), new Parallelism(degree));

        var files = Directory.GetFiles(a).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(files, Directory.GetFiles(b).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        foreach (var f in files!)
            Assert.Equal(File.ReadAllBytes(Path.Combine(a, f!)), File.ReadAllBytes(Path.Combine(b, f!)));
    }

    [Fact]
    public void At_most_two_degrees_of_pictures_are_open_at_once()
    {
        var parallelism = new Parallelism(3);
        var src = new Source(60);
        ModelBatch.Export(Names(60), src.Open, src.Release, new RiseOptions(),
            ModelDestination.Folder(Path.Combine(Temp("nfty-win-bound-"), "out")), parallelism);
        Assert.InRange(src.Max, 1, parallelism.Effective * 2);
    }

    [Fact]
    public void One_thread_keeps_the_calling_thread_and_opens_one_picture_at_a_time()
    {
        var caller = Environment.CurrentManagedThreadId;
        var threads = new List<int>();
        var src = new Source(10);
        ModelBatch.Export(Names(10), i => { threads.Add(Environment.CurrentManagedThreadId); return src.Open(i); },
            src.Release, new RiseOptions(), ModelDestination.Folder(Path.Combine(Temp("nfty-win-seq-"), "out")),
            Parallelism.Sequential);
        Assert.All(threads, t => Assert.Equal(caller, t));
        Assert.Equal(1, src.Max);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    public void When_several_pictures_fail_the_lowest_ordinal_is_named_and_every_opened_one_is_released(int degree)
    {
        const int count = 16;
        var names = Enumerable.Range(0, count).Select(i => $"p{i:D2}").ToList();   // ordinal order == index order
        var src = new Source(count);
        int opened = 0;
        ModelInput Open(int i)
        {
            if (i is 5 or 9) throw new InvalidOperationException($"bad {i}");
            Interlocked.Increment(ref opened);
            return src.Open(i);
        }

        var ex = Assert.Throws<InvalidOperationException>(() => ModelBatch.Export(names, Open, src.Release,
            new RiseOptions(), ModelDestination.Zip(Path.Combine(Temp("nfty-win-fail-"), "m.zip")), new Parallelism(degree)));

        Assert.Equal("bad 5", ex.Message);
        Assert.Equal(opened, src.Released);
    }
}
