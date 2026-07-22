using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

/// <summary>Phase-1 New CookBook wizard: collects the fields a CookBook manifest needs (name, symbol,
/// canvas size, description). Create is a stub — Phase 2 builds the manifest and writes the .cbk.</summary>
public partial class NewCookBookViewModel : WizardViewModelBase
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _symbol = "";
    [ObservableProperty] private int _width = 1000;
    [ObservableProperty] private int _height = 1000;
    [ObservableProperty] private bool _aspectLocked = true;
    [ObservableProperty] private string _description = "";

    private double _ratio = 1.0;
    private bool _syncing;

    public string DerivedId => string.Join('-',
        Name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    public NewCookBookViewModel(IDialogService dialogs, INotYetWired notify) : base(dialogs, notify) { }

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(DerivedId));

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

    [RelayCommand] private void Create() { Notify.Report("Create CookBook"); Dialogs.Close(null); }
}
