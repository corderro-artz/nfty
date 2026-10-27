using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;
using Nfty.Core.Formats;
using Nfty.Core.Publish;
using Nfty.Ticket;

namespace Nfty.App.ViewModels;

/// <summary>
/// Writes the open CookBook as a <c>.mnu</c> Menu — the CLI's <c>menu</c> command, in the app.
/// </summary>
/// <remarks>
/// <para><b>It calls <see cref="MenuWriter"/> and nothing else decides anything.</b> Validation, the
/// atlas ceiling, the packing and the file layout are all the writer's; this card gathers a path and
/// a mode, runs it, and says what happened. In particular the atlas-too-large refusal is
/// <see cref="MenuWriter.CheckAtlasFits"/>'s own message, shown verbatim, rather than a second copy of
/// the ceiling that could drift from it.</para>
///
/// <para><b>The book is validated up front, here, and that is not a second rule.</b> It is the same
/// <see cref="Validator"/> the writer runs, asked early so the card can say WHY Export is off instead
/// of letting the press fail. What the writer still refuses after that is the atlas ceiling, which it
/// throws as <see cref="AtlasTooLargeException"/>: the card shows its <c>Problem</c> and says "Choose
/// Loose" in its own words, never the command-line flag that the exception's full message carries.</para>
///
/// <para><b>The file is replaced atomically</b> (<see cref="AtomicFile"/>): a failed or canceled export
/// leaves the Menu that was there, not half of a new one. This card reads the book and never edits
/// it, so it is available on a locked book.</para>
/// </remarks>
public partial class MenuExportDialogViewModel : ViewModelBase
{
    private readonly LoadedCookBook _book;
    private readonly IFilePickerService _picker;
    private readonly IDialogService _dialogs;
    private readonly IStatusService _status;
    private readonly string _unavailable;
    private CancellationTokenSource? _cts;

    /// <summary>The atlas wording, one muted line.</summary>
    public const string AtlasNote = "One PNG holds every variant; smallest to ship.";

    /// <summary>The loose wording, one muted line.</summary>
    public const string LooseNote = "One PNG per variant; easiest to swap art later.";

    /// <summary>Atlas (true) or loose (false).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoose))]
    [NotifyPropertyChangedFor(nameof(ModeNote))]
    [NotifyPropertyChangedFor(nameof(Mode))]
    private bool _isAtlas = true;

    /// <summary>Where the Menu is written.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    [NotifyPropertyChangedFor(nameof(PathLeaf))]
    [NotifyPropertyChangedFor(nameof(PathParent))]
    [NotifyPropertyChangedFor(nameof(Message))]
    private string _path;

    /// <summary>Progress 0..1 of the run.</summary>
    [ObservableProperty] private double _progress;

    /// <summary>The writer's refusal or failure, shown in the card's reserved line.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Message))] [NotifyPropertyChangedFor(nameof(MessageIsProblem))] private string _failure = "";

    /// <summary>What the run is doing, while it runs.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Message))] private string _phase = "";

    /// <summary>Whether a write is in flight.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
    [NotifyPropertyChangedFor(nameof(Message))]
    private bool _isRunning;

    /// <summary>Creates the card.</summary>
    /// <param name="book">The book to write. Borrowed; never disposed here.</param>
    /// <param name="bookPath">The <c>.cbk</c> it was opened from, or null for a book with no file.</param>
    /// <param name="picker">Chooses a different destination.</param>
    /// <param name="dialogs">The dialog layer to close through.</param>
    /// <param name="status">Where the success sentence goes.</param>
    public MenuExportDialogViewModel(LoadedCookBook book, string? bookPath, IFilePickerService picker,
        IDialogService dialogs, IStatusService status)
    {
        _book = book; _picker = picker; _dialogs = dialogs; _status = status;
        _path = string.IsNullOrEmpty(bookPath) ? "" : System.IO.Path.ChangeExtension(bookPath, Archives.MenuExtension);

        var problems = Validator.Validate(book);
        _unavailable = problems.Count == 0
            ? ""
            : $"This book has {problems.Count} problem{(problems.Count == 1 ? "" : "s")}, so a Menu cannot be "
              + "written yet. Open the validation report from the status chip. First: " + problems[0];
    }

    /// <summary>The write itself: <see cref="MenuWriter.WriteAsync"/>. A seam only so a test can hold a
    /// run open (a real write of a small book is over before a test can press Cancel) and so prove
    /// that a canceled or failed run leaves the file that was already there.</summary>
    internal Func<LoadedCookBook, string, MenuWriteOptions, IProgress<MenuWriteProgress>, CancellationToken, Task> Writer
    { get; set; } = MenuWriter.WriteAsync;

