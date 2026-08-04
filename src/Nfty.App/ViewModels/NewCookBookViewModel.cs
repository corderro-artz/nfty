using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

/// <summary>New CookBook wizard: collects the fields a CookBook manifest needs (name, symbol, canvas
/// size, description) and, on Create, closes the dialog with itself so the caller can build the
/// manifest and write the .cbk.</summary>
public partial class NewCookBookViewModel : WizardViewModelBase
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _symbol = "";
    [ObservableProperty] private int _width = 1000;
    [ObservableProperty] private int _height = 1000;
    [ObservableProperty] private bool _aspectLocked = true;
    [ObservableProperty] private string _description = "";

    /// <summary>How many assets the collection intends to mint. Optional — 0 means "not decided",
    /// which is a real answer at this point and is stored as null rather than as a target of zero.
    /// Purely declarative: it never constrains a cook, which takes its count from the Cook dialog.</summary>
    [ObservableProperty] private int _targetSupply;

    /// <summary>Null when unset, so the manifest omits it and stays a valid schemaVersion-1 archive.</summary>
    public int? TargetSupplyOrNull => TargetSupply > 0 ? TargetSupply : null;

    private double _ratio = 1.0;
    private bool _syncing;

    public string DerivedId => string.Join('-',
        Name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    public NewCookBookViewModel(IDialogService dialogs, INotYetWired notify) : base(dialogs, notify) { }

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(DerivedId));
        CreateCommand.NotifyCanExecuteChanged();
    }

    partial void OnAspectLockedChanged(bool value)
    {
        if (value && _height > 0) _ratio = (double)_width / _height;   // W:H captured when locking
    }

    partial void OnWidthChanged(int value)
    {
        if (_syncing) return;
        if (AspectLocked && _ratio > 0)
        {
            _syncing = true;
            Height = Math.Max(1, (int)Math.Round(value / _ratio));
            _syncing = false;
        }
        else if (_height > 0) _ratio = (double)value / _height;        // unlocked: track the current ratio
    }

    partial void OnHeightChanged(int value)
    {
        if (_syncing) return;
        if (AspectLocked && _ratio > 0)
        {
            _syncing = true;
            Width = Math.Max(1, (int)Math.Round(value * _ratio));
            _syncing = false;
        }
        else if (value > 0) _ratio = (double)_width / value;
    }

    private bool CanCreate() => !string.IsNullOrWhiteSpace(DerivedId);

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private void Create() => Dialogs.Close(this);
}
