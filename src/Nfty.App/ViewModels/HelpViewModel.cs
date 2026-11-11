using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

/// <summary>Which page of the quick-reference sheet is showing.</summary>
public enum HelpTab
{
    /// <summary>The six words, layer kinds, rules, color and the everyday keys.</summary>
    Basics,

    /// <summary>The CookBook Explorer: its tree, its panes and their keys.</summary>
    Explorer,

    /// <summary>The Ingredient editor's tools and keys, and the Set inspector's.</summary>
    Editor,

    /// <summary>Rise: painting depth and the 3D view.</summary>
    Rise,

    /// <summary>The pop-out viewer and the runtime Menu and Ticket.</summary>
    Viewer,

    /// <summary>The Community page: what it is, its search, and its keys.</summary>
    Community,
}

/// <summary>A run of entries under one label (or, for <see cref="Dashed"/>, under a dashed rule).</summary>
public sealed class HelpSection
{
    /// <summary>Creates a section.</summary>
    /// <param name="title">The already uppercased section label.</param>
    /// <param name="dashed">Use a separator instead of the label.</param>
    /// <param name="first">Omit the leading gap for the first section in a column.</param>
    /// <param name="entries">Entries in their displayed order.</param>
    public HelpSection(string title, bool dashed, bool first, IReadOnlyList<HelpEntry> entries)
    {
        Title = title; Dashed = dashed; Entries = entries;
        // A nesting line under the label tucks up to it.
        var chain = entries.Count > 0 && entries[0] is HelpNote { Chain: true };
        TitleMargin = new Avalonia.Thickness(0, first ? 0 : 18, 0, chain ? 8 : first ? 14 : 12);
        RuleMargin = new Avalonia.Thickness(0, first ? 0 : 22, 0, 15);
    }

    /// <summary>The label, uppercased.</summary>
    public string Title { get; }

    /// <summary>Whether this block hangs under a dashed rule instead of a label.</summary>
    public bool Dashed { get; }

    /// <summary>Whether the label is drawn.</summary>
    public bool HasTitle => !Dashed;

    /// <summary>The entries.</summary>
    public IReadOnlyList<HelpEntry> Entries { get; }

    /// <summary>Space around the label.</summary>
    public Avalonia.Thickness TitleMargin { get; }

    /// <summary>Space around the dashed rule.</summary>
    public Avalonia.Thickness RuleMargin { get; }
}

/// <summary>One column of a page.</summary>
public sealed class HelpColumn
{
    /// <summary>Creates a column.</summary>
    /// <param name="sections">Sections in their displayed top-to-bottom order.</param>
    public HelpColumn(IReadOnlyList<HelpSection> sections) => Sections = sections;

    /// <summary>The sections, top to bottom.</summary>
    public IReadOnlyList<HelpSection> Sections { get; }
}

/// <summary>One tab's page: four columns built from the catalog. All pages share one grid cell, so
/// whether a page is showing is ink (<see cref="IsActive"/>), never layout.</summary>
public sealed partial class HelpPage : ObservableObject
{
    /// <summary>Builds the page for <paramref name="tab"/>.</summary>
    public HelpPage(HelpTab tab)
    {
        Tab = tab;
        var cols = new List<HelpColumn>();
        for (var c = 0; c < 4; c++)
        {
            var sections = new List<HelpSection>();
            var entries = HelpCatalog.Entries.Where(e => e.Tab == tab && e.Column == c).ToList();
            var i = 0;
            while (i < entries.Count)
            {
                var head = entries[i];
                var run = entries.Skip(i).TakeWhile(e => e.Section == head.Section && e.Dashed == head.Dashed).ToList();
                sections.Add(new HelpSection(head.Section, head.Dashed, sections.Count == 0, run));
                i += run.Count;
            }
            cols.Add(new HelpColumn(sections));
        }
        Columns = cols;
        Column0 = cols[0]; Column1 = cols[1]; Column2 = cols[2]; Column3 = cols[3];
    }

    /// <summary>The tab this page belongs to.</summary>
    public HelpTab Tab { get; }

    /// <summary>The four columns.</summary>
    public IReadOnlyList<HelpColumn> Columns { get; }

    /// <summary>Column 0.</summary>
    public HelpColumn Column0 { get; }
    /// <summary>Column 1.</summary>
    public HelpColumn Column1 { get; }
    /// <summary>Column 2.</summary>
    public HelpColumn Column2 { get; }
    /// <summary>Column 3.</summary>
    public HelpColumn Column3 { get; }

    /// <summary>Whether this page is the one showing.</summary>
    [ObservableProperty]
    private bool _isActive;
}

/// <summary>Modal quick-reference legend in four tabbed pages (basics, editor, Rise and 3D, viewer
/// and runtime) with a search over the lot. Pages and search both read <see cref="HelpCatalog"/>, so
/// they cannot disagree. The selected tab and the query are view state only. Closes itself via
/// <see cref="IDialogService"/>.</summary>
public partial class HelpViewModel : ViewModelBase
{
    private readonly IDialogService _dialogs;

