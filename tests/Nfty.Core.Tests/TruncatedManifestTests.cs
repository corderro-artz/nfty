using System.IO.Compression;
using System.Text;
using Nfty.Core.Formats;

namespace Nfty.Core.Tests;

/// <summary>
/// A manifest missing its required members used to be accepted. Positional records declare their
/// optionality in the type — <c>Colorization?</c> may be absent, <c>string Id</c> may not — but
/// nothing enforced it, so <c>{"schemaVersion":1}</c> deserialized into a record with every member
/// null and the reader handed it back as a valid CookBook.
///
/// <para>The first thing to touch it then threw <see cref="NullReferenceException"/>, and that thing
/// was usually <see cref="Validator"/> — the worst possible place, because answering "what is wrong
/// with this book?" is the single job it exists for. It crashed instead of answering.</para>
///
/// <para>The failure now happens at the boundary, where the message can name what is missing.</para>
/// </summary>
public class TruncatedManifestTests
{
    private static string WriteArchive(string extension, string manifestJson, params string[] emptyEntries)
    {
        var dir = Directory.CreateTempSubdirectory();
        string path = Path.Combine(dir.FullName, "truncated" + extension);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var s = zip.CreateEntry("manifest.json").Open())
            s.Write(Encoding.UTF8.GetBytes(manifestJson));
        foreach (var name in emptyEntries) zip.CreateEntry(name);
        return path;
    }

    [Fact]
    public void A_cookbook_manifest_with_no_fields_is_rejected_when_it_is_read()
    {
        string path = WriteArchive(".cbk", """{"schemaVersion":1}""");

        var ex = Assert.Throws<InvalidDataException>(() => CookBookArchive.Read(path));

        Assert.Contains("manifest.json", ex.Message);
        Assert.IsType<System.Text.Json.JsonException>(ex.InnerException);
    }

    [Fact]
    public async Task The_async_reader_rejects_it_identically()
    {
        string path = WriteArchive(".cbk", """{"schemaVersion":1}""");

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => CookBookArchive.ReadAsync(path));

        Assert.Contains("manifest.json", ex.Message);
    }

    [Fact]
    public void A_recipe_manifest_missing_its_layer_order_is_rejected()
    {
        string path = WriteArchive(".rcp", """{"id":"r","name":"R","schemaVersion":1}""");

        Assert.Throws<InvalidDataException>(() => RecipeArchive.Read(path));
    }

    [Fact]
    public void An_ingredient_manifest_missing_its_variants_is_rejected()
    {
        string path = WriteArchive(".igt", """{"id":"i","name":"I","kind":"custom","schemaVersion":1}""");

        Assert.Throws<InvalidDataException>(() => IngredientArchive.Read(path));
    }

    /// <summary>The rejection must be an nfty exception carrying a readable message, not a raw
    /// framework type — <c>ErrorReport</c> prints <c>ex.Message</c> straight to the user.</summary>
    [Fact]
    public void The_rejection_names_the_manifest_rather_than_a_clr_type()
    {
        string path = WriteArchive(".cbk", """{"schemaVersion":1}""");

        var ex = Assert.Throws<InvalidDataException>(() => CookBookArchive.Read(path));

        Assert.StartsWith("manifest.json is not a readable CookBookManifest", ex.Message);
    }

    /// <summary>Malformed JSON takes the same path, rather than escaping as a bare JsonException.</summary>
    [Fact]
    public void Malformed_json_is_reported_the_same_way()
    {
        string path = WriteArchive(".cbk", "{ not json at all");

        var ex = Assert.Throws<InvalidDataException>(() => CookBookArchive.Read(path));

        Assert.Contains("manifest.json", ex.Message);
    }
}
