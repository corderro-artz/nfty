using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

/// <summary>Modal quick-reference legend: domain terms, kinds, rule/state glyphs, keyboard chords, colour
/// prefixes, and the "unique DNA" phrase. Static display; closes itself via <see cref="IDialogService"/>.</summary>
public partial class HelpViewModel : ViewModelBase
{
    private readonly IDialogService _dialogs;
    public HelpViewModel(IDialogService dialogs) => _dialogs = dialogs;
    [RelayCommand] private void Close() => _dialogs.Close(null);
}