    /// <summary>Creates the help sheet.</summary>
    /// <param name="dialogs">The dialog layer to close through.</param>
    public HelpViewModel(IDialogService dialogs)
    {
        _dialogs = dialogs;
        Pages = Enum.GetValues<HelpTab>().Select(t => new HelpPage(t)).ToList();
        SyncPages();
    }

    /// <summary>One page per tab.</summary>
    public IReadOnlyList<HelpPage> Pages { get; }

    /// <summary>Which page is showing; starts on the basics.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBasicsTab))]
    [NotifyPropertyChangedFor(nameof(IsExplorerTab))]
    [NotifyPropertyChangedFor(nameof(IsEditorTab))]
    [NotifyPropertyChangedFor(nameof(IsRiseTab))]
    [NotifyPropertyChangedFor(nameof(IsViewerTab))]
    [NotifyPropertyChangedFor(nameof(IsCommunityTab))]
    private HelpTab _tab = HelpTab.Basics;

    /// <summary>Whether the basics tab is the chosen one. While a search is under way no tab is: the
    /// results are not a page.</summary>
    public bool IsBasicsTab => !IsSearching && Tab == HelpTab.Basics;
    /// <summary>Whether the Explorer page is showing.</summary>
    public bool IsExplorerTab => !IsSearching && Tab == HelpTab.Explorer;
    /// <summary>Whether the editor page is showing.</summary>
    public bool IsEditorTab => !IsSearching && Tab == HelpTab.Editor;
    /// <summary>Whether the Rise page is showing.</summary>
    public bool IsRiseTab => !IsSearching && Tab == HelpTab.Rise;
    /// <summary>Whether the viewer page is showing.</summary>
    public bool IsViewerTab => !IsSearching && Tab == HelpTab.Viewer;
    /// <summary>Whether the Community page is showing.</summary>
    public bool IsCommunityTab => !IsSearching && Tab == HelpTab.Community;

    partial void OnTabChanged(HelpTab value) => SyncPages();

    private void SyncPages()
    {
        foreach (var p in Pages) p.IsActive = p.Tab == Tab;
    }

    /// <summary>What was typed in the search box.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearching))]
    [NotifyPropertyChangedFor(nameof(ShowPages))]
    [NotifyPropertyChangedFor(nameof(IsBasicsTab))]
    [NotifyPropertyChangedFor(nameof(IsExplorerTab))]
    [NotifyPropertyChangedFor(nameof(IsEditorTab))]
    [NotifyPropertyChangedFor(nameof(IsRiseTab))]
    [NotifyPropertyChangedFor(nameof(IsViewerTab))]
    [NotifyPropertyChangedFor(nameof(IsCommunityTab))]
    private string _query = "";

    /// <summary>Whether a search is under way: the results replace the pages.</summary>
    public bool IsSearching => Query.Trim().Length > 0;

    /// <summary>Whether the tab pages are showing (no search).</summary>
    public bool ShowPages => !IsSearching;

    /// <summary>Whether the results list (not the empty state) is showing.</summary>
    public bool HasResults => IsSearching && Results.Count > 0;

    /// <summary>Whether the empty state is showing: a query that matched nothing.</summary>
    public bool NoMatch => IsSearching && Results.Count == 0;

    /// <summary>What the search found, in page order.</summary>
    public IReadOnlyList<HelpEntry> Results { get; private set; } = [];

    /// <summary>"6 matches", with the hint that Enter opens the first.</summary>
    public string ResultCountText => Results.Count switch
    {
        0 => "",
        1 => "1 match  ·  Enter opens it",
        var n => $"{n} matches  ·  Enter opens the first",
    };

    /// <summary>The empty state's sentence, naming the query.</summary>
    public string EmptyText => $"Nothing matches “{Query.Trim()}”";

    partial void OnQueryChanged(string value)
    {
        Results = HelpCatalog.Search(value);
        OnPropertyChanged(nameof(Results));
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(NoMatch));
        OnPropertyChanged(nameof(ResultCountText));
        OnPropertyChanged(nameof(EmptyText));
    }

    [RelayCommand]
    private void ShowTab(HelpTab tab)
    {
        Query = "";
        Tab = tab;
    }

    /// <summary>Clears the search and shows the page the entry lives on.</summary>
    [RelayCommand]
    private void Jump(HelpEntry? entry)
    {
        if (entry is null) return;
        Query = "";
        Tab = entry.Tab;
    }

    /// <summary>Jumps to the first result (Enter in the search box).</summary>
    [RelayCommand]
    private void OpenFirst()
    {
        if (Results.Count > 0) Jump(Results[0]);
    }

    [RelayCommand] private void ClearSearch() => Query = "";
    [RelayCommand] private void Close() => _dialogs.Close(this, null);
}
