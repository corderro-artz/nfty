using Nfty.Core.Diagnostics;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using Nfty.Core.Publish;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.Core.Tests;

/// <summary>
/// <c>set-index.txt</c> is a cache of <c>nfty/*.json</c>: preferred by <c>ReadExisting</c> only when it
/// matches the directory exactly, ignored (never trusted) otherwise. Whatever the index does, the
/// answer must equal the per-file walk's.
/// </summary>
public class SetIndexTests
{
    private const string IndexName = "set-index.txt";

    private static GeneratedSet MakeSet(int count, int first = 1)
    {
        var assets = new List<GeneratedAsset>();
        for (var n = first; n < first + count; n++)
        {
            var img = new Image<Rgba32>(1, 1);
            img[0, 0] = new Rgba32((byte)(n % 250), 10, 20, 255);
            assets.Add(new GeneratedAsset
            {
                SetNumber = n, Dna = $"dna-{n:D6}", RecipeId = n % 2 == 0 ? "cat" : "dog",
                RecipeName = n % 2 == 0 ? "Cat" : "Dog", Image = img,
                Traits = new[] { new TraitSelection("bg", "Background", $"v{n % 3}", $"V{n % 3}") },
                ColorRolls = new[] { new ColorRoll("bg", LayerKind.Dynamic, ColorModel.Hsv, n % 360, 0.5) },
            });
        }
        return new GeneratedSet("Pets", "desc", "PT", "seed", assets);
    }

    private static string Cook(DirectoryInfo root, string name, int count)
    {
        string dir = Path.Combine(root.FullName, name);
        using var set = MakeSet(count);
        SetWriter.Write(set, dir, false);
        return dir;
    }

