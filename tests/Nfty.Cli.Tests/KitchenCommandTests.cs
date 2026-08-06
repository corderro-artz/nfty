using System.CommandLine;
using Nfty.Core.Formats;
using Nfty.Core.Model;

namespace Nfty.Cli.Tests;

/// <summary>
/// The Kitchen was a GUI-only concept. It is one of the six domain terms, its extension was already
/// in <see cref="Archives.KindOf"/>, and the command line could neither create one nor look at one:
/// <c>inspect</c> resolved a <c>.ktn</c> to <see cref="ArchiveKind.Kitchen"/> and then threw
/// "inspect does not know how to print archive kind 'Kitchen'" — exactly the gap its own default
/// branch was written to guard against.
/// </summary>
public class KitchenCommandTests
{
    private static readonly InvocationConfiguration NonThrowing = new() { EnableDefaultExceptionHandler = false };

    private static int Run(params string[] args) =>
        CommandFactory.Build().Parse(args).Invoke(NonThrowing);

    private static string Capture(params string[] args)
    {
        var previous = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try { Run(args); }
        finally { Console.SetOut(previous); }
        return writer.ToString();
    }

    [Fact]
    public void New_kitchen_creates_a_readable_workspace()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string ktn = Path.Combine(tmp.FullName, "Studio.ktn");

            Assert.Equal(0, Run("new", "kitchen", ktn));

            var contents = Kitchen.Open(ktn);
            Assert.Equal("Studio", contents.Manifest.Name);
            Assert.Equal("studio", contents.Manifest.Id);
            Assert.True(contents.IsEmpty);
        }
        finally { tmp.Delete(recursive: true); }
    }

    /// <summary>The display name may contain spaces; the id is derived the same way the GUI wizard
    /// derives it, so a Kitchen made either way is the same Kitchen.</summary>
    [Fact]
    public void New_kitchen_derives_its_id_from_the_name_like_the_gui_does()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string ktn = Path.Combine(tmp.FullName, "workspace.ktn");
            Assert.Equal(0, Run("new", "kitchen", ktn, "--name", "Vapor Studio"));

            var contents = Kitchen.Open(ktn);
            Assert.Equal("Vapor Studio", contents.Manifest.Name);
            Assert.Equal("vapor-studio", contents.Manifest.Id);
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void New_kitchen_refuses_a_path_that_is_not_a_ktn()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            var ex = Assert.Throws<ArgumentException>(
                () => Run("new", "kitchen", Path.Combine(tmp.FullName, "Studio.cbk")));
            Assert.Contains(".ktn", ex.Message);
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Inspect_lists_what_a_kitchen_holds()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string ktn = Path.Combine(tmp.FullName, "Studio.ktn");
            Kitchen.Create(ktn, new KitchenManifest("studio", "Studio"));

            // Empty files on purpose. Membership is a directory scan by extension — a Kitchen
            // lists PATHS and never opens what it names, which is the whole reason inspecting a
            // workspace does not decode every PNG in it. Using real archives here would test
            // ImageSharp, not the listing.
            File.WriteAllText(Path.Combine(tmp.FullName, "VaporPets.cbk"), "");
            File.WriteAllText(Path.Combine(tmp.FullName, "aura.igt"), "");

            var text = Capture("inspect", ktn);

            Assert.Contains("Kitchen: Studio [studio]", text);
            Assert.Contains("VaporPets.cbk", text);
            Assert.Contains("aura.igt", text);
            // A kind with nothing in it is omitted, not printed as an empty heading.
            Assert.DoesNotContain("Recipes", text);
            // Bare file names: the folder is stated once, and absolute paths cannot be pasted
            // usefully into an issue.
            Assert.DoesNotContain(Path.Combine(tmp.FullName, "aura.igt"), text);
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Inspect_says_when_a_workspace_is_empty_rather_than_printing_nothing()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string ktn = Path.Combine(tmp.FullName, "Studio.ktn");
            Kitchen.Create(ktn, new KitchenManifest("studio", "Studio"));

            var text = Capture("inspect", ktn);

            Assert.Contains("empty", text);
        }
        finally { tmp.Delete(recursive: true); }
    }
}
