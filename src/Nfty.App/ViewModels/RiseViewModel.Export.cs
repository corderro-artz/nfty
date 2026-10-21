using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Rise;
using Nfty.Model;

namespace Nfty.App.ViewModels;

/// <summary>One of the three format cards on the EXPORT tab.</summary>
public sealed partial class FormatCard : ObservableObject
{
    /// <summary>Creates a card.</summary>
    /// <param name="format">The format it picks.</param>
    /// <param name="title">Bold line.</param>
    /// <param name="summary">One sentence under it.</param>
    public FormatCard(ModelFormat format, string title, string summary)
    {
        Format = format;
        Title = title;
        Summary = summary;
    }

    /// <summary>The format this card picks.</summary>
    public ModelFormat Format { get; }

    /// <summary>Bold line.</summary>
    public string Title { get; }

    /// <summary>One sentence under the title.</summary>
    public string Summary { get; }

    /// <summary>Whether this card is the chosen format.</summary>
    [ObservableProperty] private bool _isSelected;
}

public sealed partial class RiseViewModel
{
    private const double VoxelSizeMin = 0.01;
    private const double VoxelSizeMax = 1000;

    private bool _bulkChecking;
    private string _manifestSize = "";

    /// <summary>How many times the exact size was computed; a test proves it is not computed while the
    /// DEPTH tab is showing.</summary>
    internal int SizeBuilds { get; private set; }

    private void InitExport()
    {
        FormatCards = new[]
        {
            new FormatCard(ModelFormat.Glb, "GLB", "One binary file. The default; what engines and viewers expect."),
            new FormatCard(ModelFormat.GltfWithBin, "glTF + bin", "Readable JSON beside a binary buffer. Two files per model."),
            new FormatCard(ModelFormat.GltfEmbedded, "glTF embedded", "One text file with the buffer inside. Largest; easiest to paste."),
        };
        foreach (var card in FormatCards) card.IsSelected = card.Format == Format;
        // The item the page opened on starts ticked, so the EXPORT tab opens naming one model. Only the
        // opening one: an item reached later by selection is not ticked behind the user's back.
        if (Source.CanBatch && SelectedItem.IsRisableOrUnknown) SelectedItem.IsChecked = true;
        foreach (var item in Items) item.PropertyChanged += OnItemExportChanged;
    }

    private void DisposeExport()
    {
        foreach (var item in Items) item.PropertyChanged -= OnItemExportChanged;
    }

