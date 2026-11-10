using System.Linq;
using System.Reflection;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>The catalog is the one list the sheet's pages and its search both read.</summary>
public class HelpCatalogTests
{
    /// <summary>Every string property of <see cref="KeyHints"/>.</summary>
    internal static string[] AllHints() =>
        typeof(KeyHints).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(string)).Select(p => (string)p.GetValue(null)!).ToArray();

    [Fact]
    public void Every_key_is_a_KeyHints_string_or_a_plain_key_with_no_modifier_in_it()
    {
        var hints = AllHints().ToHashSet();
        string[] modifiers = ["Ctrl", "Shift", "Alt", "⌘", "⌥", "⇧", "⌃"];
        var keys = HelpCatalog.Entries.OfType<HelpKey>().ToList();
        Assert.True(keys.Count > 30, "a catalog with no keys proves nothing");
        foreach (var k in keys)
        {
            if (hints.Contains(k.Key)) continue;
            foreach (var m in modifiers)
                Assert.False(k.Key.Contains(m, StringComparison.Ordinal),
                    $"'{k.Key}' ({k.Term}) spells a modifier by hand; it must come from KeyHints");
        }
    }

    [Fact]
    public void Every_tab_has_all_four_columns_filled()
    {
        foreach (var t in Enum.GetValues<HelpTab>())
            for (var c = 0; c < 4; c++)
                Assert.Contains(HelpCatalog.Entries, e => e.Tab == t && e.Column == c);
    }

    [Fact]
    public void The_editor_page_lists_every_tool_chord_the_editor_binds()
    {
        var keys = HelpCatalog.Entries.Where(e => e.Tab == HelpTab.Editor).OfType<HelpKey>().Select(k => k.Key).ToHashSet();
        foreach (var k in new[] { "B", "E", "G", "R", "C", "T", "L", "M", "[  ]", KeyHints.MarkAll, "Delete",
                     KeyHints.Undo, KeyHints.Redo, KeyHints.RedoAlt, KeyHints.Constrain, KeyHints.FromCenter, "Wheel" })
            Assert.Contains(k, keys);
    }

    [Fact]
    public void The_community_gallery_is_in_the_basics_and_names_the_real_button()
    {
        var hit = Assert.Single(HelpCatalog.Search("community gallery"));
        Assert.Equal(HelpTab.Basics, hit.Tab);
        // The sentence must name the control Landing really carries.
        Assert.Contains("Browse the community gallery", hit.Description);
    }

    [Fact]
    public void Search_finds_across_tabs_case_insensitively_and_needs_every_word()
    {
        var zoom = HelpCatalog.Search("ZOOM");
        Assert.True(zoom.Select(e => e.Tab).Distinct().Count() >= 3);
        Assert.All(zoom, e => Assert.Contains("zoom", e.SearchText));

        Assert.NotEmpty(HelpCatalog.Search("redo"));
        Assert.Contains(HelpCatalog.Search("kitchen"), e => e is HelpTerm { Term: "Kitchen" });
        // matches the tab and section names, and the key text
        Assert.NotEmpty(HelpCatalog.Search("inspector"));
        Assert.Contains(HelpCatalog.Search(KeyHints.Theme), e => e is HelpKey { Term: "light / dark" });
        // AND, not OR
        Assert.All(HelpCatalog.Search("zoom editor"), e => Assert.Equal(HelpTab.Editor, e.Tab));
        Assert.Empty(HelpCatalog.Search("qzxv"));
        Assert.Empty(HelpCatalog.Search("   "));
    }

    [Fact]
    public void Markup_never_leaks_into_what_search_reads_or_a_result_shows()
    {
        foreach (var e in HelpCatalog.Entries)
        {
            Assert.DoesNotContain('*', e.Plain);
            Assert.DoesNotContain('^', e.Plain);
        }
    }

    [Fact]
    public void Every_page_entry_is_on_exactly_one_page_so_search_and_pages_cannot_disagree()
    {
        var vm = new HelpViewModel(new FakeDialogs());
        var onPages = vm.Pages.SelectMany(p => p.Columns).SelectMany(c => c.Sections).SelectMany(s => s.Entries).ToList();
        Assert.Equal(HelpCatalog.Entries.Count, onPages.Count);
        Assert.True(HelpCatalog.Entries.All(e => onPages.Contains(e)));
    }
}
