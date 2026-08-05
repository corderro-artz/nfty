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
    [ObservableProperty] private int _zoom = 100;
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
