using Nfty.Core.Formats;
using Nfty.Core.Model;
using Nfty.Core.Stats;

namespace Nfty.Core.Tests;

/// <summary>
/// <c>Kitchen.FindIn</c> returned null for two unrelated situations — "this folder is not a
/// workspace" and "this folder holds two <c>.ktn</c> files and I will not guess which you meant" —
/// so a caller could report the first but had no way to tell the user about the second, which is the
/// one only they can fix.
/// </summary>
public class KitchenLookupTests
{
    private static string NewDir() => Directory.CreateTempSubdirectory().FullName;

    private static string Ktn(string dir, string name)
    {
        string path = Path.Combine(dir, name + Kitchen.Extension);
        Kitchen.Create(path, new KitchenManifest(name.ToLowerInvariant(), name));
        return path;
    }

    [Fact]
    public void One_ktn_is_a_workspace()
    {
        var dir = NewDir();
        var path = Ktn(dir, "Studio");

        var (outcome, found) = Kitchen.TryFindIn(dir);

        Assert.Equal(KitchenLookup.Found, outcome);
        Assert.Equal(path, found);
        Assert.Equal(path, Kitchen.FindIn(dir));
    }

    [Fact]
    public void No_ktn_is_an_ordinary_folder_not_an_error()
    {
        var (outcome, found) = Kitchen.TryFindIn(NewDir());

        Assert.Equal(KitchenLookup.NotAWorkspace, outcome);
        Assert.Null(found);
    }

    [Fact]
    public void A_missing_folder_is_also_just_not_a_workspace()
    {
        var (outcome, _) = Kitchen.TryFindIn(Path.Combine(NewDir(), "nope"));

        Assert.Equal(KitchenLookup.NotAWorkspace, outcome);
    }

    /// <summary>Two Kitchens over one folder would have identical contents and neither would be
    /// wrong, so this is reported rather than resolved by picking the first.</summary>
    [Fact]
    public void Two_ktns_are_ambiguous_and_say_so()
    {
        var dir = NewDir();
        Ktn(dir, "Alpha");
        Ktn(dir, "Beta");

        var (outcome, found) = Kitchen.TryFindIn(dir);

        Assert.Equal(KitchenLookup.Ambiguous, outcome);
        Assert.Null(found);
        Assert.Null(Kitchen.FindIn(dir));   // the old shape still behaves as before
    }

    // ---- the listing --------------------------------------------------------------------------

    [Fact]
    public void The_report_names_the_workspace_and_groups_what_it_holds()
    {
        var dir = NewDir();
        var ktn = Ktn(dir, "Studio");
        File.WriteAllText(Path.Combine(dir, "b.cbk"), "");
        File.WriteAllText(Path.Combine(dir, "a.cbk"), "");
        File.WriteAllText(Path.Combine(dir, "cat.rcp"), "");

        var text = KitchenReport.Render(Kitchen.Open(ktn));

        Assert.Contains("Kitchen: Studio [studio]", text);
        Assert.Contains("CookBooks (2):", text);
        Assert.Contains("Recipes (1):", text);
        Assert.DoesNotContain("Ingredients", text);   // omitted, not an empty heading

        // Ordinal order, as every list in KitchenContents is: a listing that reorders itself by
        // machine locale is its own small bug.
        Assert.True(text.IndexOf("a.cbk", StringComparison.Ordinal)
                  < text.IndexOf("b.cbk", StringComparison.Ordinal));
    }

    [Fact]
    public void An_empty_workspace_says_so()
    {
        var dir = NewDir();
        var text = KitchenReport.Render(Kitchen.Open(Ktn(dir, "Fresh")));

        Assert.Contains("empty", text);
        Assert.DoesNotContain("CookBooks", text);
    }

    /// <summary>The report is copied between machines like the other two, so it must not carry the
    /// scanning machine's absolute paths into every row.</summary>
    [Fact]
    public void Rows_are_bare_file_names()
    {
        var dir = NewDir();
        var ktn = Ktn(dir, "Studio");
        File.WriteAllText(Path.Combine(dir, "aura.igt"), "");

        var text = KitchenReport.Render(Kitchen.Open(ktn));

        Assert.Contains("aura.igt", text);
        Assert.DoesNotContain(Path.Combine(dir, "aura.igt"), text);
    }
}
