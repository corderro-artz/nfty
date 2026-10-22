using System.Globalization;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;
using Nfty.Core.Editing;

namespace Nfty.App.ViewModels;

/// <summary>
/// One variant (or cell) that making a source voxel-ready would change: its name, how many pixels
/// move which way, and the picture before and after.
/// </summary>
/// <remarks>The two pictures are made the first time a row is read, not when the dialog opens: a book
/// can have hundreds of partial variants and the list virtualizes, so only the rows on screen pay for
/// a snap and a bitmap.</remarks>
public sealed class VoxelReadyRow : IDisposable
{
    private readonly Func<VoxelReadyChange, (Bitmap Before, Bitmap After)> _thumbs;
    private (Bitmap Before, Bitmap After)? _made;
    private bool _disposed;

    /// <summary>Creates the row.</summary>
    /// <param name="change">What changes.</param>
    /// <param name="thumbs">Makes the before and after pictures; the row owns what it returns.</param>
    public VoxelReadyRow(VoxelReadyChange change, Func<VoxelReadyChange, (Bitmap Before, Bitmap After)> thumbs)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(thumbs);
        Change = change;
        _thumbs = thumbs;
    }

    /// <summary>What changes.</summary>
    public VoxelReadyChange Change { get; }

    /// <summary>"Layer / Variant".</summary>
    public string Name => $"{Change.IngredientName} / {Change.VariantName}";

    /// <summary>"12 px solid · 3 px clear".</summary>
    public string Counts => string.Create(CultureInfo.InvariantCulture,
        $"{Change.ToSolid:N0} px solid · {Change.ToClear:N0} px clear");

    /// <summary>The whole row as one line: "Layer / Variant · 12 px solid · 3 px clear".</summary>
    public string Text => $"{Name} · {Counts}";

    /// <summary>The variant as it is now.</summary>
    public Bitmap? Before => Made()?.Before;

    /// <summary>The variant as it would be written.</summary>
    public Bitmap? After => Made()?.After;

    private (Bitmap Before, Bitmap After)? Made()
    {
        if (_disposed) return null;
        return _made ??= _thumbs(Change);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
        if (_made is { } m) { m.Before.Dispose(); m.After.Dispose(); }
        _made = null;
    }
}

/// <summary>
/// The preview card of Make voxel-ready: every variant that would change, with how many of its
/// partial pixels turn solid and how many turn clear, before anything is written.
/// </summary>
/// <remarks>
/// <para>Which variants are listed is the BOOK's business, not the card's, so the list scrolls inside a
/// capped card with the heading and the buttons outside it — the validation card's arrangement and for
/// the same reason. Closes with <c>true</c> for Apply and <c>false</c> for Cancel.</para>
/// </remarks>
public sealed partial class VoxelReadyDialogViewModel : ViewModelBase, IDisposable
{
    private readonly IDialogService _dialogs;

    /// <summary>Creates the card.</summary>
    /// <param name="dialogs">The dialog layer to close through.</param>
    /// <param name="changes">What would change, in the order to list it.</param>
    /// <param name="thumbs">Makes the before and after pictures of one change.</param>
    public VoxelReadyDialogViewModel(IDialogService dialogs, IReadOnlyList<VoxelReadyChange> changes,
        Func<VoxelReadyChange, (Bitmap Before, Bitmap After)> thumbs)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(thumbs);
        _dialogs = dialogs;
        Rows = changes.Select(c => new VoxelReadyRow(c, thumbs)).ToList();
        long solid = changes.Sum(c => (long)c.ToSolid);
        long clear = changes.Sum(c => (long)c.ToClear);
        int n = changes.Count;
        Summary = string.Create(CultureInfo.InvariantCulture,
            $"{n:N0} {(n == 1 ? "variant" : "variants")} · {solid:N0} px turn solid · {clear:N0} px turn clear");
    }

    /// <summary>The rows, one per variant that would change.</summary>
    public IReadOnlyList<VoxelReadyRow> Rows { get; }

    /// <summary>"9 variants · 120 px turn solid · 31 px turn clear".</summary>
    public string Summary { get; }

    /// <summary>The rule, in one sentence.</summary>
    public string Rule =>
        "Every pixel that is partly transparent snaps: at 50% alpha or more it becomes solid, below that it becomes clear. "
        + "Depth is untouched; a pixel that turns solid rises at paper. The Rise page offers Undo until you leave it.";

    /// <summary>Writes the snap.</summary>
    [RelayCommand] private void Apply() => _dialogs.Close(true);

    /// <summary>Writes nothing.</summary>
    [RelayCommand] private void Cancel() => _dialogs.Close(false);

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var r in Rows) r.Dispose();
    }
}
