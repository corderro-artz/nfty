using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Rise;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Editing;
using Nfty.Core.Publish;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>The grid row on the Rise rail: typing how a picture is cut.</summary>
public class RiseSheetGridTests
{
    private static string Temp() => Directory.CreateTempSubdirectory("nfty-rise-grid-").FullName;

    /// <summary>A solid picture with no gutters, so nothing detects a grid in it: only a typed one cuts it.</summary>
    private static string Solid(int w, int h, string name = "strip.png")
    {
        var path = Path.Combine(Temp(), name);
        using var img = new Image<Rgba32>(w, h, new Rgba32(120, 60, 200, 255));
        img.Save(path);
        return path;
    }

    private sealed class Yes : IDialogService
    {
        public ViewModelBase? Active { get; private set; }
        public event Action? Changed;
        public Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog)
        {
            Active = dialog;
            Changed?.Invoke();
            return Task.FromResult<TResult?>((TResult?)(object)true);
        }
        public void Close(object? result) { Active = null; Changed?.Invoke(); }
    }

    private static RiseViewModel Page(string path, IDialogService dialogs, IStatusService? status = null) =>
        new(PictureRiseSource.Open(path, new ImageBridge(), () => Task.FromResult<string?>(null)), new ImageBridge(), new FakeNav(), dialogs,
            new FilePickerService(), status ?? new StatusService());

    private static void Paint(RiseViewModel vm, int item)
    {
        var subject = vm.Source.Open(vm.Items[item]);
        subject.History.Do(new DepthStep(new[] { (1, 1) }, DepthSide.Front, +1), subject.Depth);
        vm.Items[item].RefreshDepth();
    }

    [AvaloniaFact]
    public async Task Entering_columns_and_rows_reslices_and_rewrites()
    {
        var path = Solid(16, 8);
        var status = new StatusService();
        using var vm = Page(path, new FakeDialogs(), status);
        Assert.Single(vm.Items);
        Assert.Equal(RiseSourceKind.Picture, vm.Source.Kind);

        vm.GridMode = GridMode.ColumnsRows;
        vm.GridA = 2;
        vm.GridB = 1;
        await vm.ApplyGridCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Items.Count);
        Assert.Equal(RiseSourceKind.Sheet, vm.Source.Kind);
        Assert.Same(vm.Items[0], vm.SelectedItem);
        Assert.Equal(new SheetGrid(2, 1, 8, 8, 2), SheetGrid.Read(path));          // the file says so now
        Assert.Contains("Wrote the grid", status.Last);
        Assert.Equal(8, vm.Subject.Art.Width);                                      // a cell, not the whole strip
    }

    [AvaloniaFact]
    public async Task A_cell_size_cuts_the_same_way()
    {
        var path = Solid(24, 8);
        using var vm = Page(path, new FakeDialogs());

        vm.GridMode = GridMode.CellSize;
        vm.GridA = 8;
        vm.GridB = 8;
        await vm.ApplyGridCommand.ExecuteAsync(null);

        Assert.Equal(3, vm.Items.Count);
        Assert.Equal(new SheetGrid(3, 1, 8, 8, 3), SheetGrid.Read(path));
    }

    [AvaloniaFact]
    public async Task Auto_finds_the_cells_by_their_gutters()
    {
        var path = Path.Combine(Temp(), "gutters.png");
        using (var img = new Image<Rgba32>(32, 16))
        {
            for (int n = 0; n < 2; n++)
                for (int y = 4; y < 12; y++)
                    for (int x = 4; x < 12; x++)
                        img[n * 16 + x, y] = new Rgba32(200, 40, 40, 255);
            img.Save(path);
        }
        using var vm = Page(path, new FakeDialogs());
        // Opening already detected it; take the grid away, then ask for detection again.
        vm.GridMode = GridMode.ColumnsRows;
        vm.GridA = 1;
        vm.GridB = 1;
        await vm.ApplyGridCommand.ExecuteAsync(null);
        Assert.Single(vm.Items);

        vm.GridMode = GridMode.Auto;
        Assert.Equal("", vm.GridProblem);
        await vm.ApplyGridCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Items.Count);
    }

    [AvaloniaFact]
    public async Task A_grid_that_does_not_divide_disables_apply_and_says_why()
    {
        var path = Solid(16, 8);
        var before = File.ReadAllBytes(path);
        using var vm = Page(path, new FakeDialogs());

        vm.GridMode = GridMode.ColumnsRows;
        vm.GridA = 3;
        vm.GridB = 1;

        Assert.Contains("16 px does not divide into 3 columns", vm.GridProblem);
        Assert.False(vm.ApplyGridCommand.CanExecute(null));
        await vm.ApplyGridCommand.ExecuteAsync(null);        // a disabled button is a label: still refused
        Assert.Single(vm.Items);
        Assert.Equal(before, File.ReadAllBytes(path));

        vm.GridA = 4;
        Assert.Equal("", vm.GridProblem);
        Assert.True(vm.ApplyGridCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void The_cell_size_problem_names_the_cell_that_was_typed_and_the_picture_it_misses()
    {
        using var vm = Page(Solid(48, 32), new FakeDialogs());
        vm.GridMode = GridMode.CellSize;

        vm.GridA = 5; vm.GridB = 8;
        Assert.Equal("48 px does not divide into cells 5 px wide", vm.GridProblem);

        vm.GridA = 8; vm.GridB = 5;
        Assert.Equal("32 px does not divide into cells 5 px tall", vm.GridProblem);
    }

    [AvaloniaFact]
    public async Task The_leave_card_counts_cells_for_a_sheet_and_variants_for_a_layer()
    {
        var dialogs = new FakeDialogs();
        using var sheet = Page(Solid(16, 8), dialogs);
        sheet.GridA = 2; sheet.GridB = 1;
        await sheet.ApplyGridCommand.ExecuteAsync(null);
        Paint(sheet, 0);
        Paint(sheet, 1);

        await sheet.BackCommand.ExecuteAsync(null);
        var card = Assert.IsType<LeaveDialogViewModel>(dialogs.Active);
        Assert.Contains("unsaved depth on 2 cells.", card.Message);
        Assert.Contains("unsaved depth on 2 cells", sheet.FooterText);

        using var variant = RisePages.VariantVm(oakDepth: RisePages.PaintedOak());
        var subject = variant.Source.Open(variant.Items[0]);
        subject.History.Do(new DepthStep(new[] { (3, 3) }, DepthSide.Front, +1), subject.Depth);
        variant.Items[0].RefreshDepth();
        Assert.Contains("unsaved depth on 1 variant", variant.FooterText);
    }

    [AvaloniaFact]
    public void Apply_is_as_tall_as_the_number_cells_beside_it()
    {
        using var vm = Page(Solid(48, 32), new FakeDialogs());
        using var page = RisePages.Show(vm, 1180, 720);
        double small = (double)Application.Current!.FindResource("FieldHeightSm")!;
        var apply = page.View.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Apply");
        var cell = page.View.GetVisualDescendants().OfType<NumericUpDown>().First(n => n.Classes.Contains("nin") && n.IsEffectivelyVisible);

        Assert.Equal(small, apply.Bounds.Height);
        Assert.Equal(cell.Bounds.Height, apply.Bounds.Height);
    }

    [AvaloniaFact]
    public async Task Changing_the_grid_with_unsaved_depth_asks_first()
    {
        var path = Solid(16, 8);
        var dialogs = new FakeDialogs();
        using var vm = Page(path, dialogs);
        vm.GridA = 2; vm.GridB = 1;
        await vm.ApplyGridCommand.ExecuteAsync(null);                   // a clean sheet of two
        Assert.Null(dialogs.Active);                                    // nothing unsaved: nothing asked
        Paint(vm, 0);
        Assert.True(vm.HasUnsavedDepth);

        vm.GridMode = GridMode.ColumnsRows;
        vm.GridA = 4; vm.GridB = 1;
        await vm.ApplyGridCommand.ExecuteAsync(null);                   // the stub answers "no"

        Assert.IsType<ConfirmDialogViewModel>(dialogs.Active);
        Assert.Equal(2, vm.Items.Count);                                // unchanged
        Assert.True(vm.HasUnsavedDepth);                                // depth kept
        Assert.Equal(2, SheetGrid.Read(path)!.Columns);                 // and so is the file
    }

    [AvaloniaFact]
    public async Task Saying_yes_recuts_and_discards_the_depth()
    {
        var path = Solid(16, 8);
        using var vm = Page(path, new Yes());
        vm.GridA = 2; vm.GridB = 1;
        await vm.ApplyGridCommand.ExecuteAsync(null);
        Paint(vm, 0);

        vm.GridA = 4; vm.GridB = 1;
        await vm.ApplyGridCommand.ExecuteAsync(null);

        Assert.Equal(4, vm.Items.Count);
        Assert.False(vm.HasUnsavedDepth);
        Assert.Equal(4, SheetGrid.Read(path)!.Columns);
    }

    [AvaloniaFact]
    public async Task The_same_grid_again_keeps_the_depth_and_asks_nothing()
    {
        var path = Solid(16, 8);
        using var vm = Page(path, new FakeDialogs());
        vm.GridA = 2; vm.GridB = 1;
        await vm.ApplyGridCommand.ExecuteAsync(null);
        Paint(vm, 0);
        var item = vm.Items[0];

        await vm.ApplyGridCommand.ExecuteAsync(null);                   // 2 x 1 again

        Assert.Same(item, vm.Items[0]);
        Assert.True(vm.HasUnsavedDepth);
    }

    [AvaloniaFact]
    public void Only_a_loose_picture_or_sheet_carries_the_row()
    {
        var path = Solid(16, 8);
        using var vm = Page(path, new FakeDialogs());
        Assert.True(vm.HasGridRow);

        using var variant = RisePages.VariantVm();
        using var recipe = RisePages.RecipeVm();
        Assert.False(variant.HasGridRow);
        Assert.False(recipe.HasGridRow);
    }

    private static byte[] Region(WriteableBitmap frame, int x0, int y0, int x1, int y1)
    {
        using var fb = frame.Lock();
        var bytes = new byte[(x1 - x0) * (y1 - y0) * 4];
        int row = fb.RowBytes;
        var all = new byte[row * fb.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(fb.Address, all, 0, all.Length);
        int o = 0;
        for (int y = y0; y < y1; y++)
        {
            Buffer.BlockCopy(all, y * row + x0 * 4, bytes, o, (x1 - x0) * 4);
            o += (x1 - x0) * 4;
        }
        return bytes;
    }

    [AvaloniaFact]
    public void The_grid_row_moves_nothing_when_its_problem_line_appears()
    {
        var path = Solid(48, 32);
        using var vm = Page(path, new FakeDialogs());
        using var page = RisePages.Show(vm, 1180, 720);
        var line = page.View.GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.Classes.Contains("caveat") && t.Height == 28);
        var origin = line.TranslatePoint(new Avalonia.Point(0, 0), page.Window)!.Value;
        int top = (int)Math.Ceiling(origin.Y + line.Bounds.Height);
        int railWidth = 262;
        int bottom = (int)page.Window.ClientSize.Height;

        Assert.Equal("", vm.GridProblem);
        using var clean = page.Window.CaptureRenderedFrame()!;
        var below = Region(clean, 0, top, railWidth, bottom);
        var lineBounds = line.Bounds;

        vm.GridMode = GridMode.ColumnsRows;
        vm.GridA = 5;                    // 48 px does not divide into 5 columns
        Dispatcher.UIThread.RunJobs();
        Assert.NotEqual("", vm.GridProblem);
        using var problem = page.Window.CaptureRenderedFrame()!;

        Assert.Equal(lineBounds, line.Bounds);
        Assert.Equal(below, Region(problem, 0, top, railWidth, bottom));
        // and the line itself is the one thing that changed
        Assert.NotEqual(Region(clean, 0, (int)origin.Y, railWidth, top), Region(problem, 0, (int)origin.Y, railWidth, top));
    }
}
