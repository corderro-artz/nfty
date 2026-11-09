using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

public partial class SettingsDialogViewModel
{
    private AssociationStatus? _registered;
    private IReadOnlyList<SettingsAssociationRow>? _associationTypes;
    private bool _selectingTypes;

    /// <summary>Whether this machine supports the SYSTEM page. Fixed for the platform.</summary>
    public bool ShowsSystemTab => _associations is not null;
    /// <summary>Every supported extension, once, in catalog order.</summary>
    public IReadOnlyList<SettingsAssociationRow> AssociationTypes => _associationTypes ??=
        FileTypeCatalog.All.Select(t => new SettingsAssociationRow(t, AssociationChanged)).ToArray();
    /// <summary>All, none or some selected. Setting it chooses all or clears all.</summary>
    public bool? AllTypesSelected
    {
        get
        {
            int count = AssociationTypes.Count(t => t.IsSelected);
            return count == 0 ? false : count == AssociationTypes.Count ? true : null;
        }
        set
        {
            if (value is not { } selected) return;
            _selectingTypes = true;
            try { foreach (var row in AssociationTypes) row.IsSelected = selected; }
            finally { _selectingTypes = false; }
            AssociationChanged();
        }
    }
    /// <summary>The number of file types included in the next action.</summary>
    public string SelectionText => $"{AssociationTypes.Count(t => t.IsSelected)} selected";
    /// <summary>The mode requested for selected types; unsupported app types use the viewer.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAssociationApp), nameof(IsAssociationViewer))]
    private AssociationTarget _registrationTarget = AssociationTarget.App;
    /// <summary>Whether App is selected.</summary>
    public bool IsAssociationApp => RegistrationTarget == AssociationTarget.App;
    /// <summary>Whether Viewer is selected.</summary>
    public bool IsAssociationViewer => RegistrationTarget == AssociationTarget.Viewer;
    /// <summary>The actual per-extension registration, rather than pending checkbox selections.</summary>
    public AssociationChoice Choice => _registered?.Current ?? AssociationChoice.None;
    /// <summary>The registration summary, distinct from effective default evidence.</summary>
    public string AssociationSummary
    {
        get
        {
            int registered = AssociationTypes.Count(t => t.IsRegistered);
            int defaults = AssociationTypes.Count(t => t.Registration.DefaultTarget is not null);
            int unknown = AssociationTypes.Count(t => !t.Registration.Default.IsKnown);
            return $"{registered} registered · {defaults} default" + (unknown == 0 ? "" : $" · {unknown} unknown");
        }
    }
    /// <summary>The full inspection explanation, shown on the summary tooltip.</summary>
    public string AssociationDetail => _registered?.Detail ?? "";
    /// <summary>The fixed-height explanation of app routing and Windows' protected choices.</summary>
    public string DefaultsNote =>
        "App opens editable archives; other types use Viewer. Registration adds Open with; Windows chooses defaults.";
    /// <summary>Selects the mode for the next explicit Register action.</summary>
    /// <param name="target">App or Viewer.</param>
    [RelayCommand]
    private void UseAssociationTarget(AssociationTarget target)
    {
        if (target is AssociationTarget.App or AssociationTarget.Viewer) RegistrationTarget = target;
    }

    private void AssociationChanged()
    {
        if (_selectingTypes) return;
        OnPropertyChanged(nameof(AllTypesSelected));
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(AssociationSummary));
        OnPropertyChanged(nameof(AssociationDetail));
        OnPropertyChanged(nameof(Choice));
        ApplyAssociationsCommand.NotifyCanExecuteChanged();
        RemoveAssociationsCommand.NotifyCanExecuteChanged();
    }

    private void ReadAssociations(bool adoptChoice)
    {
        if (_associations is null) return;
        try { _registered = _associations.Inspect(); }
        catch (Exception ex) when (AssociationText.IsRefusal(ex))
        {
            _registered = new(AssociationState.NeedsRepair, AssociationChoice.None, AssociationText.CannotRead);
        }
        _selectingTypes = true;
        try
        {
            foreach (var row in AssociationTypes)
            {
                var status = _registered.Types.FirstOrDefault(t => t.Type.Extension == row.Extension)
                    ?? new FileTypeRegistration(row.Type, _registered.State, _registered.Current.For(row.Type),
                        AssociationDefault.Unknown, null, _registered.Detail);
                row.Update(status);
                if (adoptChoice) row.IsSelected = status.State != AssociationState.NotRegistered;
            }
        }
        finally { _selectingTypes = false; }
        AssociationChanged();
    }

    private bool CanApplyAssociations() => _associations is not null && AssociationTypes.Any(t => t.IsSelected);
    [RelayCommand(CanExecute = nameof(CanApplyAssociations))]
    private async Task ApplyAssociationsAsync() => await ChangeAssociationsAsync(RegistrationTarget);

    private bool CanRemoveAssociations() => _associations is not null && AssociationTypes.Any(t => t.IsSelected
        && (t.Registration.State != AssociationState.NotRegistered || _registered?.State == AssociationState.NeedsRepair));
    [RelayCommand(CanExecute = nameof(CanRemoveAssociations))]
    private async Task RemoveAssociationsAsync() => await ChangeAssociationsAsync(AssociationTarget.Off);

    private async Task ChangeAssociationsAsync(AssociationTarget target)
    {
        if (_associations is null) return;
        var selected = AssociationTypes.Where(t => t.IsSelected).Select(t => t.Extension).ToArray();
        try { _associations.SetTargets(selected, target); }
        catch (Exception ex) when (AssociationText.IsRefusal(ex))
        {
            ReadAssociations(adoptChoice: false);
            await _dialogs.ShowErrorAsync(target == AssociationTarget.Off ? AssociationText.RemoveFailedTitle : AssociationText.ApplyFailedTitle,
                target == AssociationTarget.Off ? AssociationText.RemoveFailed : AssociationText.ApplyFailed);
            return;
        }
        ReadAssociations(adoptChoice: false);
    }

    /// <summary>Refreshes registration and effective defaults after returning from Windows Settings.</summary>
    [RelayCommand] private void RefreshAssociations() => ReadAssociations(adoptChoice: false);

    private bool CanOpenDefaultApps() => _associations is not null;
    [RelayCommand(CanExecute = nameof(CanOpenDefaultApps))]
    private async Task OpenDefaultAppsAsync()
    {
        try { _associations?.OpenDefaultApps(); }
        catch (Exception ex) when (AssociationText.IsRefusal(ex) || ex is InvalidOperationException)
        {
            await _dialogs.ShowErrorAsync(AssociationText.DefaultAppsFailedTitle, AssociationText.DefaultAppsFailed);
        }
    }
}
