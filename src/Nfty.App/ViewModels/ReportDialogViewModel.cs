using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;
using Nfty.Core.Formats;
using Nfty.Core.Stats;

namespace Nfty.App.ViewModels;

/// <summary>
/// The CLI's <c>stats</c> and <c>inspect</c>, shown in the app.
///
/// Neither is a new view of the data — both render through <see cref="CollectionReport"/> and
/// <see cref="IdentityReport"/>, so the text here is byte-identical to what the commands print. That
/// is the point: an author comparing the two should not have to wonder whether the GUI rounded
/// something differently.
///
/// <b>stats</b> is largely visible already (mint distribution, rarity bars, the DNA space), so what
/// this adds is getting it OUT — copied into an issue or a spreadsheet. <b>inspect</b> adds
/// something genuinely absent: the GUI shows names everywhere and never the ids, and ids are what
/// the CLI's --recipe and --variant expect.
/// </summary>
public partial class ReportDialogViewModel : ViewModelBase
{
    private readonly IDialogService _dialogs;
    private readonly IClipboardService _clipboard;
    private readonly LoadedCookBook _book;

    [ObservableProperty] private bool _showingIdentity;
    [ObservableProperty] private string _copyLabel = "Copy";

    public ReportDialogViewModel(LoadedCookBook book, IDialogService dialogs, IClipboardService clipboard)
    {
        _book = book; _dialogs = dialogs; _clipboard = clipboard;
    }

    public string Title => ShowingIdentity ? "Identity — ids for the CLI" : "Stats — the odds these weights imply";

    /// <summary>Rendered on demand rather than cached: a book can be edited behind this dialog, and
    /// a stale report is worse than a slightly slower one.</summary>
    public string Text => ShowingIdentity
        ? IdentityReport.Render(_book)
        : CollectionReport.Render(_book);

    partial void OnShowingIdentityChanged(bool value)
    {
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(Title));
        CopyLabel = "Copy";   // the confirmation belonged to the other report
    }

    [RelayCommand] private void ShowStats() => ShowingIdentity = false;
    [RelayCommand] private void ShowIdentity() => ShowingIdentity = true;

    /// <summary>Copies the report. The label confirms it, because a clipboard write is otherwise
    /// completely silent and the user cannot tell it happened.</summary>
    [RelayCommand]
    private async Task Copy()
    {
        await _clipboard.SetTextAsync(Text);
        CopyLabel = "Copied";
    }

    [RelayCommand] private void Close() => _dialogs.Close(null);
}
