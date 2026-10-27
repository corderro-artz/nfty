using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

public class SetItemFileTests
{
    private static GeneratedSet MakeSet(int number, string dna) => new(
        "Pets", "desc", "P", "seed",
        new[]
        {
            new GeneratedAsset
            {
                SetNumber = number, Dna = dna, RecipeId = "cat", RecipeName = "Cat",
                Image = new Image<Rgba32>(2, 2, new Rgba32(1, 2, 3, 255)),
                Traits = new[] { new TraitSelection("bg", "Background", "sunset", "Sunset") },
                ColorRolls = new[] { new ColorRoll("bg", LayerKind.Custom, null, null, null) },
            },
        });

    /// <summary>A real folder Set of two assets (6 and 7) with item 0007 damaged as given.</summary>
    private static string SetWithDamagedItem(string content, string subdir = "nfty")
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        SetWriter.Write(MakeSet(6, "d6"), dir, pack: false);
        SetWriter.Write(MakeSet(7, "d7"), dir, pack: false);
        File.WriteAllText(Path.Combine(dir, subdir, "0007.json"), content);
        return dir;
    }

    public static TheoryData<string> Readers() => new() { "Read", "ReadAsync", "ReadExisting", "Write" };

    private static async Task Run(string reader, string dir)
    {
        switch (reader)
        {
            case "Read": SetReader.Read(dir).Dispose(); break;
            case "ReadAsync": (await SetReader.ReadAsync(dir)).Dispose(); break;
            case "ReadExisting": SetWriter.ReadExisting(dir); break;
            default: SetWriter.Write(MakeSet(8, "d8"), dir, pack: false); break;
        }
    }

    [Theory]
    [MemberData(nameof(Readers))]
    public async Task A_null_item_file_is_reported_as_corrupt_by_every_reader(string reader)
    {
        var dir = SetWithDamagedItem("null");

        var ex = await Assert.ThrowsAsync<CorruptSetException>(() => Run(reader, dir));

        Assert.EndsWith("0007.json", ex.Path);
        Assert.Contains("0007.json", ex.Message);
    }

    [Theory]
    [InlineData("Read")]
    [InlineData("ReadAsync")]
    public async Task An_unparsable_item_file_is_reported_as_corrupt_by_the_set_reader(string reader)
    {
        var dir = SetWithDamagedItem("{ truncated");

        var ex = await Assert.ThrowsAsync<CorruptSetException>(() => Run(reader, dir));

        Assert.EndsWith("0007.json", ex.Path);
    }

    [Fact]
    public void An_unparsable_opensea_file_is_reported_as_corrupt_on_extend()
    {
        var dir = SetWithDamagedItem("{ truncated", "metadata");

        var ex = Assert.Throws<CorruptSetException>(
            () => SetWriter.Write(MakeSet(8, "d8"), dir, pack: false));

        Assert.EndsWith("0007.json", ex.Path);
        Assert.Contains("metadata", ex.Path);
    }

    [Fact]
    public void A_null_opensea_file_is_reported_as_corrupt_on_extend()
    {
        var dir = SetWithDamagedItem("null", "metadata");

        var ex = Assert.Throws<CorruptSetException>(
            () => SetWriter.Write(MakeSet(8, "d8"), dir, pack: false));

        Assert.EndsWith("0007.json", ex.Path);
    }
}