    /// <summary>The art mode the tray has chosen.</summary>
    public MenuArtMode Mode => IsAtlas ? MenuArtMode.Atlas : MenuArtMode.Loose;

    /// <summary>The complement of <see cref="IsAtlas"/>, for markup that cannot negate.</summary>
    public bool IsLoose => !IsAtlas;

    /// <summary>The muted line under the tray for the mode chosen.</summary>
    public string ModeNote => IsAtlas ? AtlasNote : LooseNote;

    /// <summary>Why Export is off for the book itself, or empty when the book can be written.</summary>
    public string Unavailable => _unavailable;

    /// <summary>The destination's own file name — the part a reader looks for.</summary>
    public string PathLeaf => string.IsNullOrEmpty(Path) ? "Choose where to write the Menu" : System.IO.Path.GetFileName(Path);

    /// <summary>Where it sits; trimmed in the view, since a path is read from its end.</summary>
    public string PathParent => string.IsNullOrEmpty(Path) ? "" : System.IO.Path.GetDirectoryName(Path) ?? "";

    /// <summary>The one reserved line: a failure, else the book's reason, else the running phase.</summary>
    public string Message =>
        Failure.Length > 0 ? Failure
        : _unavailable.Length > 0 ? _unavailable
        : IsRunning ? Phase
        : string.IsNullOrEmpty(Path) ? "Pick a destination to enable Export."
        : "";

    /// <summary>Whether <see cref="Message"/> is a problem (warning ink) rather than information.</summary>
    public bool MessageIsProblem => Failure.Length > 0 || _unavailable.Length > 0;

    /// <summary>Tray: atlas.</summary>
    [RelayCommand] private void ChooseAtlas() { IsAtlas = true; Failure = ""; }

    /// <summary>Tray: loose.</summary>
    [RelayCommand] private void ChooseLoose() { IsAtlas = false; Failure = ""; }

    /// <summary>Opens the save picker.</summary>
    [RelayCommand(CanExecute = nameof(CanBrowse))]
    private async Task Browse()
    {
        string? chosen = await _picker.SaveFileAsync("Export a Menu", Archives.MenuExtension);
        if (!string.IsNullOrEmpty(chosen)) Path = chosen;
    }
    private bool CanBrowse() => !IsRunning;

    private bool CanExport() => !IsRunning && _unavailable.Length == 0 && !string.IsNullOrWhiteSpace(Path);

    /// <summary>Writes the Menu.</summary>
    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task Export()
    {
        // A disabled button is a label: the gate is checked here too.
        if (!CanExport()) return;
        _cts = new CancellationTokenSource();
        IsRunning = true; Progress = 0; Failure = ""; Phase = "Reading art…";
        string path = Path;
        try
        {
            var progress = new Progress<MenuWriteProgress>(OnProgress);
            var options = new MenuWriteOptions { Art = Mode };
            await AtomicFile.ReplaceAsync(path,
                tmp => Writer(_book, tmp, options, progress, _cts.Token));
            Progress = 1;
            _status.Say($"Wrote {System.IO.Path.GetFileName(path)} ({SizeText.Of(new FileInfo(path).Length)}).");
            _dialogs.Close(path);
        }
        catch (OperationCanceledException) { Phase = "Canceled"; }
        // The writer's own typed refusal: its Problem is what is wrong, without the command-line flag
        // that does not exist on this screen; the tray is right there, so the instruction is ours.
        catch (AtlasTooLargeException ex) { Failure = ex.Problem + ". Choose Loose above."; }
        catch (Exception ex)
        {
            await _dialogs.ShowAsync<object>(new ErrorDialogViewModel(_dialogs, "Menu export failed", ex.Message));
        }
        finally
        {
            IsRunning = false;
            // A bar left full beside a refusal reads as a finished run. Success closes the card, so
            // nothing legitimate is left on screen to protect.
            if (Failure.Length > 0 || Phase == "Canceled") Progress = 0;
        }
    }

    /// <summary>Takes one report from the writer. <see cref="Progress{T}"/> posts, so a report can land
    /// after the run has ended and put a full bar back beside a refusal; a report that arrives when
    /// nothing is running is dropped.</summary>
    /// <param name="p">What the writer reported.</param>
    internal void OnProgress(MenuWriteProgress p)
    {
        if (!IsRunning) return;
        Phase = p.Phase;
        Progress = p.Total <= 0 ? 0 : (double)p.Written / p.Total;
    }

    /// <summary>Stops the write between cells.</summary>
    [RelayCommand(CanExecute = nameof(CanCancel))] private void Cancel() => _cts?.Cancel();
    private bool CanCancel() => IsRunning;

    /// <summary>Closes the card.</summary>
    [RelayCommand(CanExecute = nameof(CanClose))] private void Close() => _dialogs.Close(null);
    private bool CanClose() => !IsRunning;
}