    private void OnItemExportChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_bulkChecking || _disposed) return;
        if (e.PropertyName is nameof(RiseItem.IsChecked) or nameof(RiseItem.IsRisableOrUnknown) or nameof(RiseItem.ExportBaseName))
            RefreshExport(size: false);
    }

    // ---------------------------------------------------------------- options

    /// <summary>The three format cards.</summary>
    public IReadOnlyList<FormatCard> FormatCards { get; private set; } = Array.Empty<FormatCard>();

    /// <summary>The file shape.</summary>
    [ObservableProperty] private ModelFormat _format = ModelFormat.Glb;

    /// <summary>Chooses a format.</summary>
    /// <param name="card">One of <see cref="FormatCards"/>.</param>
    [RelayCommand]
    private void PickFormat(FormatCard card)
    {
        if (card is null) return;
        Format = card.Format;
    }

    partial void OnFormatChanged(ModelFormat value)
    {
        foreach (var card in FormatCards) card.IsSelected = card.Format == value;
        RefreshExport(size: true);
    }

    /// <summary>Which point of the model lands on the origin.</summary>
    [ObservableProperty] private Pivot _pivot = Pivot.BottomCenter;

    /// <summary>Radio: the pivot is the bottom center.</summary>
    public bool IsPivotBottomCenter
    {
        get => Pivot == Pivot.BottomCenter;
        set { if (value) Pivot = Pivot.BottomCenter; }
    }

    /// <summary>Radio: the pivot is the center.</summary>
    public bool IsPivotCenter
    {
        get => Pivot == Pivot.Center;
        set { if (value) Pivot = Pivot.Center; }
    }

    /// <summary>Radio: the pivot is the minimum corner.</summary>
    public bool IsPivotMinCorner
    {
        get => Pivot == Pivot.MinCorner;
        set { if (value) Pivot = Pivot.MinCorner; }
    }

    partial void OnPivotChanged(Pivot value)
    {
        OnPropertyChanged(nameof(IsPivotBottomCenter));
        OnPropertyChanged(nameof(IsPivotCenter));
        OnPropertyChanged(nameof(IsPivotMinCorner));
        RefreshExport(size: true);
    }

    /// <summary>World units per pixel. The ceiling, the floor and the rounding belong to the property,
    /// so a typed value and a spinner cannot disagree about them.</summary>
    [ObservableProperty] private double _voxelSize = 1;

    partial void OnVoxelSizeChanged(double value)
    {
        double clean = double.IsFinite(value)
            ? Math.Round(Math.Clamp(value, VoxelSizeMin, VoxelSizeMax), 2, MidpointRounding.AwayFromZero)
            : 1;
        if (clean != value) { VoxelSize = clean; return; }
        OnPropertyChanged(nameof(ModelDimensionsText));
        RefreshExport(size: true);
    }

    partial void OnMeshChanged(Mesh? value) => OnPropertyChanged(nameof(ModelDimensionsText));

    /// <summary>"model 6 × 6 × 1": the model's extent in world units, mesh extent times voxel size.</summary>
    public string ModelDimensionsText
    {
        get
        {
            if (Mesh is not { } m) return "";
            var e = m.Max - m.Min;
            string F(float v) => ((double)v * VoxelSize).ToString("0.##", CultureInfo.InvariantCulture);
            return $"model {F(e.X)} × {F(e.Y)} × {F(e.Z)}";
        }
    }

    /// <summary>One zip (true) or a folder (false), for a batch.</summary>
    [ObservableProperty] private bool _packed = true;

    /// <summary>Radio: a folder of files.</summary>
    public bool IsFolderShape
    {
        get => !Packed;
        set { if (value) Packed = false; }
    }

    /// <summary>Radio: one zip.</summary>
    public bool IsPackedShape
    {
        get => Packed;
        set { if (value) Packed = true; }
    }

    partial void OnPackedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsFolderShape));
        OnPropertyChanged(nameof(IsPackedShape));
        RefreshExport(size: false);
    }

    /// <summary>What the options say, for the writer.</summary>
    public RiseOptions ExportOptions => new() { Format = Format, Pivot = Pivot, VoxelSize = (float)VoxelSize };

    // ---------------------------------------------------------------- what is exported

    /// <summary>Several models are exported: the Shape section is inked.</summary>
    public bool IsBatch => ExportItems.Count > 1;

    /// <summary>What an export would rise: the ticked items that can rise (or are not loaded yet, so
    /// not known to be unable) for a batch source, the selected item otherwise.</summary>
    public IReadOnlyList<RiseItem> ExportItems =>
        Source.CanBatch
            ? Items.Where(i => i.IsChecked && i.IsRisableOrUnknown).ToList()
            : SelectedItem.IsRisableOrUnknown
                ? new[] { SelectedItem }
                : Array.Empty<RiseItem>();

    /// <summary>How many models an export would write.</summary>
    public int ExportCount => ExportItems.Count;

    /// <summary>The tab's count badge.</summary>
    public string ExportBadge => ExportCount.ToString(CultureInfo.InvariantCulture);

    private int RisableCount => Items.Count(i => i.IsRisableOrUnknown);

    /// <summary>"2 of 3 checked for export"; empty for a source that exports only the selected item.</summary>
    public string CheckedSummary
    {
        get
        {
            if (!Source.CanBatch) return "";
            int n = Items.Count(i => i.IsChecked && i.IsRisableOrUnknown);
            return string.Create(CultureInfo.InvariantCulture, $"{n} of {RisableCount} checked for export");
        }
    }

    /// <summary>"Check all" when anything that can rise is unchecked, otherwise "Clear".</summary>
    public string CheckAllLabel => AllChecked ? "Clear" : "Check all";

    private bool AllChecked
    {
        get
        {
            int risable = RisableCount;
            return risable > 0 && Items.Count(i => i.IsChecked && i.IsRisableOrUnknown) == risable;
        }
    }

    /// <summary>Ticks every item that can rise, or clears them all when they are all ticked.</summary>
    [RelayCommand]
    private void ToggleCheckAll()
    {
        if (!Source.CanBatch) return;
        bool tick = !AllChecked;
        _bulkChecking = true;
        try
        {
            foreach (var item in Items)
                item.IsChecked = tick && item.IsRisableOrUnknown;
        }
        finally { _bulkChecking = false; }
        RefreshExport(size: false);
    }

    // ---------------------------------------------------------------- the live manifest

    private IReadOnlyList<string> BaseNames(IReadOnlyList<RiseItem> items) =>
        ModelBatch.UniqueBaseNames(items.Select(i => i.ExportBaseName).ToList());

    private string BatchFileStem => ModelBatch.SafeBaseName(Source.BatchStem);

    private static string PivotWords(Pivot p) => p switch
    {
        Pivot.Center => "center",
        Pivot.MinCorner => "min corner",
        _ => "bottom center",
    };

    private bool NothingToExport => Source.ExportRefusal is not null || ExportCount == 0;

    /// <summary>The manifest's heading: the file, zip or folder an export writes.</summary>
    public string ManifestTitle
    {
        get
        {
            if (NothingToExport) return "Nothing to export";
            var items = ExportItems;
            if (items.Count == 1) return ModelBatch.FileNamesFor(BaseNames(items)[0], Format)[0];
            return Packed ? BatchFileStem + ".zip" : BatchFileStem + "/";
        }
    }

    /// <summary>What the heading's file holds, from the same names the writer is given.</summary>
    public IReadOnlyList<string> ManifestParts
    {
        get
        {
            if (NothingToExport) return Array.Empty<string>();
            var items = ExportItems;
            var bases = BaseNames(items);
            if (items.Count == 1)
            {
                return Format switch
                {
                    ModelFormat.GltfWithBin => ModelBatch.FileNamesFor(bases[0], Format),
                    ModelFormat.GltfEmbedded => new[] { "buffer inside", "pivot " + PivotWords(Pivot) },
                    _ => new[] { "texture inside", "pivot " + PivotWords(Pivot) },
                };
            }
            int files = bases.Sum(b => ModelBatch.FileNamesFor(b, Format).Count);
            var firstThree = string.Join(", ", bases.Take(3).Select(b => ModelBatch.FileNamesFor(b, Format)[0]))
                + (items.Count > 3 ? ", …" : "");
            return new[]
            {
                string.Create(CultureInfo.InvariantCulture, $"{items.Count} models"),
                string.Create(CultureInfo.InvariantCulture, $"{files} files"),
                firstThree,
            };
        }
    }

    /// <summary>The exact size of a single model, or empty: a batch states counts and names, never a
    /// figure it would have to estimate.</summary>
    public string ManifestSize => _manifestSize;

    /// <summary>Why nothing can be exported, in words; null when something can.</summary>
    public string? ExportProblem
    {
        get
        {
            if (Source.ExportRefusal is { } refusal) return refusal;
            if (ExportCount > 0) return null;
            if (Source.CanBatch)
                return Items.Any(i => i.IsChecked)
                    ? "Everything checked stays 2D (partial alpha or nothing opaque), so there is nothing to rise."
                    : "Check at least one item to export.";
            return SelectedItem.Subject is { Depth.PartialCount: > 0 }
                ? "This item has partial alpha, so it stays 2D and cannot be exported: a voxel is solid or empty."
                : "Nothing in this item is fully opaque, so there is nothing to rise.";
        }
    }

    /// <summary>The export button's label.</summary>
    public string ExportLabel => ExportCount > 1
        ? string.Create(CultureInfo.InvariantCulture, $"Export {ExportCount} models…")
        : "Export model…";

    /// <summary>An export is running.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private bool _isExporting;

    private bool CanExport() => Source.ExportRefusal is null && ExportCount > 0 && !IsExporting;

    partial void OnIsDepthTabChanged(bool value)
    {
        // The exact size is built only while somebody can read it.
        if (value) return;
        RefreshExport(size: true);
    }

    private void RefreshExport(bool size)
    {
        if (_disposed) return;
        if (size) RecomputeSize();
        else if (ExportCount != 1) _manifestSize = "";
        OnPropertyChanged(nameof(ExportItems));
        OnPropertyChanged(nameof(ExportCount));
        OnPropertyChanged(nameof(ExportBadge));
        OnPropertyChanged(nameof(IsBatch));
        OnPropertyChanged(nameof(CheckedSummary));
        OnPropertyChanged(nameof(CheckAllLabel));
        OnPropertyChanged(nameof(ManifestTitle));
        OnPropertyChanged(nameof(ManifestParts));
        OnPropertyChanged(nameof(ManifestSize));
        OnPropertyChanged(nameof(ExportProblem));
        OnPropertyChanged(nameof(ExportLabel));
        ExportCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Builds the one model to count its bytes (ruling 8), only while the EXPORT tab shows.</summary>
    private void RecomputeSize()
    {
        _manifestSize = "";
        if (!IsExportTab || Source.ExportRefusal is not null) return;
        var items = ExportItems;
        if (items.Count != 1) return;
        try
        {
            SizeBuilds++;
            var result = ModelBatch.Build(Source.ExportInputs(items, BaseNames(items)), ExportOptions);
            if (result.Files.Count == 0) return;
            // The .gltf names its .bin, so its bytes depend on the stem the user has not picked yet:
            // that format's figure is said to be approximate; the other two carry no name at all.
            _manifestSize = (Format == ModelFormat.GltfWithBin ? "~" : "")
                + SizeText(result.Files.Sum(f => (long)f.Bytes.Length));
        }
        catch (Exception)
        {
            _manifestSize = "";     // a figure we cannot stand behind is left off, not guessed
        }
    }

    private static string SizeText(long bytes) => bytes switch
    {
        < 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes} B"),
        < 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:0.#} KB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024.0):0.#} MB"),
    };

    // ---------------------------------------------------------------- export

    /// <summary>Rises the exportable items and writes them where the user chooses.</summary>
    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task Export()
    {
        // A disabled button is a label: the generated command does not consult CanExecute. The sealed
        // refusal is first, so the picker never opens for a Set nothing may leave.
        if (!CanExport()) return;
        CancelDepthPreview();
        IsExporting = true;
        try
        {
            var items = ExportItems;
            if (items.Count == 1) await ExportOneAsync(items[0]);
            else await ExportManyAsync(items);
        }
        catch (Exception ex)
        {
            await _dialogs.ShowAsync<object>(new ErrorDialogViewModel(_dialogs, "Could not export", ex.Message));
        }
        finally
        {
            IsExporting = false;
        }
    }

    private async Task ExportOneAsync(RiseItem item)
    {
        // Everything the file depends on is read BEFORE the picker opens: a picker that stays open
        // while the user changes the format must not leave a .glb name on glTF bytes.
        var options = ExportOptions;
        string ext = options.Format == ModelFormat.Glb ? ".glb" : ".gltf";
        var path = await _picker.SaveFileAsync($"Export {ManifestTitle}", ext);
        if (string.IsNullOrEmpty(path)) return;

        string full = Path.GetFullPath(path);
        string dir = Path.GetDirectoryName(full)!;
        string stem = Path.GetFileNameWithoutExtension(full);

        // The first input is taken HERE, on the UI thread: it snapshots the depth, so a stroke painted
        // while the model is written cannot be half in it, and it keeps the enumerator (which may own
        // a decoded picture) alive until the build is done.
        using var inputs = Source.ExportInputs(new[] { item }, new[] { stem }).GetEnumerator();
        if (!inputs.MoveNext()) return;
        var input = inputs.Current;
        var result = await Task.Run(() =>
        {
            var built = ModelBatch.Build(new[] { input }, options);
            if (built.Files.Count > 0) ModelBatch.WriteFolder(dir, built.Files);
            return built;
        });

        MarkSkippedItems(result.Skipped, new[] { item }, new[] { stem });
        if (result.Files.Count == 0)
        {
            _status.Say("Nothing exported: " + SkippedText(result.Skipped) + ".");
            return;
        }
        _status.Say($"Exported {string.Join(", ", result.Files.Select(f => f.FileName))} to {dir}.");
    }

    private async Task ExportManyAsync(IReadOnlyList<RiseItem> items)
    {
        var options = ExportOptions;
        bool packed = Packed;
        string stem = BatchFileStem;
        string? destination = packed
            ? await _picker.SaveFileAsync($"Export {ManifestTitle}", ".zip")
            : await _picker.PickFolderAsync($"Export {ManifestTitle}");
        if (string.IsNullOrEmpty(destination)) return;

        var bases = BaseNames(items);
        // Ticked rows already known to be 2D are not in the batch; they are still named in the status.
        var knownFlat = Items.Where(i => i.IsChecked && !i.IsRisableOrUnknown).Select(i => i.Name + " (2D only)").ToList();
        string where = destination;
        var result = await BusyViewModel.RunAsync(_dialogs, "Exporting models",
            string.Create(CultureInfo.InvariantCulture, $"Rising {items.Count} models…"),
            (_, _) => Task.Run(() =>
            {
                var built = ModelBatch.Build(Source.ExportInputs(items, bases), options);
                if (built.Files.Count == 0) return built;
                if (packed) ModelBatch.WriteZip(destination, built.Files);
                else
                {
                    where = Path.Combine(destination, stem);
                    ModelBatch.WriteFolder(where, built.Files);
                }
                return built;
            }));

        MarkSkippedItems(result.Skipped, items, bases);
        var skipped = result.Skipped.Select(SkippedName).Concat(knownFlat).ToList();
        string tail = skipped.Count == 0 ? "" : " Skipped " + string.Join(", ", skipped) + ".";
        if (result.Files.Count == 0)
        {
            _status.Say("Nothing exported." + tail);
            return;
        }
        int models = items.Count - result.Skipped.Count;
        _status.Say(string.Create(CultureInfo.InvariantCulture,
            $"Exported {models} {(models == 1 ? "model" : "models")} ({result.Files.Count} files) to {where}.") + tail);
    }

    /// <summary>Items the writer refused become known to be 2D, so the manifest, badge and label stop
    /// promising them and a second export does not repeat the loss.</summary>
    private static void MarkSkippedItems(IReadOnlyList<SkippedModel> skipped, IReadOnlyList<RiseItem> items, IReadOnlyList<string> bases)
    {
        foreach (var skip in skipped)
        {
            // Names were made safe and distinct before the build, so the writer's name is the one we gave.
            for (int i = 0; i < items.Count; i++)
                if (string.Equals(bases[i], skip.Name, StringComparison.OrdinalIgnoreCase)) items[i].MarkSkipped(skip.Reason);
        }
    }

    private static string SkippedName(SkippedModel skip) => skip.Reason switch
    {
        SkipReason.PartialAlpha => $"{skip.Name} (partial alpha)",
        _ => $"{skip.Name} (nothing solid)",
    };

    private static string SkippedText(IReadOnlyList<SkippedModel> skipped) =>
        skipped.Count == 0 ? "nothing could rise" : "skipped " + string.Join(", ", skipped.Select(SkippedName));
}
