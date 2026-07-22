using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

public partial class CookBookDetailViewModel : ViewModelBase
{
    private readonly INotYetWired _notify;
    public CookBookDetailViewModel(INotYetWired notify) => _notify = notify;
    [RelayCommand] private void Cook() => _notify.Report("Cook");
}
