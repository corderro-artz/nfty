using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

/// <summary>Edits a recipe or ingredient name through the owning Explorer's validated save.</summary>
public partial class ItemSettingsDialogViewModel : ViewModelBase
{
    private readonly IDialogService _dialogs;
    private readonly Func<string, Task<BookSettingsOutcome>> _save;
    /// <summary>The kind of item being edited.</summary>
    public string Title { get; }
    [ObservableProperty] private string _name;
    [ObservableProperty] private string _problem = "";

    /// <summary>Creates the name form.</summary>
    /// <param name="dialogs">The owning dialog layer.</param>
    /// <param name="title">The form heading.</param>
    /// <param name="name">The current name.</param>
    /// <param name="save">Validates and persists the name against the live book.</param>
    public ItemSettingsDialogViewModel(IDialogService dialogs, string title, string name,
        Func<string, Task<BookSettingsOutcome>> save)
    { _dialogs = dialogs; Title = title; _name = name; _save = save; }

    [RelayCommand]
    private async Task Save()
    {
        if (string.IsNullOrWhiteSpace(Name)) { Problem = "Enter a name."; return; }
        var result = await _save(Name.Trim());
        if (result.Saved) _dialogs.Close(this, null);
        else Problem = string.Join(" ", result.Problems);
    }

    [RelayCommand] private void Cancel() => _dialogs.Close(this, null);
}
