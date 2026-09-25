using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Nfty.Ticket.Tests;

public class MenuArchiveTests
{
    private static byte[] ZipWith(string manifestJson)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        using (var w = new StreamWriter(zip.CreateEntry("manifest.json").Open(), Encoding.UTF8))
            w.Write(manifestJson);
        return ms.ToArray();
    }

    /// <summary>
    /// A well-formed loose-mode manifest with every top-level field <see cref="MenuArchive"/> now
    /// checks for, minus whichever names appear in <paramref name="omit"/> — built with plain string
    /// concatenation rather than a raw interpolated string, since this file already hit CS9007 once
    /// from JSON's own braces colliding with a raw string's interpolation-hole syntax.
    /// </summary>
    private static string ManifestOmitting(params string[] omit)
    {
        var fields = new Dictionary<string, string>
        {
            ["menuSchemaVersion"] = MenuSchema.Current.ToString(CultureInfo.InvariantCulture),
            ["canvas"] = "{\"width\":2,\"height\":2}",
            ["collectionName"] = "\"C\"",
            ["collectionSymbol"] = "\"S\"",
            ["recipes"] = "[]",
            ["art"] = "{\"mode\":\"loose\",\"atlasWidth\":0,\"atlasHeight\":0,\"cells\":[]}",
        };
        foreach (var name in omit) fields.Remove(name);
        return "{" + string.Join(",", fields.Select(kv => $"\"{kv.Key}\":{kv.Value}")) + "}";
    }

    [Fact]
    public void A_menu_from_a_newer_schema_is_refused_rather_than_misread()
    {
        // The gate refuses NEWER and accepts older, because a future field this build cannot see
        // could change what the file means — the rule ArchiveIo.ReadManifest already follows.
        byte[] bytes = ZipWith($$"""{"menuSchemaVersion":{{MenuSchema.Current + 1}}}""");
        var ex = Assert.Throws<NotSupportedException>(() => MenuArchive.Open(new MemoryStream(bytes)));
        Assert.Contains("schema", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_zip_with_no_manifest_is_refused()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            zip.CreateEntry("nothing.txt");

        // ThrowsAny<Exception> passes for a NullReferenceException just as readily as for a real
        // refusal - pinned to the specific type and message MenuArchive.Open actually throws for a
        // missing manifest.
        var ex = Assert.Throws<InvalidDataException>(
            () => MenuArchive.Open(new MemoryStream(ms.ToArray())));
        Assert.Equal("Menu is missing manifest.json.", ex.Message);
    }

    [Fact]
    public void Asking_for_a_cell_the_menu_does_not_have_names_the_key()
    {
        // Triple-dollar / triple-brace: the JSON itself ends in two adjacent literal '}' (closing
        // "art" then the outer object), which a $$ raw string reads as an unmatched interpolation
        // close (CS9007). $$$ needs three braces to open or close a hole, so "}}" stays literal.
        byte[] bytes = ZipWith($$$"""
            {"menuSchemaVersion":{{{MenuSchema.Current}}},"canvas":{"width":2,"height":2},
             "collectionName":"C","collectionSymbol":"S","recipes":[],
             "art":{"mode":"loose","atlasWidth":0,"atlasHeight":0,"cells":[]}}
            """);
        using var menu = MenuArchive.Open(new MemoryStream(bytes));
        var ex = Assert.Throws<KeyNotFoundException>(() => menu.ArtBytes("cat/bg/a"));

        // .NET's own dictionary-indexer KeyNotFoundException already contains the missing key,
        // which a bare Contains(key, ...) cannot tell apart from - pinned to the actual message
        // ArtBytes throws.
        Assert.Equal("No cell for 'cat/bg/a'.", ex.Message);
    }

    // A manifest missing a required member is a Menu that used to open fine and fail later with a
    // NullReferenceException somewhere deep inside Ticketer.Prepare - far from the actual problem.
    // These pin that MenuArchive.Open refuses it up front and names the member that is missing.

    [Fact]
    public void A_manifest_missing_canvas_names_it()
    {
        var ex = Assert.Throws<InvalidDataException>(
            () => MenuArchive.Open(new MemoryStream(ZipWith(ManifestOmitting("canvas")))));
        Assert.Contains("canvas", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_manifest_missing_collectionName_names_it()
    {
        var ex = Assert.Throws<InvalidDataException>(
            () => MenuArchive.Open(new MemoryStream(ZipWith(ManifestOmitting("collectionName")))));
        Assert.Contains("collectionName", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_manifest_missing_collectionSymbol_names_it()
    {
        var ex = Assert.Throws<InvalidDataException>(
            () => MenuArchive.Open(new MemoryStream(ZipWith(ManifestOmitting("collectionSymbol")))));
        Assert.Contains("collectionSymbol", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_manifest_missing_recipes_names_it()
    {
        var ex = Assert.Throws<InvalidDataException>(
            () => MenuArchive.Open(new MemoryStream(ZipWith(ManifestOmitting("recipes")))));
        Assert.Contains("recipes", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_manifest_missing_art_names_it()
    {
        var ex = Assert.Throws<InvalidDataException>(
            () => MenuArchive.Open(new MemoryStream(ZipWith(ManifestOmitting("art")))));
        Assert.Contains("art", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_atlas_manifest_with_non_positive_atlas_dimensions_names_it()
    {
        // Cells present so only the width/height check is what fires.
        byte[] bytes = ZipWith(
            "{\"menuSchemaVersion\":" + MenuSchema.Current.ToString(CultureInfo.InvariantCulture)
            + ",\"canvas\":{\"width\":2,\"height\":2},\"collectionName\":\"C\","
            + "\"collectionSymbol\":\"S\",\"recipes\":[],"
            + "\"art\":{\"mode\":\"atlas\",\"atlasWidth\":0,\"atlasHeight\":0,"
            + "\"cells\":[{\"key\":\"cat/bg/a\",\"x\":0,\"y\":0,\"width\":1,\"height\":1}]}}");
        var ex = Assert.Throws<InvalidDataException>(() => MenuArchive.Open(new MemoryStream(bytes)));
        Assert.Contains("atlasWidth", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_atlas_manifest_with_no_cells_names_it()
    {
        byte[] bytes = ZipWith(
            "{\"menuSchemaVersion\":" + MenuSchema.Current.ToString(CultureInfo.InvariantCulture)
            + ",\"canvas\":{\"width\":2,\"height\":2},\"collectionName\":\"C\","
            + "\"collectionSymbol\":\"S\",\"recipes\":[],"
            + "\"art\":{\"mode\":\"atlas\",\"atlasWidth\":2,\"atlasHeight\":2,\"cells\":[]}}");
        var ex = Assert.Throws<InvalidDataException>(() => MenuArchive.Open(new MemoryStream(bytes)));
        Assert.Contains("cells", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_schema_gate_accepts_older_and_refuses_newer()
    {
        // `current` is a parameter so the RANGE is testable while Current is still 1 — the same
        // argument Schema.IsSupported makes about itself.
        Assert.True(MenuSchema.IsSupported(1, current: 3));
        Assert.True(MenuSchema.IsSupported(3, current: 3));
        Assert.False(MenuSchema.IsSupported(4, current: 3));
        Assert.False(MenuSchema.IsSupported(0, current: 3));
    }
}
