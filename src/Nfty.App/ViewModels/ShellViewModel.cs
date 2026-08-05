using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

public partial class ShellViewModel : ViewModelBase
{
    private readonly INavigationService _nav;
    private readonly IDialogService _dialogs;
    private readonly INotYetWired _notify;
    private readonly IThemeService _theme;
    private readonly IKitchenSession? _kitchen;

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

    partial void OnCurrentPageChanged(ViewModelBase? value) => OnPropertyChanged(nameof(CurrentExplorer));

    public event Action? MinimizeRequested;
    public event Action? ToggleMaximizeRequested;
    public event Action? CloseRequested;

    /// <summary>The titlebar's workspace chip. Per explorer.html it is "fixed for every item below
    /// it; changes only when you close this Kitchen and open another" - so it lives on the shell
    /// rather than following the selection. Empty when no Kitchen is open, which is a normal state:
    /// a CookBook opened from anywhere on disk needs no workspace.</summary>
    public string? KitchenName => _kitchen?.Current?.Manifest.Name;
    public bool HasKitchen => _kitchen?.Current is not null;

    public ShellViewModel(INavigationService nav, IDialogService dialogs, INotYetWired notify, IThemeService theme,
        IStatusService status, IKitchenSession? kitchen = null)
    {
        _nav = nav; _dialogs = dialogs; _notify = notify; _theme = theme;
        _kitchen = kitchen;
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
        _notify.Reported += a => StatusMessage = $"Not wired yet: {a}";
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
    // OpenKitchen was here, bound to nothing in any view - dead the moment the titlebar's Kitchen
    // chip became a static label. Kitchens are not modelled yet (LandingViewModel's "New Kitchen…"
    // is CanExecute=Never for the same reason); the command comes back when they are, wired to
    // something real rather than to the not-wired channel.
    [RelayCommand] private void ToggleTheme() => _theme.Toggle();
}
