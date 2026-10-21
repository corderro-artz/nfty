using System.IO.Compression;
using System.Text.Json;
using Nfty.Core.Generation;
using Nfty.Core.Output;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>The models seam of <see cref="SetWriter"/>: <c>models/</c> in a Set and the
/// <c>models</c> block in <c>set.json</c>.</summary>
public class SetModelsWriterTests
{
    private sealed class FakeModeler(SetModels options, params int[] nullFor) : IAssetModeler
    {
        public string? NameOverride { get; init; }
        public SetModels Options { get; } = options;

        public IReadOnlyList<SetFile>? Model(GeneratedAsset asset, string baseName)
        {
            if (Array.IndexOf(nullFor, asset.SetNumber) >= 0) return null;
            return [new SetFile(NameOverride ?? $"{baseName}.fake", [(byte)asset.SetNumber])];
        }
    }

    private static readonly SetModels Fake = new("fake", "center", 0.5f);

    private static GeneratedSet SetOf(params int[] numbers) => new(
        "Col", "desc", "COL", "seed",
        numbers.Select(n => new GeneratedAsset
        {
            SetNumber = n,
            Dna = $"dna{n}",
            RecipeId = "r",
            RecipeName = "R",
            Image = new Image<Rgba32>(2, 2, new Rgba32(10, 20, 30, 255)),
            Traits = [new TraitSelection("i", "Body", "v", "V")],
            ColorRolls = [],
        }).ToList());

    private static string Tmp() => Directory.CreateTempSubdirectory().FullName;

    private static SetManifest ManifestAt(string dir) =>
        JsonSerializer.Deserialize<SetManifest>(File.ReadAllText(Path.Combine(dir, "set.json")), Nfty.Core.Formats.Json.Options)!;

    private static Dictionary<string, byte[]> Snapshot(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(dir, f), File.ReadAllBytes);

    [Fact]
    public void A_cook_with_a_modeler_writes_one_model_per_asset_beside_its_image()
    {
        string dir = Tmp();
        using (var set = SetOf(1, 2, 3)) SetWriter.Write(set, dir, false, new FakeModeler(Fake));

        for (int n = 1; n <= 3; n++)
            Assert.Equal(new[] { (byte)n }, File.ReadAllBytes(Path.Combine(dir, "models", $"{n:D4}.fake")));
    }

    [Fact]
    public void The_manifest_records_the_model_options()
    {
        string dir = Tmp();
        using (var set = SetOf(1, 2)) SetWriter.Write(set, dir, false, new FakeModeler(Fake));

        Assert.Equal(new SetModels("fake", "center", 0.5f, null), ManifestAt(dir).Models);
    }

    [Fact]
    public void A_cook_without_a_modeler_writes_no_models_folder_and_no_block()
    {
        string dir = Tmp();
        using (var set = SetOf(1)) SetWriter.Write(set, dir, false);

        Assert.False(Directory.Exists(Path.Combine(dir, "models")));
        Assert.Null(ManifestAt(dir).Models);
    }

    [Fact]
    public void A_fully_transparent_asset_is_skipped_and_recorded()
    {
        string dir = Tmp();
        using (var set = SetOf(1, 2, 3)) SetWriter.Write(set, dir, false, new FakeModeler(Fake, 2));

        Assert.False(File.Exists(Path.Combine(dir, "models", "0002.fake")));
        Assert.True(File.Exists(Path.Combine(dir, "models", "0003.fake")));
        Assert.Equal([2], ManifestAt(dir).Models!.Skipped);
    }

    [Fact]
    public void Pack_carries_the_models()
    {
        string dir = Tmp();
        using (var set = SetOf(1)) SetWriter.Write(set, dir, true, new FakeModeler(Fake));

        using var zip = ZipFile.OpenRead(Directory.EnumerateFiles(dir, "*.set").Single());
        Assert.Contains(zip.Entries, e => e.FullName == "models/0001.fake");
    }

    [Fact]
    public void Extending_a_modeled_set_models_only_the_new_assets()
    {
        string dir = Tmp();
        using (var set = SetOf(1, 2)) SetWriter.Write(set, dir, false, new FakeModeler(Fake, 2));
        File.WriteAllBytes(Path.Combine(dir, "models", "0001.fake"), [99]);   // would be overwritten if re-modeled

        using (var more = SetOf(3, 4, 5)) SetWriter.Write(more, dir, false, new FakeModeler(Fake, 5, 4));

        Assert.Equal(new byte[] { 99 }, File.ReadAllBytes(Path.Combine(dir, "models", "0001.fake")));
        Assert.True(File.Exists(Path.Combine(dir, "models", "0003.fake")));
        Assert.Equal([2, 4, 5], ManifestAt(dir).Models!.Skipped);
    }

