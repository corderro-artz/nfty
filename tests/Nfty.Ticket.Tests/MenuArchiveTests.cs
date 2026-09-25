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

    [Fact]
    public void A_loose_manifest_missing_cells_is_refused_rather_than_a_bare_null_reference()
    {
        // "cells" is required in BOTH modes (a loose Menu still writes one whole-image MenuCell per
        // variant), so a manifest omitting it entirely deserializes Art.Cells to null and used to
        // reach the caller as a bare NullReferenceException out of MenuArchive's own constructor.
        byte[] bytes = ZipWith(
            "{\"menuSchemaVersion\":" + MenuSchema.Current.ToString(CultureInfo.InvariantCulture)
            + ",\"canvas\":{\"width\":2,\"height\":2},\"collectionName\":\"C\","
            + "\"collectionSymbol\":\"S\",\"recipes\":[],"
            + "\"art\":{\"mode\":\"loose\",\"atlasWidth\":0,\"atlasHeight\":0}}");
        var ex = Assert.Throws<InvalidDataException>(() => MenuArchive.Open(new MemoryStream(bytes)));
        Assert.Contains("cells", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>A one-recipe manifest, every field present, minus whichever names appear in
    /// <paramref name="omit"/> — the recipe-level twin of <see cref="ManifestOmitting"/>.</summary>
    private static string ManifestWithRecipeOmitting(params string[] omit)
    {
        var fields = new Dictionary<string, string>
        {
            ["id"] = "\"cat\"",
            ["name"] = "\"Cat\"",
            ["weight"] = "1",
            ["layerOrder"] = "[]",
            ["layers"] = "[]",
            ["rules"] = "[]",
            ["absentPercent"] = "{}",
        };
        foreach (var name in omit) fields.Remove(name);
        string recipe = "{" + string.Join(",", fields.Select(kv => $"\"{kv.Key}\":{kv.Value}")) + "}";
        return "{\"menuSchemaVersion\":" + MenuSchema.Current.ToString(CultureInfo.InvariantCulture)
            + ",\"canvas\":{\"width\":2,\"height\":2},\"collectionName\":\"C\","
            + "\"collectionSymbol\":\"S\",\"recipes\":[" + recipe + "],"
            + "\"art\":{\"mode\":\"loose\",\"atlasWidth\":0,\"atlasHeight\":0,\"cells\":[]}}";
    }

    [Fact]
    public void A_recipe_missing_layerOrder_names_the_recipe_and_the_field()
    {
        byte[] bytes = ZipWith(ManifestWithRecipeOmitting("layerOrder"));
        var ex = Assert.Throws<InvalidDataException>(() => MenuArchive.Open(new MemoryStream(bytes)));
        Assert.Contains("cat", ex.Message, StringComparison.Ordinal);
        Assert.Contains("layerOrder", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_recipe_missing_layers_names_the_recipe_and_the_field()
    {
        byte[] bytes = ZipWith(ManifestWithRecipeOmitting("layers"));
        var ex = Assert.Throws<InvalidDataException>(() => MenuArchive.Open(new MemoryStream(bytes)));
        Assert.Contains("cat", ex.Message, StringComparison.Ordinal);
        Assert.Contains("layers", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_recipe_missing_absentPercent_names_the_recipe_and_the_field()
    {
        byte[] bytes = ZipWith(ManifestWithRecipeOmitting("absentPercent"));
        var ex = Assert.Throws<InvalidDataException>(() => MenuArchive.Open(new MemoryStream(bytes)));
        Assert.Contains("cat", ex.Message, StringComparison.Ordinal);
        Assert.Contains("absentPercent", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_recipe_missing_rules_names_the_recipe_and_the_field()
    {
        byte[] bytes = ZipWith(ManifestWithRecipeOmitting("rules"));
        var ex = Assert.Throws<InvalidDataException>(() => MenuArchive.Open(new MemoryStream(bytes)));
        Assert.Contains("cat", ex.Message, StringComparison.Ordinal);
        Assert.Contains("rules", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_menuSchemaVersion_that_is_not_a_number_is_a_named_refusal()
    {
        // v.GetInt32() on a string-typed JsonElement throws InvalidOperationException, which the
        // JsonException catch around it does not see — this used to reach the caller as a bare,
        // unnamed InvalidOperationException instead of the InvalidDataException every other
        // malformed-manifest case throws.
        byte[] bytes = ZipWith("""{"menuSchemaVersion":"1"}""");
        var ex = Assert.Throws<InvalidDataException>(() => MenuArchive.Open(new MemoryStream(bytes)));
        Assert.Contains("menuSchemaVersion", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_menuSchemaVersion_of_zero_says_it_is_invalid_rather_than_too_new()
    {
        // Nothing predates schema 1 (MenuSchema.Oldest), so 0 is a malformed manifest — the "this
        // build supports up to N" phrasing used to fire here too, which reads as an invitation to
        // upgrade something that was never a valid Menu in the first place.
        byte[] bytes = ZipWith("""{"menuSchemaVersion":0}""");
        var ex = Assert.Throws<NotSupportedException>(() => MenuArchive.Open(new MemoryStream(bytes)));
        Assert.DoesNotContain("supports up to", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_integer_enum_value_on_the_wire_is_refused_rather_than_silently_accepted()
    {
        // "kind":1 on a layer is a raw integer where the wire format expects a named string
        // ("dynamic"/"static"/"custom"); the default JsonStringEnumConverter reads either, which
        // would let a hand-edited or corrupted manifest silently pick a different LayerKind than
        // the one written.
        string recipe = "{\"id\":\"cat\",\"name\":\"Cat\",\"weight\":1,\"layerOrder\":[\"bg\"],"
            + "\"layers\":[{\"id\":\"bg\",\"name\":\"Bg\",\"kind\":1,\"colorization\":null,"
            + "\"variants\":[{\"id\":\"a\",\"name\":\"A\",\"weight\":1}]}],"
            + "\"rules\":[],\"absentPercent\":{}}";
        byte[] bytes = ZipWith(
            "{\"menuSchemaVersion\":" + MenuSchema.Current.ToString(CultureInfo.InvariantCulture)
            + ",\"canvas\":{\"width\":2,\"height\":2},\"collectionName\":\"C\","
            + "\"collectionSymbol\":\"S\",\"recipes\":[" + recipe + "],"
            + "\"art\":{\"mode\":\"loose\",\"atlasWidth\":0,\"atlasHeight\":0,\"cells\":[]}}");
        Assert.Throws<InvalidDataException>(() => MenuArchive.Open(new MemoryStream(bytes)));
    }
}
