using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

/// <summary>
/// The card the app shows while it is doing something the user would otherwise watch it freeze for.
/// </summary>
/// <remarks>
/// <para><b>Opening a Set used to run on the UI thread.</b> <c>SetReader.Read</c> unpacks a
/// <c>.set</c> into a temporary directory and reads a JSON file per asset, so a large collection
/// froze the window for seconds with nothing on screen saying why — the application looked hung,
/// which is the one thing a working program must never look like. The read has always had an async
/// twin; nothing was calling it.</para>
///
/// <para><b>One card for every slow job, rather than a bar per screen.</b> The Cook dialog keeps its
/// own inline progress because it is a multi-state card that a run is one state OF — the form, the
/// run, the result. Everything else is a job with no screen of its own: opening a Set, opening a
/// CookBook. Those get this, and they get the same shape each time, which is what makes waiting
/// legible rather than surprising.</para>
///
/// <para><b>Indeterminate is a real state and not a failure to measure.</b> Unpacking an archive
/// reports nothing until it is done — <c>ZipFile.ExtractToDirectory</c> has no progress and
/// <c>ZipArchive</c>'s own directory parsing has no async API at all. A bar that invented a
/// percentage there would be lying; a spinning one says "working, no idea how long", which is the
/// truth. Anything that CAN count reports a fraction instead.</para>
/// </remarks>
public partial class BusyViewModel : ViewModelBase
{
    private readonly IDialogService _dialogs;
    private readonly CancellationTokenSource? _cts;

    /// <summary>What is being done, as a heading.</summary>
    public string Title { get; }

    /// <summary>What it is doing right now.</summary>
    [ObservableProperty] private string _phaseText;

    /// <summary>How far it has got, 0..1. Ignored while <see cref="IsIndeterminate"/>.</summary>
    [ObservableProperty] private double _progress;

    /// <summary>Whether the bar spins rather than fills.</summary>
    [ObservableProperty] private bool _isIndeterminate = true;

    /// <summary>Whether this job can be stopped.</summary>
    public bool CanCancel => _cts is not null;

    /// <summary>Creates the card.</summary>
    /// <param name="dialogs">The dialog layer, for the cancel path to close through.</param>
    /// <param name="title">What is being done.</param>
    /// <param name="phase">The opening line.</param>
    /// <param name="cancellation">The run's token source, or null for a job that cannot be stopped.</param>
    public BusyViewModel(IDialogService dialogs, string title, string phase,
        CancellationTokenSource? cancellation = null)
    {
        _dialogs = dialogs;
        _cts = cancellation;
        Title = title;
        _phaseText = phase;
    }

    /// <summary>Stops the job.</summary>
    /// <remarks>
    /// It does NOT close the card. The job owns that: cancellation is a request, and a card that
    /// vanished on the click would tell the user it had stopped before it had. The phase line says
    /// so instead, and the work closes the card when it actually unwinds.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        PhaseText = "Stopping…";
        _cts?.Cancel();
    }

    /// <summary>
    /// Runs a job behind a busy card and closes the card whatever happens.
    /// </summary>
    /// <typeparam name="T">What the job produces.</typeparam>
    /// <param name="dialogs">The dialog layer.</param>
    /// <param name="title">What is being done.</param>
    /// <param name="phase">The opening line.</param>
    /// <param name="work">The job. It is handed the card so it can report, and a token.</param>
    /// <param name="cancellable">Whether to offer a Cancel button.</param>
    /// <returns>Whatever the job returned.</returns>
    /// <remarks>
    /// <b>The card is shown WITHOUT awaiting it.</b> <c>IDialogService.ShowAsync</c> completes when
    /// the dialog closes, so awaiting it here would deadlock against the job that is supposed to do
    /// the closing. The <c>finally</c> is what guarantees the card comes down — an exception thrown
    /// by the job must not leave the app behind a modal nothing can dismiss.
    /// </remarks>
    public static async Task<T> RunAsync<T>(IDialogService dialogs, string title, string phase,
        Func<BusyViewModel, CancellationToken, Task<T>> work, bool cancellable = false)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(work);

        using var cts = cancellable ? new CancellationTokenSource() : null;
        var busy = new BusyViewModel(dialogs, title, phase, cts);
        _ = dialogs.ShowAsync<object>(busy);
        try
        {
            return await work(busy, cts?.Token ?? CancellationToken.None);
        }
        finally
        {
            dialogs.Close(null);
        }
    }
}
