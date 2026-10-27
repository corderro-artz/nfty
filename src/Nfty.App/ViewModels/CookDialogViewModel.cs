using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;
using Nfty.Core;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Output;
using Nfty.Core.Stats;
using Nfty.Model;

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

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(CookCommand))] [NotifyPropertyChangedFor(nameof(ResultingTotalText))] private int _count = 50;
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
    // BOTH commands, not just Reveal. A CanExecute predicate is only re-read when something tells the
    // command to re-read it, and a button's IsEnabled follows CanExecuteChanged and nothing else - so
    // Open Set was laid out, styled, correct in its predicate and PERMANENTLY DISABLED in the running
    // app, while a test calling OpenSetCommand.CanExecute(null) evaluated the predicate directly and
    // passed. Driving is what found it; see CookDialogGestureTests, which asks the BUTTON.
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(RevealCommand))] [NotifyCanExecuteChangedFor(nameof(OpenSetCommand))] [NotifyPropertyChangedFor(nameof(ShowForm))] [NotifyPropertyChangedFor(nameof(FootHint))] [NotifyPropertyChangedFor(nameof(ProvenanceShown))] private bool _isDone;

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
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ResultingTotalText))] [NotifyPropertyChangedFor(nameof(RecipeChoiceEnabled))] private bool _isExtending;

    /// <summary>How many assets the folder's Set already held when this cook found it. Zero until a
    /// folder has been read.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ResultingTotalText))] private int _existingCount;

    /// <summary>What the Set will hold once an extend finishes: <c>→ 62 total</c>, invariant and
    /// thousands-separated. Empty on a fresh cook - the view reserves the room and draws no ink, so
    /// finding out the folder holds a Set moves nothing. Count keeps meaning "how many more".</summary>
    public string ResultingTotalText => IsExtending
        ? $"→ {((long)ExistingCount + Math.Max(0, Count)).ToString("N0", CultureInfo.InvariantCulture)} total"
        : "";

    /// <summary>The recipes this cook may be limited to: any (by weight), then each recipe in the
    /// book's own order. A shelved recipe (weight 0) is listed and labeled, since <c>--recipe</c>
    /// cooks it too.</summary>
    public IReadOnlyList<RecipeChoice> RecipeChoices { get; }

    /// <summary>The chosen recipe; the first entry (any recipe, by weight) unless the person picks.</summary>
    [ObservableProperty] private RecipeChoice _selectedRecipe;

    /// <summary>Said in the running line when a recipe was chosen and the folder turned out to hold a
    /// Set: the choice is picked before the folder, so this is where the person learns it was not used.</summary>
    internal const string RecipeSetAsideText = "Extending — the Set's own recipe mix is used; your recipe choice was set aside.";

    /// <summary>Whether the recipe choice can be changed. Off while extending: restricting would
    /// silently change a Set's mix part-way through the collection.</summary>
    public bool RecipeChoiceEnabled => !IsExtending;

    /// <summary>The reason the recipe choice is off, drawn beside it while extending.</summary>
    public const string RecipeLockedReason = "An extend continues the Set's own mix.";

    /// <summary>Also write a 3D model of every asset into <c>models/</c>.</summary>
    /// <remarks>Disabled for a 2D-only book, but a disabled box is a label: <see cref="Cook"/>
    /// checks <see cref="BookIsTwoDOnly"/> again before it builds a modeler.</remarks>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowModelOptionsSection))] [NotifyPropertyChangedFor(nameof(ShowModelOptionsToggle))] private bool _includeModels;

    /// <summary>Whether the three model choices are showing. Starts closed: the defaults are what
    /// nearly everyone wants, and the choices are session-only (not persisted).</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowModelOptionsSection))] private bool _showModelOptions;

    /// <summary>Opens or closes the model choices.</summary>
    [RelayCommand] private void ToggleModelOptions() => ShowModelOptions = !ShowModelOptions;

    /// <summary>The file format for the models.</summary>
    [ObservableProperty] private ModelFormat _modelFormat = ModelFormat.Glb;

    /// <summary>Which point of each model sits at its origin.</summary>
    [ObservableProperty] private Pivot _modelPivot = Pivot.BottomCenter;

    /// <summary>World units per voxel.</summary>
    [ObservableProperty] private double _voxelSize = 1;

    /// <summary>The smallest voxel size: <see cref="Nfty.Core.Publish.ModelVoxelSize.Min"/>, as the spinner takes it.</summary>
    public decimal VoxelSizeMin => (decimal)Nfty.Core.Publish.ModelVoxelSize.Min;

    /// <summary>The largest voxel size: <see cref="Nfty.Core.Publish.ModelVoxelSize.Max"/>, as the spinner takes it.</summary>
    public decimal VoxelSizeMax => (decimal)Nfty.Core.Publish.ModelVoxelSize.Max;

    /// <summary>Every format, with the words the picker shows.</summary>
    public IReadOnlyList<ModelFormatChoice> FormatChoices { get; } =
    [
        new ModelFormatChoice(ModelFormat.Glb, "GLB"),
        new ModelFormatChoice(ModelFormat.GltfWithBin, "glTF + bin"),
        new ModelFormatChoice(ModelFormat.GltfEmbedded, "glTF embedded"),
    ];

    /// <summary>Every pivot, with the words the picker shows.</summary>
    public IReadOnlyList<ModelPivotChoice> PivotChoices { get; } =
    [
        new ModelPivotChoice(Pivot.BottomCenter, "Bottom center"),
        new ModelPivotChoice(Pivot.Center, "Center"),
        new ModelPivotChoice(Pivot.MinCorner, "Min corner"),
    ];

    /// <summary>True when some variant has a partly transparent pixel, so no model can be made.
    /// False until the scan lands (<see cref="ScanLanded"/>): the scan reads every pixel of every
    /// variant, which is not work for the thread that is building the card. Computed once: the book
    /// is borrowed and not edited while this card is open.</summary>
    [ObservableProperty] private bool _bookIsTwoDOnly;

    /// <summary>Why the box is off, or what it does. Reserved before the scan lands and not inked
    /// (<see cref="ModelsNoteShown"/>), so the row never moves when the answer arrives.</summary>
    [ObservableProperty] private string _modelsNote = "Writes a model of every asset into models/.";

    /// <summary>True once the partial-alpha scan has answered. Cook stays disabled until then,
    /// because whether a model can be made depends on it.</summary>
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(CookCommand))] [NotifyPropertyChangedFor(nameof(ModelsNoteShown))] private bool _scanLanded;

    /// <summary>Whether the models note is inked: it fades in when the scan lands.</summary>
    public bool ModelsNoteShown => ScanLanded;

    /// <summary>The scan, from construction to its answer being applied. Never faults: a scan that
    /// throws lands as "no partial alpha" and the modeler refuses a bad book itself, with a message.</summary>
    internal Task ScanTask { get; }

    /// <summary>Set when a run extends a Set cooked from a different book than this one, in the
    /// words <see cref="SetProvenance.Warning"/> gives the CLI. A warning, never a refusal.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ProvenanceShown))] [NotifyPropertyChangedFor(nameof(ProvenanceHeadline))] private string _provenanceWarning = "";

    /// <summary>The first line of the warning, verbatim: what the card has room for. The whole text,
    /// hashes and advice, is the tooltip and the CLI's stderr; a card that printed all nine lines grew
    /// past the form it shares a cell with.</summary>
    public string ProvenanceHeadline => ProvenanceWarning.Split((char)10)[0].TrimEnd();

    /// <summary>Whether the provenance line is inked: only in the done state, and only when there
    /// is something to say. The line is reserved either way.</summary>
    public bool ProvenanceShown => IsDone && ProvenanceWarning.Length > 0;

    /// <summary>Whether the model choices are on show: a reveal, last in the form.</summary>
    public bool ShowModelOptionsSection => IncludeModels && ShowModelOptions;

    /// <summary>Whether the disclosure toggle is on show.</summary>
    public bool ShowModelOptionsToggle => IncludeModels;

    /// <summary>Creates the cook dialog.</summary>
    /// <param name="book">The book to generate from.</param>
    /// <param name="picker">Chooses the output folder.</param>
    /// <param name="revealer">Opens the output folder when the run finishes.</param>
    /// <param name="dialogs">The dialog layer to close through.</param>
    /// <param name="threads">The thread count to run with, read at the start of each operation;
    /// null runs at the default.</param>
    /// <param name="scan">Counts the book's partial-alpha variants; null scans with <see cref="VoxelReport"/>.
    /// A seam so a test can prove the constructor does not run it.</param>
    public CookDialogViewModel(LoadedCookBook book, IFilePickerService picker, IFolderRevealer revealer, IDialogService dialogs,
        IParallelismSetting? threads = null, Func<LoadedCookBook, Parallelism, int>? scan = null)
    {
        _book = book; _picker = picker; _revealer = revealer; _dialogs = dialogs; _threads = threads;
        _scan = scan;
        RecipeChoices = BuildRecipeChoices(book);
        _selectedRecipe = RecipeChoices[0];
        ScanTask = ScanAsync();
    }

    private readonly IParallelismSetting? _threads;
    private readonly Func<LoadedCookBook, Parallelism, int>? _scan;

    private Parallelism Threads => _threads?.Current ?? Parallelism.Default;

    /// <summary>
    /// Counts the partial-alpha variants OFF the calling thread and applies the answer back on it.
    /// The cache answers first when the Explorer has already looked at these very images, and then
    /// nothing is scanned at all.
    /// </summary>
    private async Task ScanAsync()
    {
        int partial = 0;
        try
        {
            var par = Threads;
            var book = _book;
            var scan = _scan;
            // Not awaited with ConfigureAwait(false): the answer is applied on the context that
            // built the card, which is the one the bindings read on.
            partial = await Task.Run(() =>
                scan is not null ? scan(book, par)
                : PartialAlphaCache.TryCountPartialVariants(book, out var known) ? known
                : VoxelReport.Scan(book, par).Count(v => !v.IsClean));
        }
        catch (Exception)
        {
            // Lands as clean; SetModeler refuses a book with partial alpha by name when Cook builds one.
            partial = 0;
        }
        BookIsTwoDOnly = partial > 0;
        ModelsNote = BookIsTwoDOnly
            ? $"This book is 2D only — {partial} {(partial == 1 ? "variant has" : "variants have")} partly transparent pixels. "
              + "Open a layer in Rise and use Make voxel-ready."
            : "Writes a model of every asset into models/.";
        ScanLanded = true;
    }

    private static List<RecipeChoice> BuildRecipeChoices(LoadedCookBook book)
    {
        var rank = new Dictionary<string, int>(StringComparer.Ordinal);
        if (book.Manifest.RecipeOrder is { } order)
            for (int i = 0; i < order.Count; i++) rank.TryAdd(order[i], i);

        var choices = new List<RecipeChoice> { new(null, "Any recipe (by weight)") };
        choices.AddRange(book.Recipes
            .Select((r, i) => (r, i))
            .OrderBy(t => rank.TryGetValue(t.r.Manifest.Id, out int k) ? k : int.MaxValue)
            .ThenBy(t => t.i)
            .Select(t =>
            {
                bool shelved = !book.Manifest.RecipeWeights.TryGetValue(t.r.Manifest.Id, out double w) || w <= 0;
                return new RecipeChoice(t.r.Manifest.Id, shelved ? $"{t.r.Manifest.Name} (shelved)" : t.r.Manifest.Name);
            }));
        return choices;
    }

    /// <summary>Whether the options form is showing rather than the progress view.</summary>
    public bool ShowForm => !IsRunning && !IsDone;

    /// <summary>The footer's left-hand hint, which is a different sentence in each of the three
    /// states this one card passes through.</summary>
    public string FootHint => IsRunning
        ? "Cancel stops after the current asset"
        : IsDone ? "Open Set to browse and export it" : "Same book, same seed, same collection";

    private bool CanCook() => ScanLanded && !IsRunning && Count > 0;

    [RelayCommand(CanExecute = nameof(CanCook))]
    private async Task Cook()
    {
        // The button is disabled until the scan lands, but a disabled button is a label (a generated
        // command's ExecuteAsync does not consult CanExecute): the answer the modeler gate below
        // reads must be in before anything is decided.
        await ScanTask;

        var dir = await _picker.PickFolderAsync("Cook to folder");
        if (dir is null) return;

        // Read ONCE, here: a change on the Settings card applies to the next cook, never this one.
        var par = Threads;
        ProvenanceWarning = "";
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
            var existing = await SetWriter.ReadExistingAsync(dir, _cts.Token, par);
            IsExtending = existing.Dnas.Count > 0;
            // The CLI's rule and the CLI's words: a warning, never a refusal. Re-cooking a
            // deliberately edited book is legitimate, and the author is the one who knows.
            if (IsExtending && SetProvenance.Warning(existing.CookbookSha256, _book.SourceSha256) is { } warning)
                ProvenanceWarning = warning;
            // The items the reader actually loaded, not the highest number: a Set with a deleted item
            // holds fewer than its top number says, and the total is a count of what it holds.
            ExistingCount = IsExtending ? existing.Dnas.Count : 0;

            // A Set cooked with models keeps making them, whatever the box says: models are decided
            // once, at its first cook, as `extend` does. Built BEFORE rolling so a 2D-only book is
            // refused with nothing generated. The box is a label; the gate is checked here too.
            SetModeler? modeler = existing.Models is { } recorded
                ? new SetModeler(_book, ModelTokens.OptionsOf(recorded))
                : IncludeModels && !BookIsTwoDOnly
                    ? new SetModeler(_book, new RiseOptions
                    {
                        VoxelSize = (float)VoxelSize,
                        Pivot = ModelPivot,
                        Format = ModelFormat,
                    })
                    : null;

            // An extend continues the Set's own mix, so the recipe choice is ignored then: the combo is
            // disabled, but a disabled box is a label, and the gate is checked here too.
            var opts = new GenerateOptions(Count, Seed, RecipeId: IsExtending ? null : SelectedRecipe.Id,
                EnforceUniqueDna: !Unlimited, Parallelism: par);
            PhaseText = !IsExtending ? "Generating…"
                : $"Extending {ExistingCount} assets {ResultingTotalText}…" + (SelectedRecipe.Id is null ? "" : " " + RecipeSetAsideText);
            var genProgress = new Progress<GenerationProgress>(p => Progress = p.Fraction);
            set = await Generator.GenerateAsync(_book, opts, existing.Dnas, existing.NextNumber,
                genProgress, _cts.Token);

            PhaseText = modeler is null ? "Writing…" : "Writing models…"; Progress = 0;
            var writeProgress = new Progress<WriteProgress>(p => Progress = p.Fraction);
            await SetWriter.WriteAsync(set, dir, Pack, writeProgress, _cts.Token, models: modeler, parallelism: par);

            _outDir = dir;
            OutputPath = dir;
            int made = set.Assets.Count;
            string noun = made == 1 ? "asset" : "assets";
            ResultText = IsExtending
                ? $"Added +{made} {noun} — {ExistingCount + made} total in the collection."
                : $"Cooked {made} {noun}.";
            if (modeler is not null)
            {
                var written = (await SetWriter.ReadExistingAsync(dir, _cts.Token, par)).Models;
                int skipped = (written?.Skipped ?? []).Count(n => n >= existing.NextNumber);
                ResultText += $" · {made - skipped} models" + (skipped > 0 ? $" · {skipped} skipped" : "");
            }
            IsDone = true;
        }
        catch (OperationCanceledException) { PhaseText = "Canceled"; }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync("Cook failed", ex.Message);
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

    /// <summary>Closes the dialog and hands the caller the folder the Set was written to.</summary>
    /// <remarks>
    /// <para>THIS IS THE ONLY WAY TO REACH THE EXPORT DIALOG FROM A BOOK YOU JUST COOKED. Export -
    /// pick the art, either metadata set, the source .cbk, and whether to seal - lives on the Set
    /// browser, because it is an operation on a cooked Set and there is nothing to plan before one
    /// exists. But cooking left you on the Explorer with a path on a card, so the only route to it
    /// was to go back to Landing and re-open by hand the folder the app had just written. Everything
    /// worked and the feature was unreachable in the flow that produces its input.</para>
    ///
    /// <para>The result is the PATH rather than a loaded Set: reading it is the caller's business,
    /// and a Set opened here would have to be disposed by whoever failed to navigate. Packing writes
    /// a <c>.set</c> INSIDE the folder rather than instead of it, so the folder is a readable Set
    /// either way and the answer does not depend on that checkbox.</para>
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanReveal))] private void OpenSet() => _dialogs.Close(_outDir);

    [RelayCommand(CanExecute = nameof(CanClose))] private void Close() => _dialogs.Close(null);
    private bool CanClose() => !IsRunning;
}

/// <summary>One entry of the recipe picker.</summary>
/// <param name="Id">The recipe id the cook is limited to, or null for any recipe by weight.</param>
/// <param name="Label">What the picker shows.</param>
public sealed record RecipeChoice(string? Id, string Label);

/// <summary>One entry of the model format picker.</summary>
/// <param name="Value">What the choice sets.</param>
/// <param name="Label">What the picker shows.</param>
public sealed record ModelFormatChoice(ModelFormat Value, string Label);

/// <summary>One entry of the model pivot picker.</summary>
/// <param name="Value">What the choice sets.</param>
/// <param name="Label">What the picker shows.</param>
public sealed record ModelPivotChoice(Pivot Value, string Label);
