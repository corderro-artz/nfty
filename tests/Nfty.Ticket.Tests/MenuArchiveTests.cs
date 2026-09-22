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
        Assert.ThrowsAny<Exception>(() => MenuArchive.Open(new MemoryStream(ms.ToArray())));
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
        Assert.Contains("cat/bg/a", ex.Message, StringComparison.Ordinal);
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