    /// <summary>What the per-file walk says: the same Set with its index taken away.</summary>
    private static SetWriter.ExistingSet Walked(string dir)
    {
        string copy = dir + "-walk";
        if (Directory.Exists(copy)) Directory.Delete(copy, true);
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            string to = Path.Combine(copy, Path.GetRelativePath(dir, f));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(f, to);
        }
        File.Delete(Path.Combine(copy, IndexName));
        return SetWriter.ReadExisting(copy);
    }

    private static void AssertSame(SetWriter.ExistingSet a, SetWriter.ExistingSet b)
    {
        Assert.Equal(a.Dnas, b.Dnas);
        Assert.Equal(a.NextNumber, b.NextNumber);
        Assert.Equal(a.CookbookSha256, b.CookbookSha256);
    }

    [Fact]
    public void A_written_set_carries_an_index_and_it_answers_exactly_as_the_walk_does()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            string dir = Cook(root, "s", 25);
            Assert.True(File.Exists(Path.Combine(dir, IndexName)));
            var viaIndex = SetWriter.ReadExisting(dir);
            Assert.Equal(25, viaIndex.Dnas.Count);
            Assert.Equal(26, viaIndex.NextNumber);
            AssertSame(Walked(dir), viaIndex);
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public async Task The_async_read_agrees_with_the_sync_one()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            string dir = Cook(root, "s", 12);
            AssertSame(SetWriter.ReadExisting(dir), await SetWriter.ReadExistingAsync(dir));
            using var more = MakeSet(3, first: 13);
            await SetWriter.WriteAsync(more, dir, false);
            AssertSame(Walked(dir), await SetWriter.ReadExistingAsync(dir));
            Assert.Equal(15, (await SetWriter.ReadExistingAsync(dir)).Dnas.Count);
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public void Extending_rewrites_the_index_so_it_still_matches()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            string dir = Cook(root, "s", 10);
            // The regrade rewrites every existing nfty file (sizes can change), so a stale index would be refused.
            using (var more = MakeSet(5, first: 11)) SetWriter.Write(more, dir, false);
            var viaIndex = SetWriter.ReadExisting(dir);
            Assert.Equal(15, viaIndex.Dnas.Count);
            Assert.Equal(16, viaIndex.NextNumber);
            AssertSame(Walked(dir), viaIndex);

            // Prove the index is actually the path taken: it is the only way a lone DNA edit stays invisible.
            // (Same size edit; documented limit.) Here we only assert the index file is current.
            var lines = File.ReadAllLines(Path.Combine(dir, IndexName));
            Assert.Equal(1 + 15, lines.Length);
            Assert.Equal("nfty-set-index 1 15", lines[0]);
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public void A_hand_deleted_asset_file_falls_back_to_the_walk()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            string dir = Cook(root, "s", 10);
            File.Delete(Path.Combine(dir, "nfty", "0004.json"));
            var got = SetWriter.ReadExisting(dir);
            Assert.Equal(9, got.Dnas.Count);
            Assert.DoesNotContain("dna-000004", got.Dnas);
            AssertSame(Walked(dir), got);
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public void A_hand_added_asset_file_falls_back_to_the_walk()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            string dir = Cook(root, "s", 10);
            string json = File.ReadAllText(Path.Combine(dir, "nfty", "0001.json"))
                .Replace("dna-000001", "dna-hand").Replace("\"setNumber\": 1", "\"setNumber\": 99")
                .Replace("\"setNumber\":1", "\"setNumber\":99");
            File.WriteAllText(Path.Combine(dir, "nfty", "0099.json"), json);
            var got = SetWriter.ReadExisting(dir);
            Assert.Equal(11, got.Dnas.Count);
            Assert.Contains("dna-hand", got.Dnas);
            Assert.Equal(100, got.NextNumber);
            AssertSame(Walked(dir), got);
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public void A_hand_edited_asset_file_whose_size_changed_falls_back_to_the_walk()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            string dir = Cook(root, "s", 10);
            string path = Path.Combine(dir, "nfty", "0003.json");
            File.WriteAllText(path, File.ReadAllText(path).Replace("dna-000003", "dna-edited-longer"));
            var got = SetWriter.ReadExisting(dir);
            Assert.Contains("dna-edited-longer", got.Dnas);
            Assert.DoesNotContain("dna-000003", got.Dnas);
            AssertSame(Walked(dir), got);
        }
        finally { root.Delete(true); }
    }

    [Theory]
    [InlineData("truncated")]
    [InlineData("partial-last-line")]
    [InlineData("garbage")]
    [InlineData("wrong-count")]
    [InlineData("empty")]
    [InlineData("missing")]
    public void A_damaged_or_missing_index_falls_back_to_the_walk(string damage)
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            string dir = Cook(root, "s", 10);
            string index = Path.Combine(dir, IndexName);
            string text = File.ReadAllText(index);
            switch (damage)
            {
                case "truncated": File.WriteAllText(index, text[..(text.Length / 2)]); break;
                case "partial-last-line": File.WriteAllText(index, text.TrimEnd('\n')); break;
                case "garbage": File.WriteAllText(index, "not an index\n\u0000\u0001\n"); break;
                case "wrong-count": File.WriteAllText(index, text.Replace("index 1 10", "index 1 9")); break;
                case "empty": File.WriteAllText(index, ""); break;
                case "missing": File.Delete(index); break;
            }
            var got = SetWriter.ReadExisting(dir);
            Assert.Equal(10, got.Dnas.Count);
            AssertSame(Walked(dir), got);
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public void An_index_that_lies_about_a_size_is_refused()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            string dir = Cook(root, "s", 5);
            string index = Path.Combine(dir, IndexName);
            var lines = File.ReadAllLines(index);
            var cols = lines[2].Split('\t');
            cols[3] = "dna-forged"; // a forged DNA is only believable if the size still matched; make it not
            cols[1] = "1";
            lines[2] = string.Join('\t', cols);
            File.WriteAllText(index, string.Join('\n', lines) + "\n");
            Assert.DoesNotContain("dna-forged", SetWriter.ReadExisting(dir).Dnas);
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public void A_set_cooked_before_the_index_existed_still_extends()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            string dir = Cook(root, "s", 6);
            File.Delete(Path.Combine(dir, IndexName)); // what an older build left
            using (var more = MakeSet(2, first: 7)) SetWriter.Write(more, dir, false);
            Assert.True(File.Exists(Path.Combine(dir, IndexName)));
            Assert.Equal(8, SetWriter.ReadExisting(dir).Dnas.Count);
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public void Without_needing_dnas_the_next_number_is_still_right()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            string dir = Cook(root, "s", 9);
            var lean = SetWriter.ReadExisting(dir, needDnas: false);
            Assert.Empty(lean.Dnas);
            Assert.Equal(10, lean.NextNumber);
            Assert.Equal(10, SetWriter.ReadExisting(dir, Parallelism.Sequential, false).NextNumber);
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public void The_index_is_never_part_of_the_set_a_reader_a_packer_or_an_exporter_sees()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            string dir = Path.Combine(root.FullName, "s");
            using (var set = MakeSet(6)) SetWriter.Write(set, dir, pack: true);
            Assert.True(File.Exists(Path.Combine(dir, IndexName)));

            Assert.DoesNotContain(SetLayout.FilesIn(dir), f => Path.GetFileName(f) == IndexName);

            using (var loaded = SetReader.Read(dir)) Assert.Equal(6, loaded.Items.Count);

            // The packed archive carries no index; reading it and extending it still works.
            using (var zip = System.IO.Compression.ZipFile.OpenRead(Path.Combine(dir, "s.set")))
                Assert.DoesNotContain(zip.Entries, e => e.FullName.Contains("index", StringComparison.Ordinal));
            using (var loaded = SetReader.Read(Path.Combine(dir, "s.set"))) Assert.Equal(6, loaded.Items.Count);

            string outDir = Path.Combine(root.FullName, "out");
            SetExporter.Export(dir, outDir, new ExportOptions());
            Assert.Empty(Directory.EnumerateFiles(outDir, "*index*", SearchOption.AllDirectories));
        }
        finally { root.Delete(true); }
    }

    /// <summary>Not a gate on timing (machines differ): prints the measurement the BACKLOG asked for.</summary>
    [Fact]
    public void Index_versus_walk_at_five_thousand_assets_prints_timings()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            const int n = 5000;
            string dir = Path.Combine(root.FullName, "big");
            using (var set = MakeSet(n)) SetWriter.Write(set, dir, false);

            Perf.Enable();
            Perf.Reset();
            try
            {
                SetWriter.ExistingSet viaIndex = default!, viaWalk = default!;
                for (int i = 0; i < 5; i++) viaIndex = SetWriter.ReadExisting(dir);
                File.Delete(Path.Combine(dir, IndexName));
                for (int i = 0; i < 5; i++) viaWalk = SetWriter.ReadExisting(dir);
                AssertSame(viaWalk, viaIndex);
                Assert.Equal(n, viaIndex.Dnas.Count);
                TestContext.Current.TestOutputHelper?.WriteLine(Perf.Report());
            }
            finally { Perf.Disable(); }
        }
        finally { root.Delete(true); }
    }
}
