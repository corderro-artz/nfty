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

    /// <summary>The id derived from the name: lower-case, spaces to dashes.</summary>
    public string DerivedId => DeriveId(Name);

    /// <summary>The derived id as the Identifier chip prints it.</summary>
    public string DerivedIdText => IdChipText(DerivedId);

    /// <summary>Creates the New CookBook wizard.</summary>
    /// <param name="dialogs">The dialog layer.</param>
    public NewCookBookViewModel(IDialogService dialogs) : base(dialogs) { }

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(DerivedId));
        OnPropertyChanged(nameof(DerivedIdText));
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
