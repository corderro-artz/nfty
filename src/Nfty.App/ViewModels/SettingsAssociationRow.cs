using CommunityToolkit.Mvvm.ComponentModel;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

/// <summary>One independently selectable file extension and the evidence Windows returned for it.</summary>
public partial class SettingsAssociationRow : ViewModelBase
{
    private readonly Action _selectionChanged;
    private FileTypeRegistration _registration;

    /// <summary>Creates a catalog row without reading the machine.</summary>
    /// <param name="type">The supported type.</param>
    /// <param name="selectionChanged">Updates the card's selection summary.</param>
    public SettingsAssociationRow(AssociatedType type, Action selectionChanged)
    {
        Type = type;
        _selectionChanged = selectionChanged;
        _registration = new(type, AssociationState.NotRegistered, AssociationTarget.Off,
            AssociationDefault.Unknown, null, "Not registered.");
    }

    /// <summary>The catalog entry.</summary>
    public AssociatedType Type { get; }
    /// <summary>The extension, including its dot.</summary>
    public string Extension => Type.Extension;
    /// <summary>Whether the next registration action includes this row.</summary>
    [ObservableProperty] private bool _isSelected;
    partial void OnIsSelectedChanged(bool value) => _selectionChanged();

    /// <summary>The latest registration evidence.</summary>
    public FileTypeRegistration Registration => _registration;
    /// <summary>Whether this copy has a complete registration.</summary>
    public bool IsRegistered => _registration.State == AssociationState.Registered;
    /// <summary>Whether the registration is incomplete or names a different copy.</summary>
    public bool NeedsRepair => _registration.State == AssociationState.NeedsRepair;
    /// <summary>The short row status; registration is not presented as a Windows default.</summary>
    public string Status => NeedsRepair ? "needs repair"
        : !IsRegistered ? "not registered"
        : $"{(_registration.Target == AssociationTarget.App ? "app" : "viewer")} · "
            + (_registration.DefaultTarget == _registration.Target ? "default" : "registered");
    /// <summary>The complete registration/default explanation, available without changing geometry.</summary>
    public string Detail => $"{Type.FriendlyName}. {_registration.Detail} "
        + (!_registration.Default.IsKnown ? "Windows' effective default could not be checked."
            : _registration.DefaultTarget is { } mode
                ? $"Windows currently opens it in this copy's {(mode == AssociationTarget.App ? "app" : "viewer")}."
                : _registration.Default.ProgId is null ? "Windows reports no default program."
                : "Windows reports a default that does not identify this copy. Default apps lets you choose.")
        + (!Type.OpensInApp ? " This type uses the viewer, including when App is requested." : "");

    /// <summary>Replaces read-only evidence without changing the selection.</summary>
    /// <param name="registration">The result of inspecting this extension.</param>
    public void Update(FileTypeRegistration registration)
    {
        _registration = registration;
        OnPropertyChanged(nameof(Registration));
        OnPropertyChanged(nameof(IsRegistered));
        OnPropertyChanged(nameof(NeedsRepair));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Detail));
    }
}