    [Fact]
    public void Extending_a_modeled_set_without_a_modeler_is_refused_before_writing()
    {
        string dir = Tmp();
        using (var set = SetOf(1)) SetWriter.Write(set, dir, false, new FakeModeler(Fake));
        var before = Snapshot(dir);

        using var more = SetOf(2);
        var ex = Assert.Throws<InvalidOperationException>(() => SetWriter.Write(more, dir, false));

        Assert.Contains("fake", ex.Message, StringComparison.Ordinal);
        Assert.Contains("center", ex.Message, StringComparison.Ordinal);
        Assert.Contains(dir, ex.Message, StringComparison.Ordinal);
        var after = Snapshot(dir);
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
        foreach (var (k, v) in before) Assert.Equal(v, after[k]);
    }

    [Fact]
    public void Extending_an_unmodeled_set_with_a_modeler_is_refused()
    {
        string dir = Tmp();
        using (var set = SetOf(1)) SetWriter.Write(set, dir, false);
        var before = Snapshot(dir);

        using var more = SetOf(2);
        var ex = Assert.Throws<InvalidOperationException>(
            () => SetWriter.Write(more, dir, false, new FakeModeler(Fake)));

        Assert.Contains("without 3D models", ex.Message, StringComparison.Ordinal);
        Assert.Equal(before.Count, Snapshot(dir).Count);
        Assert.False(Directory.Exists(Path.Combine(dir, "models")));
    }

    [Fact]
    public void Extending_with_different_options_is_refused_naming_both()
    {
        string dir = Tmp();
        using (var set = SetOf(1)) SetWriter.Write(set, dir, false, new FakeModeler(Fake));

        using var more = SetOf(2);
        var ex = Assert.Throws<InvalidOperationException>(() => SetWriter.Write(
            more, dir, false, new FakeModeler(new SetModels("fake", "bottom-center", 2f))));

        Assert.Contains("center", ex.Message, StringComparison.Ordinal);
        Assert.Contains("bottom-center", ex.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(dir, "images", "0002.png")));
    }

    [Fact]
    public async Task Sync_and_async_writers_produce_identical_bytes()
    {
        string a = Tmp(), b = Tmp();
        using (var s1 = SetOf(1, 2, 3, 4)) SetWriter.Write(s1, a, false, new FakeModeler(Fake, 3));
        using (var s2 = SetOf(1, 2, 3, 4)) await SetWriter.WriteAsync(s2, b, false, models: new FakeModeler(Fake, 3));

        var sa = Snapshot(a);
        var sb = Snapshot(b);
        Assert.Equal(sa.Keys.Order(StringComparer.Ordinal), sb.Keys.Order(StringComparer.Ordinal));
        foreach (var (k, v) in sa) Assert.Equal(v, sb[k]);
    }

    [Fact]
    public void A_model_file_name_that_leaves_the_folder_is_refused()
    {
        string dir = Tmp();
        using var set = SetOf(1);

        Assert.Throws<InvalidOperationException>(() =>
            SetWriter.Write(set, dir, false, new FakeModeler(Fake) { NameOverride = "../x" }));
        Assert.False(File.Exists(Path.Combine(dir, "x")));
    }

    [Fact]
    public void ReadExisting_reports_the_recorded_models()
    {
        string dir = Tmp();
        using (var set = SetOf(1, 2)) SetWriter.Write(set, dir, false, new FakeModeler(Fake, 2));

        Assert.Equal(new SetModels("fake", "center", 0.5f, [2]), SetWriter.ReadExisting(dir).Models, ModelsComparer.Instance);
    }

    [Fact]
    public async Task ReadExistingAsync_reports_the_recorded_models()
    {
        string dir = Tmp();
        using (var set = SetOf(1)) SetWriter.Write(set, dir, false, new FakeModeler(Fake));

        Assert.Equal(Fake, (await SetWriter.ReadExistingAsync(dir)).Models, ModelsComparer.Instance);
    }

    [Fact]
    public void An_unmodeled_set_json_has_no_models_key()
    {
        string dir = Tmp();
        using (var set = SetOf(1)) SetWriter.Write(set, dir, false);

        Assert.DoesNotContain("models", File.ReadAllText(Path.Combine(dir, "set.json")), StringComparison.Ordinal);
    }

    private sealed class ModelsComparer : IEqualityComparer<SetModels?>
    {
        public static readonly ModelsComparer Instance = new();
        public bool Equals(SetModels? x, SetModels? y) =>
            x is not null && y is not null && x.Format == y.Format && x.Pivot == y.Pivot
            && x.VoxelSize == y.VoxelSize
            && (x.Skipped ?? []).SequenceEqual(y.Skipped ?? []);
        public int GetHashCode(SetModels? o) => 0;
    }
}
