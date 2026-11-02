using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

/// <summary>Which page of the quick-reference sheet is showing.</summary>
public enum HelpTab
{
    /// <summary>The six words, layer kinds, rules, color and the everyday keys.</summary>
    Basics,

    /// <summary>Rise: painting depth and the 3D view.</summary>
    Rise,

    /// <summary>The pop-out viewer and the runtime Menu and Ticket.</summary>
    Viewer,
}

/// <summary>Modal quick-reference legend in three tabbed pages: the basics (domain terms, kinds,
/// rule/state glyphs, keyboard chords, color prefixes), Rise and the 3D view, and the viewer and
/// runtime. The selected tab is view state only. Closes itself via <see cref="IDialogService"/>.</summary>
public partial class HelpViewModel : ViewModelBase
{
    private readonly IDialogService _dialogs;
    /// <summary>Creates the help sheet.</summary>
    /// <param name="dialogs">The dialog layer to close through.</param>
    public HelpViewModel(IDialogService dialogs) => _dialogs = dialogs;

    /// <summary>Which page is showing; starts on the basics.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBasicsTab))]
    [NotifyPropertyChangedFor(nameof(IsRiseTab))]
    [NotifyPropertyChangedFor(nameof(IsViewerTab))]
    private HelpTab _tab = HelpTab.Basics;

    /// <summary>Whether the basics page is showing.</summary>
    public bool IsBasicsTab => Tab == HelpTab.Basics;
    /// <summary>Whether the Rise page is showing.</summary>
    public bool IsRiseTab => Tab == HelpTab.Rise;
    /// <summary>Whether the viewer page is showing.</summary>
    public bool IsViewerTab => Tab == HelpTab.Viewer;

    [RelayCommand] private void ShowTab(HelpTab tab) => Tab = tab;
    [RelayCommand] private void Close() => _dialogs.Close(null);
}
