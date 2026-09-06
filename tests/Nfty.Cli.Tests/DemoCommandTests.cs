using System.CommandLine;
using Nfty.Cli;
using Nfty.Core.Demo;
using Nfty.Core.Formats;

namespace Nfty.Cli.Tests;

/// <summary>
/// <c>nfty demo</c>: writes the built-in sample CookBook out. The command exists because the
/// archive is EMBEDDED in the program — without it, a download's only copy of the demo would be one
/// the GUI had unpacked, which is no use to someone who came for the command line.
/// </summary>
public class DemoCommandTests
{
    private static readonly InvocationConfiguration NonThrowing = new() { EnableDefaultExceptionHandler = false };

    private static int Run(params string[] args) =>
        CommandFactory.Build().Parse(args).Invoke(NonThrowing);

    [Fact]
    public void It_writes_a_readable_cookbook_into_a_folder_that_did_not_exist()
    {
        var dir = Path.Combine(Directory.CreateTempSubdirectory().FullName, "fresh");

        Assert.Equal(0, Run("demo", dir));

        var path = Path.Combine(dir, DemoCookBook.FileName);
        Assert.True(File.Exists(path));
        // Reading it is the assertion that matters: "a file appeared" would pass on zero bytes.
        using var book = CookBookArchive.Read(path);
        Assert.Equal(DemoCookBook.DisplayName, book.Manifest.Name);
    }

    [Fact]
    public void It_leaves_an_existing_copy_alone_unless_forced()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, DemoCookBook.FileName);
        Run("demo", dir);
        File.WriteAllText(path, "mine");

        Assert.Equal(0, Run("demo", dir));
        Assert.Equal("mine", File.ReadAllText(path));

        Assert.Equal(0, Run("demo", dir, "--force"));
        Assert.NotEqual("mine", File.ReadAllText(path));
    }
}
