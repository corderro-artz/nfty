using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Nfty.App.Services;
using Nfty.Core.Imaging;
using Nfty.Core.Output;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// A failure the app shows is in the product's words: nfty's own messages pass through verbatim,
/// a framework's never reach the reader. Each case raises the REAL framework exception (a missing
/// file, a ZIP that is not one, broken JSON), because a hand-constructed exception has no throwing
/// method and so cannot tell the two apart the way the running app does.
/// </summary>
public class ErrorTextTests
{
    private static Exception Caught(Action act)
    {
        try { act(); }
        catch (Exception ex) { return ex; }
        throw new InvalidOperationException("expected a throw");
    }

    [Fact]
    public void A_zip_reader_message_never_reaches_the_reader()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory().FullName, "not-a.set");
        File.WriteAllText(path, "this is not a zip");
        var ex = Caught(() => ZipFile.OpenRead(path).Dispose());

        var text = ErrorText.For(ex);
        Assert.DoesNotContain(ex.Message, text);
        Assert.DoesNotContain("Central Directory", text);
        Assert.Contains("damaged", text);
    }

    [Fact]
    public void A_missing_file_names_the_file_and_what_to_do()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory().FullName, "gone.cbk");
        var ex = Caught(() => File.ReadAllBytes(path));

        var text = ErrorText.For(ex);
        Assert.DoesNotContain(ex.Message, text);
        Assert.Contains("gone.cbk", text);
        Assert.Contains("Choose it again", text);
    }

    [Fact]
    public void A_json_parser_offset_never_reaches_the_reader()
    {
        var ex = Caught(() => JsonSerializer.Deserialize<Dictionary<string, int>>("{ \"a\": nope }"));

        var text = ErrorText.For(ex);
        Assert.DoesNotContain(ex.Message, text);
        Assert.DoesNotContain("BytePosition", text);
    }

    [Fact]
    public void A_framework_invalid_operation_is_mapped_even_though_core_uses_the_same_type()
    {
        var ex = Caught(() => Array.Empty<int>().First());

        Assert.False(ErrorText.IsOwnWording(ex));
        Assert.DoesNotContain(ex.Message, ErrorText.For(ex));
    }

    [Fact]
    public void A_message_nfty_threw_passes_through_verbatim()
    {
        // A framework TYPE, thrown by nfty's own code with nfty's own sentence.
        var spec = Caught(() => ColorSpec.Parse("d6249f"));
        Assert.True(ErrorText.IsOwnWording(spec));
        Assert.Equal(spec.Message, ErrorText.For(spec));

        // nfty's own exception TYPE.
        var corrupt = Caught(() => throw new CorruptSetException("x.set", "The Set's set.json is missing."));
        Assert.Equal("The Set's set.json is missing.", ErrorText.For(corrupt));
    }

    [Fact]
    public void A_single_exception_inside_an_aggregate_is_judged_on_its_own()
    {
        var spec = Caught(() => ColorSpec.Parse("d6249f"));
        Assert.Equal(spec.Message, ErrorText.For(new AggregateException(spec)));
    }

    // THE SWEEP. Every place the GUI shows a failure goes through ErrorText.For; a bare ex.Message
    // reintroduced anywhere is how "End of Central Directory record could not be found" reached an
    // error card. ErrorText.cs itself is the one file allowed to read the message.
    private static readonly string[] Exempt = { "ErrorText.cs" };

    [Fact]
    public void No_view_model_or_service_shows_a_raw_exception_message()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "nfty.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var app = Path.Combine(dir!.FullName, "src", "Nfty.App");

        var raw = new Regex(@"(?<![\w.])(ex|e|exception|err)\.Message(?!\w)");
        var offenders = Directory.EnumerateFiles(app, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => !Exempt.Contains(Path.GetFileName(f)))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (f, line, i)))
            .Where(x => raw.IsMatch(x.line))
            .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}: {x.line.Trim()}")
            .ToList();
        Assert.True(offenders.Count == 0, "raw exception text shown to the user:\n" + string.Join("\n", offenders));
    }
}
