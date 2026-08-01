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

    [ObservableProperty] private ViewModelBase? _currentPage;
    [ObservableProperty] private ViewModelBase? _activeDialog;
    [ObservableProperty] private int _zoom = 100;
    [ObservableProperty] private string _statusMessage = "";

    public event Action? MinimizeRequested;
    public event Action? ToggleMaximizeRequested;
    public event Action? CloseRequested;

    public ShellViewModel(INavigationService nav, IDialogService dialogs, INotYetWired notify, IThemeService theme,
        IStatusService status)
    {
        _nav = nav; _dialogs = dialogs; _notify = notify; _theme = theme;
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
    [RelayCommand] private void OpenKitchen() => _notify.Report("New Kitchen");
    [RelayCommand] private void ToggleTheme() => _theme.Toggle();
}
