using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

public partial class ShellViewModel : ViewModelBase
{
    private readonly INavigationService _nav;
    private readonly IDialogService _dialogs;
    private readonly IThemeService _theme;
    private readonly IKitchenSession? _kitchen;
    private readonly ICookBookSession? _session;

    [ObservableProperty] private ViewModelBase? _currentPage;
    [ObservableProperty] private ViewModelBase? _activeDialog;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZoomScale))]
    [NotifyPropertyChangedFor(nameof(EffectivePageScale))]
    private int _zoom = 100;

    /// <summary>What 100% means. The mockups are authored at CSS 1:1, and reproducing them at exactly
    /// 1.0 leaves the app noticeably small on a large high-DPI display — every control is the right
    /// size in logical pixels and too small in physical ones. So the whole shell renders at a base
    /// scale, and the status bar's "100%" refers to it rather than to raw 1.0.
    ///
    /// A base scale, not a bigger token set: the mockups are the locked 1:1 reference, so growing the
    /// font and padding tokens by a fifth would be design drift spread across hundreds of literals and
    /// would have to be undone to compare against a mockup ever again. One factor keeps every mockup
    /// proportion exact and is a single number to revisit.</summary>
    public const double BaseScale = 1.2;

    /// <summary>Applied to the entire shell — chrome included, since "everything is small" was about
    /// the titlebar and status bar too, not just the page.</summary>
    public double ChromeScale => BaseScale;

    /// <summary>The user's zoom, as a factor RELATIVE to the base scale — the chrome transform already
    /// applies BaseScale to this control's ancestor, so multiplying it in here would square it.</summary>
    public double ZoomScale => Zoom / 100.0;

    /// <summary>What the page is actually scaled by once both transforms compose. Nothing binds to it;
    /// it exists so the relationship is asserted somewhere rather than inferred from two XAML files.</summary>
    public double EffectivePageScale => ChromeScale * ZoomScale;
    [ObservableProperty] private string _statusMessage = "";

    /// <summary>The titlebar's Kitchen chip / crumbs / lock flag are Explorer-specific (the mockup's
    /// landing titlebar has none of them), so the shared shell chrome binds through this rather than
    /// exposing crumbs/lock state on ShellViewModel itself — null on any other page.</summary>
    public ExplorerViewModel? CurrentExplorer => CurrentPage as ExplorerViewModel;

    /// <summary>
    /// Whether a document — a CookBook in the Explorer, or a cooked Set in the browser — is open on
    /// top of Landing.
    ///
    /// <para>The shell had no such notion, and that is why neither could be closed: navigation only
    /// ever pushed, <c>ICookBookSession.Close</c> and <c>IKitchenSession.Close</c> had no callers at
    /// all, and opening a second CookBook meant restarting the application.</para>
    /// </summary>
    public bool HasOpenDocument => CurrentPage is ExplorerViewModel or SetBrowserViewModel;

    partial void OnCurrentPageChanged(ViewModelBase? value)
    {
        // The status bar is a last-message board, and a message belongs to the page that said it.
        // Carried across a navigation it becomes a claim about a screen that never made it: the Set
        // browser -- which has no lock at all -- greeted the user with the Explorer's "Editing
        // locked - unlock to make changes." Clear on the way in; whatever the new page has to say,
        // it says after this.
        StatusMessage = "";

        // ...and then let the arriving page say its own opening line, if it has one. The Explorer
        // does: its lock state, which the chip in the titlebar shows at the same time, and which the
        // two must never disagree about.
        (value as ExplorerViewModel)?.SayLockState();

        OnPropertyChanged(nameof(CurrentExplorer));
        OnPropertyChanged(nameof(HasOpenDocument));
        CloseDocumentCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Raised when the titlebar's minimise is pressed; the head owns the window.</summary>
    public event Action? MinimizeRequested;
    /// <summary>Raised when maximise is pressed or the titlebar is double-clicked.</summary>
    public event Action? ToggleMaximizeRequested;
    /// <summary>Raised when the window's close is pressed.</summary>
    public event Action? CloseRequested;

    /// <summary>The titlebar's workspace chip. Per explorer.html it is "fixed for every item below
    /// it; changes only when you close this Kitchen and open another" - so it lives on the shell
    /// rather than following the selection. Empty when no Kitchen is open, which is a normal state:
    /// a CookBook opened from anywhere on disk needs no workspace.</summary>
    public string? KitchenName => _kitchen?.Current?.Manifest.Name;
    /// <summary>Whether a Kitchen is open, which is what shows the titlebar chip.</summary>
    public bool HasKitchen => _kitchen?.Current is not null;

    /// <summary>Builds the shell.</summary>
    /// <param name="nav">The page stack.</param>
    /// <param name="dialogs">The dialog layer.</param>
    /// <param name="theme">Light/dark switching.</param>
    /// <param name="status">The status bar's guidance channel.</param>
    /// <param name="kitchen">The open workspace, if any.</param>
    /// <param name="session">The open CookBook, so closing a document can free it.</param>
    public ShellViewModel(INavigationService nav, IDialogService dialogs, IThemeService theme,
        IStatusService status, IKitchenSession? kitchen = null, ICookBookSession? session = null)
    {
        _nav = nav; _dialogs = dialogs; _theme = theme;
        _kitchen = kitchen;
        _session = session;
        if (_kitchen is not null)
            _kitchen.Changed += () =>
            {
                OnPropertyChanged(nameof(KitchenName));
                OnPropertyChanged(nameof(HasKitchen));
            };
        _nav.Changed += () => CurrentPage = _nav.Current;
        _dialogs.Changed += () => ActiveDialog = _dialogs.Active;
        // Two channels on purpose: Report is for buttons that genuinely do nothing yet, Say is for
        // real guidance. Routing guidance through Report told users a working feature was unbuilt.
        status.Said += m => StatusMessage = m;
    }

    [RelayCommand] private void ShowHelp() => _dialogs.ShowAsync<object>(new HelpViewModel(_dialogs));
    [RelayCommand] private void CloseDialog() => _dialogs.Close(null);
    [RelayCommand] private void ZoomIn() => Zoom = Math.Min(300, Zoom + 10);
    [RelayCommand] private void ZoomOut() => Zoom = Math.Max(50, Zoom - 10);
    [RelayCommand] private void ZoomReset() => Zoom = 100;
    [RelayCommand] private void Minimize() => MinimizeRequested?.Invoke();
    [RelayCommand] private void ToggleMaximize() => ToggleMaximizeRequested?.Invoke();
    [RelayCommand] private void Close() => CloseRequested?.Invoke();
    [RelayCommand] private void ToggleTheme() => _theme.Toggle();

    /// <summary>
    /// Closes the open document and returns to Landing, freeing everything it held.
    ///
    /// <para>Order matters and is the whole reason this lives on the shell rather than in a page.
    /// <c>Back()</c> pops the page and disposes it, which releases the detail views that reference
    /// the book's decoded images; only then is it safe to close the session, which disposes the
    /// images themselves. Closing the session first would leave a live Explorer holding disposed
    /// bitmaps.</para>
    ///
    /// <para>A Set browser holds no CookBook, so it only pops. The Kitchen is deliberately left
    /// open: it is a workspace that outlives any one CookBook — explorer.html calls it "fixed for
    /// every item below it" — and closing a book is not leaving the workspace.</para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasOpenDocument))]
    private void CloseDocument()
    {
        bool wasCookBook = CurrentPage is ExplorerViewModel;
        _nav.Back();
        if (wasCookBook) _session?.Close();
    }
}
