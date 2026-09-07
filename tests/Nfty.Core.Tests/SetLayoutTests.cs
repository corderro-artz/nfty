using System.Globalization;
using Nfty.Core.Output;

namespace Nfty.Core.Tests;

/// <summary>
/// What a Set is made of, as an include list - the single description that separates the Set from
/// the folder the user happens to keep it in.
/// </summary>
public class SetLayoutTests
{
    /// <summary>A folder holding a Set's parts plus a pile of the author's own files.</summary>
    private static string FolderWithSetAndClutter()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(SetLayout.ManifestPath(dir), "{}");
        foreach (var name in SetLayout.Directories)
        {
            Directory.CreateDirectory(Path.Combine(dir, name));
            File.WriteAllText(Path.Combine(dir, name, "0001.dat"), name);
        }

        File.WriteAllText(Path.Combine(dir, "Book.cbk"), "source");
        File.WriteAllText(Path.Combine(dir, "out.set"), "a previous pack");
        Directory.CreateDirectory(Path.Combine(dir, "art"));
        File.WriteAllText(Path.Combine(dir, "art", "body.png"), "source art");
        return dir;
    }

    [Fact]
    public void It_lists_the_sets_own_files_and_nothing_else_in_the_folder()
    {
        var dir = FolderWithSetAndClutter();

        var names = SetLayout.FilesIn(dir).Select(p => Path.GetRelativePath(dir, p)).ToList();

        Assert.Equal(1 + SetLayout.Directories.Count, names.Count);
        Assert.Contains(SetLayout.ManifestFile, names);
        Assert.DoesNotContain("Book.cbk", names);
        Assert.DoesNotContain("out.set", names);
        Assert.DoesNotContain(names, n => n.Contains("art", StringComparison.Ordinal));
    }

    [Fact]
    public void A_subfolder_of_a_set_directory_is_still_part_of_the_set()
    {
        // The boundary is the DIRECTORY, not the file type or the depth: images/ is nfty's own
        // namespace inside the user's folder. Filtering on ".png" instead would silently drop an
        // asset the day the format grows a second encoding.
        var dir = FolderWithSetAndClutter();
        Directory.CreateDirectory(Path.Combine(dir, SetLayout.ImagesDir, "hi-res"));
        File.WriteAllText(Path.Combine(dir, SetLayout.ImagesDir, "hi-res", "0001.webp"), "x");

        var names = SetLayout.FilesIn(dir).Select(p => Path.GetRelativePath(dir, p)).ToList();

        Assert.Contains(names, n => n.EndsWith("0001.webp", StringComparison.Ordinal));
    }

    [Fact]
    public void The_listing_is_ordinal_so_the_same_set_packs_identically_on_any_machine()
    {
        // These paths become ZIP entries in this order. A default OrderBy sorts by the CURRENT
        // CULTURE, so the same Set would pack to different bytes on an en-US box than a sv-SE one -
        // the rule every sort that reaches an output file here follows.
        var dir = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(SetLayout.ManifestPath(dir), "{}");
        Directory.CreateDirectory(Path.Combine(dir, SetLayout.ImagesDir));
        // Chosen so the two comparers genuinely disagree: ordinal compares code points, which puts
        // every uppercase letter before every lowercase one ('B' is 66, 'a' is 97), while a
        // culture-aware compare orders a before B. They differ by more than CASE as well, because a
        // Windows filesystem is case-insensitive and would fold "a.png" and "A.png" into one file -
        // which is how the first draft of this test failed.
        foreach (var leaf in new[] { "ay.png", "Bz.png", "cx.png" })
            File.WriteAllText(Path.Combine(dir, SetLayout.ImagesDir, leaf), "x");

        var listed = SetLayout.FilesIn(dir);

        Assert.Equal(listed.OrderBy(p => p, StringComparer.Ordinal).ToList(), listed);
        // ...and the names above are ones the two comparers really do order differently, so the
        // assertion above cannot quietly become a tautology if someone edits the list.
        Assert.NotEqual(
            listed.OrderBy(p => p, StringComparer.Create(new CultureInfo("en-US"), false)).ToList(),
            listed);
    }

    [Fact]
    public void An_empty_or_absent_folder_lists_nothing_rather_than_throwing()
    {
        // Pack runs after a write that created all three directories, but FilesIn is a public
        // description of the format and will be called on a folder that is not a Set. Answering
        // "nothing" is right; IsSetFolder is the separate question of whether it is a Set at all.
        Assert.Empty(SetLayout.FilesIn(Directory.CreateTempSubdirectory().FullName));
        Assert.Empty(SetLayout.FilesIn(Path.Combine(Path.GetTempPath(), "nfty-absent-" + Guid.NewGuid())));
        Assert.Throws<ArgumentException>(() => SetLayout.FilesIn("  "));
    }
}
