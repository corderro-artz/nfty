using System.CommandLine;
using System.Security.Cryptography;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Cli.Tests;

public sealed class CliExecutionAuditTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("nfty-cli-execution-").FullName;
    private static readonly InvocationConfiguration Invocation = new() { EnableDefaultExceptionHandler = false };

    private static int Run(params string[] args) => CommandFactory.Build().Parse(args).Invoke(Invocation);

    [Theory]
    [InlineData("set.json")]
    [InlineData("set-index.txt")]
    [InlineData("images/0001.png")]
    [InlineData("metadata/notes.txt")]
    [InlineData("nfty/notes.txt")]
    public void Generate_refuses_occupied_set_contents_without_changing_any_file(string relative)
    {
        string book = WriteBook();
        string output = Path.Combine(_root, "set");
        string occupied = Path.Combine(output, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(occupied)!);
        File.WriteAllText(occupied, "existing author data");
        var before = Snapshot(output);

        var error = Assert.Throws<InvalidOperationException>(() => Run("generate", book,
            "--count", "1", "--out", output));

        Assert.Contains("extend", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, Snapshot(output));
    }

    [Fact]
    public void Generate_cannot_leave_duplicate_dna_in_a_previously_unique_set()
    {
        string bookPath = WriteBook();
        string output = Path.Combine(_root, "set");
        using var book = CookBookArchive.Read(bookPath);
        using var original = Generator.Generate(book, new GenerateOptions(2, "first"));
        SetWriter.Write(original, output, pack: false);
        string tail = original.Assets[1].Dna;
        string? matchingSeed = null;
        for (int i = 0; i < 32; i++)
        {
            string seed = "candidate" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            using var candidate = Generator.Generate(book, new GenerateOptions(1, seed));
            if (candidate.Assets[0].Dna == tail) { matchingSeed = seed; break; }
        }
        Assert.NotNull(matchingSeed);
        var before = Snapshot(output);

        Assert.Throws<InvalidOperationException>(() => Run("generate", bookPath,
            "--count", "1", "--seed", matchingSeed, "--out", output));

        Assert.Equal(before, Snapshot(output));
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void Already_met_extension_targets_are_no_ops_without_overflow(int target)
    {
        string book = WriteBook();
        string output = Path.Combine(_root, "set");
        Assert.Equal(0, Run("generate", book, "--count", "1", "--out", output));
        var before = Snapshot(output);

        int code = Run("extend", book, output, "--to",
            target.ToString(System.Globalization.CultureInfo.InvariantCulture), "--max-rerolls", "1");

        Assert.Equal(0, code);
        Assert.Equal(before, Snapshot(output));
    }

    [Fact]
    public void Generate_allows_unrelated_kitchen_files_and_empty_set_directories()
    {
        string book = WriteBook();
        Directory.CreateDirectory(Path.Combine(_root, SetLayout.ImagesDir));
        Directory.CreateDirectory(Path.Combine(_root, SetLayout.ModelsDir));
        string model = Path.Combine(_root, SetLayout.ModelsDir, "sculpture.glb");
        File.WriteAllText(model, "unrelated model");
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "author notes");
        byte[] originalBook = File.ReadAllBytes(book);

        Assert.Equal(0, Run("generate", book, "--count", "1", "--out", _root));

        Assert.Equal("author notes", File.ReadAllText(Path.Combine(_root, "notes.txt")));
        Assert.Equal("unrelated model", File.ReadAllText(model));
        Assert.Equal(originalBook, File.ReadAllBytes(book));
    }

    [Fact]
    public void Modeled_generation_preserves_unrelated_models_but_refuses_a_numbered_collision()
    {
        string book = WriteBook();
        string output = Path.Combine(_root, "set");
        string models = Path.Combine(output, SetLayout.ModelsDir);
        Directory.CreateDirectory(models);
        string collision = Path.Combine(models, "0001.glb");
        File.WriteAllText(collision, "previous numbered model");
        File.WriteAllText(Path.Combine(models, "sculpture.glb"), "unrelated model");
        var before = Snapshot(output);

        Assert.Throws<InvalidOperationException>(() => Run("generate", book,
            "--count", "1", "--models", "--out", output));

        Assert.Equal(before, Snapshot(output));
        File.Delete(collision);
        Assert.Equal(0, Run("generate", book, "--count", "1", "--models", "--out", output));
        Assert.Equal("unrelated model", File.ReadAllText(Path.Combine(models, "sculpture.glb")));
    }

    [Fact]
    public void Packed_generation_refuses_its_existing_archive_without_blocking_other_archives()
    {
        string book = WriteBook();
        string output = Path.Combine(_root, "set");
        Directory.CreateDirectory(output);
        string packed = Path.Combine(output, "set.set");
        File.WriteAllText(packed, "previous packed output");
        File.WriteAllText(Path.Combine(output, "other.set"), "unrelated packed set");
        var before = Snapshot(output);

        Assert.Throws<InvalidOperationException>(() => Run("generate", book,
            "--count", "1", "--pack", "--out", output));

        Assert.Equal(before, Snapshot(output));
        File.Delete(packed);
        Assert.Equal(0, Run("generate", book, "--count", "1", "--pack", "--out", output));
        Assert.Equal("unrelated packed set", File.ReadAllText(Path.Combine(output, "other.set")));
    }

    private string WriteBook()
    {
        var ingredient = new LoadedIngredient
        {
            Manifest = new IngredientManifest("art", "Art", LayerKind.Custom, null,
                [new Variant("red", "Red", 1), new Variant("blue", "Blue", 1)]),
            VariantImages = new Dictionary<string, Image<Rgba32>>
            {
                ["red"] = new(1, 1, new Rgba32(255, 0, 0, 255)),
                ["blue"] = new(1, 1, new Rgba32(0, 0, 255, 255)),
            },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("one", "One", ["art"], []),
            Ingredients = [ingredient],
        };
        using var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("book", "Book", new Dimensions(1, 1),
                new Collection("Colors", "", "CLR"), new Dictionary<string, double> { ["one"] = 1 }),
            Recipes = [recipe],
        };
        string path = Path.Combine(_root, "book.cbk");
        CookBookArchive.Write(path, book.Manifest, book.Recipes);
        return path;
    }

    private static string[] Snapshot(string directory) => Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal)
        .Select(path => Path.GetRelativePath(directory, path) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))
        .ToArray();

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
