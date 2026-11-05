using System.Globalization;
using System.IO.Compression;
using System.Text;
using Nfty.Ticket;

namespace Nfty.Ticket.Tests;

/// <summary>
/// The hand-written Menu manifest reader (no System.Text.Json, so Nfty.Ticket also builds for
/// netstandard2.1) keeps the rules the old serializer options stated.
/// </summary>
public class MenuJsonTests
{
    private static MenuArchive Open(string manifestJson)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        using (var w = new StreamWriter(zip.CreateEntry("manifest.json").Open(), new UTF8Encoding(false)))
            w.Write(manifestJson);
        ms.Position = 0;
        return MenuArchive.Open(ms);
    }

    private static string Book(string recipes = "[]", string extra = "") =>
        "{\"menuSchemaVersion\":" + MenuSchema.Current.ToString(CultureInfo.InvariantCulture)
        + ",\"canvas\":{\"width\":2,\"height\":2},\"collectionName\":\"C \\u00e9\\\"q\","
        + "\"collectionSymbol\":\"S\",\"recipes\":" + recipes + ","
        + "\"art\":{\"mode\":\"loose\",\"atlasWidth\":0,\"atlasHeight\":0,\"cells\":["
        + (recipes == "[]" ? "" : "{\"key\":\"cat/bg/a\",\"x\":0,\"y\":0,\"width\":2,\"height\":2}")
        + "]}" + extra + "}";

    private const string Recipe =
        "[{\"id\":\"cat\",\"name\":\"Cat\",\"weight\":2.5e0,\"layerOrder\":[\"bg\"],"
        + "\"layers\":[{\"id\":\"bg\",\"name\":\"BG\",\"kind\":\"dynamic\","
        + "\"colorization\":{\"model\":\"hsv\",\"hueQuantize\":30,\"satQuantize\":40,"
        + "\"entries\":[{\"weight\":1,\"range\":{\"hueMin\":0,\"hueMax\":360,\"satMin\":10,\"satMax\":90},\"fixed\":null}]},"
        + "\"variants\":[{\"id\":\"a\",\"name\":\"A\",\"weight\":0.5}]}],"
        + "\"rules\":[{\"type\":\"exclude\",\"when\":{\"ingredientId\":\"bg\",\"variantId\":\"a\"},\"targets\":[]}],"
        + "\"absentPercent\":{\"bg\":12.5}}]";

    [Fact]
    public void A_full_book_binds_every_member()
    {
        using var menu = Open(Book(Recipe, ",\"partialAlpha\":true,\"unknownFutureField\":[1,{\"x\":null}]"));
        var b = menu.Book;

        Assert.Equal("C é\"q", b.CollectionName);
        Assert.True(b.PartialAlpha);
        var r = Assert.Single(b.Recipes);
        Assert.Equal(2.5, r.Weight);
        Assert.Equal(12.5, r.AbsentPercent["bg"]);
        var layer = Assert.Single(r.Layers);
        Assert.Equal(Nfty.Core.Model.LayerKind.Dynamic, layer.Kind);
        Assert.Equal(30, layer.Colorization!.HueQuantize);
        Assert.Null(layer.Colorization.Entries[0].Fixed);
        Assert.Equal(90, layer.Colorization.Entries[0].Range!.SatMax);
        Assert.Equal(0.5, layer.Variants[0].Weight);
        Assert.Equal(Nfty.Core.Model.RuleType.Exclude, r.Rules[0].Type);
    }

    [Theory]
    [InlineData("{\"menuSchemaVersion\":2,}")]          // trailing comma
    [InlineData("{\"menuSchemaVersion\":2 /* c */}")]   // comment
    [InlineData("{\"menuSchemaVersion\":02}")]          // leading zero
    [InlineData("{\"menuSchemaVersion\":2")]            // truncated
    [InlineData("{\"menuSchemaVersion\":2} x")]         // trailing text
    public void Malformed_json_is_refused_as_invalid_data(string json)
    {
        Assert.Throws<InvalidDataException>(() => Open(json));
    }

    [Fact]
    public void An_enum_given_as_an_integer_is_refused()
    {
        string json = Book(Recipe.Replace("\"kind\":\"dynamic\"", "\"kind\":1", StringComparison.Ordinal));
        var ex = Assert.Throws<InvalidDataException>(() => Open(json));
        Assert.Contains("kind", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Null_for_a_non_nullable_member_is_refused()
    {
        string json = Book(Recipe.Replace("\"name\":\"Cat\"", "\"name\":null", StringComparison.Ordinal));
        var ex = Assert.Throws<InvalidDataException>(() => Open(json));
        Assert.Contains("name", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_fractional_whole_number_is_refused()
    {
        string json = Book().Replace("\"width\":2", "\"width\":2.5", StringComparison.Ordinal);
        var ex = Assert.Throws<InvalidDataException>(() => Open(json));
        Assert.Contains("width", ex.Message, StringComparison.Ordinal);
    }
}
