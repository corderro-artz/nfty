using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;
using Nfty.Core;

namespace Nfty.App.ViewModels;

/// <summary>
/// The Settings card: one row choosing how many worker threads cooking, exporting and opening may
/// use. Auto uses two thirds of the machine; Custom is a count from 1 to the logical processors.
/// </summary>
/// <remarks>
/// The stepper is always laid out and only dimmed in Auto (reserve space, toggle ink). A
/// <c>NFTY_THREADS</c> override locks both controls and the line below says so.
/// </remarks>
public partial class SettingsDialogViewModel : ViewModelBase
{
    private readonly IDialogService _dialogs;
    private readonly IParallelismSetting _setting;
    private bool _syncing;

    /// <summary>Creates the card over the setting.</summary>
    /// <param name="dialogs">The dialog layer to close through.</param>
    /// <param name="setting">What the card edits.</param>
    public SettingsDialogViewModel(IDialogService dialogs, IParallelismSetting setting)
    {
        _dialogs = dialogs;
        _setting = setting;
        _threads = setting.Current.Effective;
    }

    /// <summary>The machine's logical processor count: the stepper's ceiling.</summary>
    public double Maximum => Parallelism.LogicalProcessors;

    /// <summary>True when <c>NFTY_THREADS</c> decides this session.</summary>
    public bool IsEnvLocked => _setting.EnvironmentText is not null;

    /// <summary>True when the choice is Auto.</summary>
    public bool IsAuto => !IsEnvLocked && _setting.Saved is null;

    /// <summary>True when the choice is a count (also while the environment fixes one).</summary>
    public bool IsCustom => !IsAuto;

    /// <summary>The tray is live unless the environment decides.</summary>
    public bool TrayEnabled => !IsEnvLocked;

    /// <summary>The stepper is live in Custom, and only there.</summary>
    public bool StepperEnabled => !IsEnvLocked && IsCustom;

    /// <summary>The count shown in the stepper.</summary>
    [ObservableProperty] private double _threads;

    /// <summary>The muted line under the row: what the current choice means.</summary>
    public string Note
    {
        get
        {
            var lp = Parallelism.LogicalProcessors;
            if (_setting.EnvironmentText is { } env)
                return $"Set by NFTY_THREADS={env} for this session.";
            if (IsAuto)
                return $"Auto uses {Parallelism.DefaultDegree} of {lp} logical processors.";
            if (_setting.Saved is { } saved && saved > lp)
                return $"Saved as {saved}; this machine has {lp} logical processors, so {lp} is used.";
            return "Results are identical at any thread count; fewer threads leave more of the machine free.";
        }
    }

    partial void OnThreadsChanged(double value)
    {
        if (_syncing || !StepperEnabled) return;
        var rounded = (int)Math.Round(value, MidpointRounding.AwayFromZero);
        _setting.Set(Math.Clamp(rounded, 1, Math.Max(1, Parallelism.LogicalProcessors)));
        Refresh();
    }

    [RelayCommand]
    private void UseAuto()
    {
        if (IsEnvLocked) return;
        _setting.Set(null);
        Refresh();
    }

    [RelayCommand]
    private void UseCustom()
    {
        if (IsEnvLocked || IsCustom) return;
        _setting.Set(Parallelism.DefaultDegree);
        Refresh();
    }

    [RelayCommand] private void Close() => _dialogs.Close(null);

    private void Refresh()
    {
        _syncing = true;
        try { Threads = _setting.Current.Effective; }
        finally { _syncing = false; }
        OnPropertyChanged(nameof(IsAuto));
        OnPropertyChanged(nameof(IsCustom));
        OnPropertyChanged(nameof(StepperEnabled));
        OnPropertyChanged(nameof(Note));
    }
}
