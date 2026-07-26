using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Output;

namespace Nfty.App.ViewModels;

/// <summary>Runs the real generate+write pipeline for a borrowed <see cref="LoadedCookBook"/>.
/// The book is owned by the caller — never disposed here; only the <see cref="GeneratedSet"/>
/// this run produces is disposed once written.</summary>
public partial class CookDialogViewModel : ViewModelBase
{
    private readonly LoadedCookBook _book;
    private readonly IFilePickerService _picker;
    private readonly IFolderRevealer _revealer;
    private readonly IDialogService _dialogs;
    private CancellationTokenSource? _cts;
    private string? _outDir;

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(CookCommand))] private int _count = 50;
    [ObservableProperty] private string _seed = Guid.NewGuid().ToString("N")[..8];
    [ObservableProperty] private bool _pack;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(CookCommand))] [NotifyCanExecuteChangedFor(nameof(CancelCommand))] [NotifyCanExecuteChangedFor(nameof(CloseCommand))] [NotifyPropertyChangedFor(nameof(ShowForm))] private bool _isRunning;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _phaseText = "";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(RevealCommand))] [NotifyPropertyChangedFor(nameof(ShowForm))] private bool _isDone;
    [ObservableProperty] private string _resultText = "";

    public CookDialogViewModel(LoadedCookBook book, IFilePickerService picker, IFolderRevealer revealer, IDialogService dialogs)
    { _book = book; _picker = picker; _revealer = revealer; _dialogs = dialogs; }

    public bool ShowForm => !IsRunning && !IsDone;

    private bool CanCook() => !IsRunning && Count > 0;

    [RelayCommand(CanExecute = nameof(CanCook))]
    private async Task Cook()
    {
        var dir = await _picker.PickFolderAsync("Cook to folder");
        if (dir is null) return;

        _cts = new CancellationTokenSource();
        IsRunning = true; IsDone = false; Progress = 0;
        GeneratedSet? set = null;
        try
        {
            var opts = new GenerateOptions(Count, Seed);
            PhaseText = "Generating…";
            var genProgress = new Progress<GenerationProgress>(p => Progress = p.Fraction);
            set = await Generator.GenerateAsync(_book, opts, progress: genProgress, cancellationToken: _cts.Token);

            PhaseText = "Writing…"; Progress = 0;
            var writeProgress = new Progress<WriteProgress>(p => Progress = p.Fraction);
            await SetWriter.WriteAsync(set, dir, Pack, writeProgress, _cts.Token);

            _outDir = dir;
            ResultText = $"{set.Assets.Count} assets → {dir}";
            IsDone = true;
        }
        catch (OperationCanceledException) { PhaseText = "Cancelled"; }
        catch (Exception ex)
        {
            await _dialogs.ShowAsync<object>(new ErrorDialogViewModel(_dialogs, "Cook failed", ex.Message));
        }
        finally
        {
            set?.Dispose();
            IsRunning = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))] private void Cancel() => _cts?.Cancel();
    private bool CanCancel() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanReveal))] private void Reveal() { if (_outDir is not null) _revealer.Reveal(_outDir); }
    private bool CanReveal() => IsDone;

    [RelayCommand(CanExecute = nameof(CanClose))] private void Close() => _dialogs.Close(null);
    private bool CanClose() => !IsRunning;
}
