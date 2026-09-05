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

    /// <summary>Accept every roll instead of requiring each asset to have distinct DNA.</summary>
    /// <remarks>
    /// The engine has always supported this and the CLI has always exposed it as <c>--unlimited</c>;
    /// this dialog built <c>new GenerateOptions(Count, Seed)</c> and took the default, so a person
    /// using only the app could not mint a collection larger than its unique space. They met
    /// "allows exactly 33 unique DNA, but 500 were requested" and had no way past it except asking
    /// for fewer.
    ///
    /// <para>That is a real way to mint: identity is the token id, as ERC-721 defines it, and the
    /// weights still decide how common each variant is — a rare variant is rare, and a layer with a
    /// low appearance chance is rarer still. Duplicates are the point, not a defect.</para>
    /// </remarks>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(UniqueHint))] private bool _unlimited;

    /// <summary>What the switch means, in the words a person minting would use.</summary>
    public string UniqueHint => Unlimited
        ? "Every roll is kept, so assets can repeat — weights still decide how rare each one is."
        : "Every asset is different. The run stops if the cookbook cannot fill the count.";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(CookCommand))] [NotifyCanExecuteChangedFor(nameof(CancelCommand))] [NotifyCanExecuteChangedFor(nameof(CloseCommand))] [NotifyPropertyChangedFor(nameof(ShowForm))] [NotifyPropertyChangedFor(nameof(FootHint))] private bool _isRunning;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _phaseText = "";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(RevealCommand))] [NotifyPropertyChangedFor(nameof(ShowForm))] [NotifyPropertyChangedFor(nameof(FootHint))] private bool _isDone;

    /// <summary>What the run produced, as a sentence — the counts alone.</summary>
    /// <remarks>
    /// Split from <see cref="OutputPath"/> deliberately. The two used to be one string
    /// ("20 assets → C:\long\path"), which wrapped mid-path across three lines of prose and
    /// could not be clicked: a path is a THING the user wants to go to, not a clause in a sentence
    /// about it. Separated, the sentence can wrap and the path gets a control of its own.
    /// </remarks>
    [ObservableProperty] private string _resultText = "";

    /// <summary>The folder the Set was written to, shown as its own control and openable.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(OutputLeaf))] [NotifyPropertyChangedFor(nameof(OutputParent))] private string _outputPath = "";

    /// <summary>The folder's own name — the part a reader is actually looking for.</summary>
    /// <remarks>
    /// A path is read from the END: which folder, then where it sits. Trimming a long path with a
    /// trailing ellipsis keeps the useless half (the drive and the user's home) and eats the half
    /// that identifies it. So the leaf is stated in full and the parent is what gets trimmed.
    /// </remarks>
    public string OutputLeaf => string.IsNullOrEmpty(OutputPath)
        ? ""
        : Path.GetFileName(OutputPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
          is { Length: > 0 } leaf ? leaf : OutputPath;

    /// <summary>Where that folder sits. Trimmed when it is long; the tooltip carries all of it.</summary>
    public string OutputParent => string.IsNullOrEmpty(OutputPath)
        ? ""
        : Path.GetDirectoryName(OutputPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? "";

    /// <summary>True once the chosen folder turns out to already hold a Set, so this cook is adding
    /// to it rather than starting one. Set during the run, because it depends on the folder the user
    /// picks - which is also why the dialog cannot say so up front.</summary>
    [ObservableProperty] private bool _isExtending;

    /// <summary>Creates the cook dialog.</summary>
    /// <param name="book">The book to generate from.</param>
    /// <param name="picker">Chooses the output folder.</param>
    /// <param name="revealer">Opens the output folder when the run finishes.</param>
    /// <param name="dialogs">The dialog layer to close through.</param>
    public CookDialogViewModel(LoadedCookBook book, IFilePickerService picker, IFolderRevealer revealer, IDialogService dialogs)
    { _book = book; _picker = picker; _revealer = revealer; _dialogs = dialogs; }

    /// <summary>Whether the options form is showing rather than the progress view.</summary>
    public bool ShowForm => !IsRunning && !IsDone;

    /// <summary>The footer's left-hand hint, which is a different sentence in each of the three
    /// states this one card passes through.</summary>
    public string FootHint => IsRunning
        ? "Cancel stops after the current asset"
        : IsDone ? "Click the folder to open it" : "Same book, same seed, same collection";

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
            // EXTEND, when the folder already holds a Set. This is the `extend` command's whole
            // mechanism - CLAUDE.md: "not a second pipeline, the same Generator.Generate call with
            // its existingDnas / startNumber supplied" - and until now the GUI never supplied them.
            //
            // That was not merely a missing feature. SetWriter names files by asset.SetNumber, so
            // generating from 1 into a folder that already had 0001.png would have OVERWRITTEN the
            // existing assets, and without the existing DNAs the new ones could duplicate them. The
            // writer was already extend-aware (it loads the existing items and regrades rarity
            // across the whole collection); only the generator was being told nothing.
            PhaseText = "Reading existing…";
            var existing = await SetWriter.ReadExistingAsync(dir, _cts.Token);
            IsExtending = existing.Dnas.Count > 0;

            var opts = new GenerateOptions(Count, Seed, EnforceUniqueDna: !Unlimited);
            PhaseText = IsExtending ? $"Extending {existing.Dnas.Count} assets…" : "Generating…";
            var genProgress = new Progress<GenerationProgress>(p => Progress = p.Fraction);
            set = await Generator.GenerateAsync(_book, opts, existing.Dnas, existing.NextNumber,
                genProgress, _cts.Token);

            PhaseText = "Writing…"; Progress = 0;
            var writeProgress = new Progress<WriteProgress>(p => Progress = p.Fraction);
            await SetWriter.WriteAsync(set, dir, Pack, writeProgress, _cts.Token);

            _outDir = dir;
            OutputPath = dir;
            int made = set.Assets.Count;
            string noun = made == 1 ? "asset" : "assets";
            ResultText = IsExtending
                ? $"Added +{made} {noun} — {existing.Dnas.Count + made} total in the collection."
                : $"Cooked {made} {noun}.";
            IsDone = true;
        }
        catch (OperationCanceledException) { PhaseText = "Canceled"; }
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
