using Nfty.App.Services;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Rise;
using Nfty.Core.Publish;

namespace Nfty.App.ViewModels;

/// <summary>How a sheet's grid is entered.</summary>
public enum GridMode
{
    /// <summary>Find it by the transparent gutters between cells.</summary>
    Auto,

    /// <summary>Say how many columns and rows.</summary>
    ColumnsRows,

    /// <summary>Say how big one cell is.</summary>
    CellSize,
}

public sealed partial class RiseViewModel
{
    private SheetGrid? _detected;
    private bool _detectedKnown;

    // Opens the row on the grid the source is cut by now, so Apply starts as a no-op and the numbers
    // are the picture's own.
    private void InitGrid()
    {
        if (Source is not PictureRiseSource pic) return;
        GridA = pic.Grid?.Columns ?? 1;
        GridB = pic.Grid?.Rows ?? 1;
    }

    /// <summary>The page carries the grid row: a loose picture or a sheet, where the grid is the
    /// person's to correct (detection can be wrong on a foreign sheet). Constant for the page's life —
    /// a picture becomes a sheet by entering a grid — so the row is laid out or not, never toggled.</summary>
    public bool HasGridRow => Source is PictureRiseSource;

    /// <summary>How the two numbers below are read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GridProblem), nameof(GridNumbersEnabled), nameof(GridIsAuto),
        nameof(GridIsColumnsRows), nameof(GridIsCellSize))]
    [NotifyCanExecuteChangedFor(nameof(ApplyGridCommand))]
    private GridMode _gridMode = GridMode.ColumnsRows;

    /// <summary>Columns, or a cell's width.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GridProblem))]
    [NotifyCanExecuteChangedFor(nameof(ApplyGridCommand))]
    private int _gridA = 1;

    /// <summary>Rows, or a cell's height.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GridProblem))]
    [NotifyCanExecuteChangedFor(nameof(ApplyGridCommand))]
    private int _gridB = 1;

    /// <summary>The tray's first button is the one in force.</summary>
    public bool GridIsAuto => GridMode == GridMode.Auto;

    /// <summary>The tray's second button is the one in force.</summary>
    public bool GridIsColumnsRows => GridMode == GridMode.ColumnsRows;

    /// <summary>The tray's third button is the one in force.</summary>
    public bool GridIsCellSize => GridMode == GridMode.CellSize;

    /// <summary>The two number cells take input; with Auto there is nothing to type.</summary>
    public bool GridNumbersEnabled => GridMode != GridMode.Auto;

    /// <summary>Why the grid as entered cannot be applied, or "" when it can. The line under the row
    /// reserves its space either way.</summary>
    public string GridProblem
    {
        get
        {
            if (Source is not PictureRiseSource pic) return "";
            return ProblemFor(pic, out _) ?? "";
        }
    }

    // The grid the entry makes, and what is wrong with it, from the engine's own rule
    // (SheetGrid.TryFrom...), so this screen has no second idea of "tiles the picture" and the words
    // name what was typed: columns for columns, a cell's width for a cell size.
    private string? ProblemFor(PictureRiseSource pic, out SheetGrid? grid)
    {
        int w = pic.ImageWidth, h = pic.ImageHeight;
        grid = null;
        switch (GridMode)
        {
            case GridMode.Auto:
                // The picture never changes, so it is searched once.
                if (!_detectedKnown) { _detected = pic.DetectGrid(); _detectedKnown = true; }
                grid = _detected;
                return grid is null ? "No grid found: the cells have no transparent gutters to tell them apart." : null;
            case GridMode.ColumnsRows:
                return SheetGrid.TryFromColumnsRows(GridA, GridB, w, h, out grid);
            default:
                return SheetGrid.TryFromCellSize(GridA, GridB, w, h, out grid);
        }
    }

    // Moving to another way of saying the same grid keeps what is true: the numbers become the current
    // grid's own in the new terms.
    partial void OnGridModeChanged(GridMode value)
    {
        if (Source is not PictureRiseSource pic || value == GridMode.Auto) return;
        var g = pic.Grid;
        if (value == GridMode.ColumnsRows)
        {
            GridA = g?.Columns ?? 1;
            GridB = g?.Rows ?? 1;
        }
        else
        {
            GridA = g?.CellWidth ?? pic.ImageWidth;
            GridB = g?.CellHeight ?? pic.ImageHeight;
        }
    }

    /// <summary>Picks how the grid is entered.</summary>
    /// <param name="mode">The way to enter it.</param>
    [RelayCommand]
    private void SetGridMode(GridMode mode) => GridMode = mode;

    private bool CanApplyGrid() =>
        Source is PictureRiseSource && !IsExporting && !IsSaving && !IsVoxelBusy && GridProblem.Length == 0;

    /// <summary>Re-cuts the picture by the grid as entered, and writes it into the file.</summary>
    /// <remarks>Asks first when any cell carries unsaved depth, because re-cutting discards it all; the
    /// same grid again changes nothing and asks nothing. The page then stands on the new first cell.</remarks>
    [RelayCommand(CanExecute = nameof(CanApplyGrid))]
    private async Task ApplyGrid()
    {
        // A disabled button is a label: the generated command does not consult CanExecute.
        if (Source is not PictureRiseSource pic) return;
        if (!CanApplyGrid())
        {
            // Said, not swallowed: a press that reaches here past a dimmed button is a job still running.
            if (GridProblem.Length == 0) _status.Say("Wait for the running job to finish, then apply the grid.");
            return;
        }
        if (ProblemFor(pic, out var grid) is not null) return;
        // One cell is no sheet; the source reads it as "one picture".
        bool same = Equals(grid, pic.Grid) || (grid is { Count: 1 } && pic.Grid is null);
        int dirty = Items.Count(i => i.IsDirty);
        if (!same && dirty > 0)
        {
            var message = string.Create(CultureInfo.InvariantCulture,
                $"{dirty} {(dirty == 1 ? "cell has" : "cells have")} depth that is not saved. Cutting the picture a new way discards it.");
            var ok = await _dialogs.ShowAsync<bool>(
                new ConfirmDialogViewModel(_dialogs, "Change the grid?", message, "Change grid"));
            if (ok != true) return;
        }
        if (same)
        {
            _status.Say("The picture is already cut that way.");
            return;
        }

        CancelDepthPreview();
        var old = Items.ToList();
        foreach (var item in old) item.PropertyChanged -= OnItemExportChanged;
        // The rewrite of the PNG runs off the UI thread under the same busy flag voxel-ready holds, so
        // Save, Export and a second Apply are refused for as long as it runs.
        IsVoxelBusy = true;
        try { await pic.ApplyGridAsync(grid); }
        catch (Exception ex)
        {
            foreach (var item in old) item.PropertyChanged += OnItemExportChanged;
            if (!_disposed)
                await _dialogs.ShowErrorAsync("Could not change the grid", ex.Message);
            return;
        }
        finally { IsVoxelBusy = false; }
        if (_disposed) return;
        foreach (var item in Items) item.PropertyChanged += OnItemExportChanged;
        if (Source.CanBatch && Source.InitialItem.IsRisableOrUnknown) Source.InitialItem.IsChecked = true;

        // The page stands on the new first cell; every binding that reads the source is told it changed
        // (the kind of rail, the facts, the batch controls all follow the grid).
        SelectedItem = Source.InitialItem;
        OnPropertyChanged(nameof(Source));
        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(Facts));
        OnPropertyChanged(nameof(IsTileRail));
        RefreshExport(size: true);
        OnDepthEdited();
        if (GridMode != GridMode.Auto) OnGridModeChanged(GridMode);   // the numbers read the grid now in force

        var cut = pic.Grid is { } g
            ? string.Create(CultureInfo.InvariantCulture, $"Cut into {g.Count} cells ({g.Columns} × {g.Rows}, {g.CellWidth}×{g.CellHeight}).")
            : "Back to one picture.";
        _status.Say(pic.GridWriteNote is { } note ? $"{cut} {note}" : cut);
    }
}
