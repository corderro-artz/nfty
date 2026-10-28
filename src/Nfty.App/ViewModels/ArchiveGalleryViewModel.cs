using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;
using Nfty.App.Viewer;

namespace Nfty.App.ViewModels;

/// <summary>
/// The tile grid the viewer shows for an archive: a CookBook's, Recipe's or Ingredient's variants, a
/// Set's assets, a Menu's art cells, or a Kitchen's archives. Built by <see cref="GalleryBuilder"/>.
/// </summary>
/// <remarks>
/// <para><b>Listing reads manifests, not pictures.</b> A tile decodes its own image only when a view
/// realizes it, through the one <see cref="ThumbnailGate"/>; opening a tile builds a frozen subject
/// for it and hands that to the host. Nothing here edits anything.</para>
/// <para>The gallery owns what it was built over (a Set's temp copy, a Menu's open archive) and frees
/// it on <see cref="Dispose"/>; a tile's subject is a snapshot and outlives it.</para>
/// </remarks>
public sealed partial class ArchiveGalleryViewModel : ViewModelBase, IDisposable
{
    private readonly IReadOnlyList<IDisposable> _owned;

    internal ArchiveGalleryViewModel(string title, IReadOnlyList<ViewerFact> facts,
        IReadOnlyList<GalleryItem> items, ThumbnailGate gate, IReadOnlyList<IDisposable>? owned = null,
        bool allowExport = true)
    {
        Title = title;
        Facts = facts;
        Items = items;
        Gate = gate;
        AllowExport = allowExport;
        _owned = owned ?? [];
    }

    /// <summary>The window title.</summary>
    public string Title { get; }

    /// <summary>What the info rail says about the archive.</summary>
    public IReadOnlyList<ViewerFact> Facts { get; }

    /// <summary>One tile per variant, asset, cell or archive, in the archive's own order.</summary>
    public IReadOnlyList<GalleryItem> Items { get; }

    /// <summary>The one limit on how many tiles decode at once.</summary>
    internal ThumbnailGate Gate { get; }

    /// <summary>False when the archive was sealed view-only, so every tile's viewer refuses export.</summary>
    public bool AllowExport { get; }

    /// <summary>Whether the archive has no tiles to show.</summary>
    public bool IsEmpty => Items.Count == 0;

    /// <summary>Where an opened tile's viewer goes; null leaves tiles inert.</summary>
    public IViewerHost? Host { get; set; }

    /// <summary>Where a tile that cannot be opened says why.</summary>
    public IDialogService? Dialogs { get; set; }

    /// <summary>Opens a tile in its own viewer.</summary>
    /// <param name="item">The tile.</param>
    /// <returns>Completes when the viewer was handed over, or the failure was shown.</returns>
    [RelayCommand]
    public async Task OpenTile(GalleryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (Host is null) return;
        ViewerSubject subject;
        try { subject = await item.Open(); }
        catch (OperationCanceledException) { return; }   // the opener already said why
        catch (Exception ex)
        {
            if (Dialogs is not null) await Dialogs.ShowErrorAsync("Could not open", ex.Message);
            return;
        }
        Host.Open(subject);
    }

    /// <summary>Frees the tiles and whatever the gallery was built over.</summary>
    public void Dispose()
    {
        foreach (var i in Items) i.Dispose();
        foreach (var o in _owned) o.Dispose();
    }
}
