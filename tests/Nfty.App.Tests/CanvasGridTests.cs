using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using Image = Avalonia.Controls.Image;
using Point = Avalonia.Point;

namespace Nfty.App.Tests;

/// <summary>
/// The canvas backdrop is a lattice of the art's own pixels.
/// </summary>
/// <remarks>
/// <para>It was a fixed 18px checker tiled from the canvas PANE's corner, so its squares stood in no
/// relation to the pixels drawn on top of them: a pixel covered part of one square and part of the
/// next, and how much changed with the canvas size and with where the tile happened to be centered.
/// Reported as the grid being "off-center from actual pixels", which is exactly what it was.</para>
///
/// <para>Two things have to be true for it to be a pixel grid, and only one of them is the size: a
/// square must be <c>GridSize</c> canvas pixels wide, AND the lattice must be in PHASE with the
/// art's top-left corner. A brush of the right size tiled from the wrong origin is the same bug with
/// better arithmetic — so the phase is what most of these measure.</para>
/// </remarks>
public class CanvasGridTests
{
    private static (Window Window, IngredientEditorViewModel Vm, Views.IngredientEditorView View)
        Render(int canvas = 8, double width = 1180, double height = 720)
    {
        var (book, recipe, ing) = Fixture(canvas);
        var vm = new IngredientEditorViewModel(ing, recipe, book, new ImageBridge(), new FakeNav(),
            new CookBookSession(), new FakeDialogs(), new FilePickerService());
        var view = new Views.IngredientEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = width, Height = height };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, view);
    }

    /// <summary>A dynamic layer at a chosen canvas size — the size is the whole point here, so it
    /// cannot come from the shared 8x8 fixture.</summary>
    private static (LoadedCookBook, LoadedRecipe, LoadedIngredient) Fixture(int canvas)
    {
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("aura", "Aura", LayerKind.Dynamic,
                new Colorization(ColorModel.Hsv, 12, 4,
                    new[] { new ColorEntry(1, new ColorRange(190, 320, 55, 95), null) }),
                new[] { new Variant("glow", "Glow", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["glow"] = new(canvas, canvas) },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "aura" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(canvas, canvas),
                new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 }),
            Recipes = new[] { recipe },
        };
        return (book, recipe, ing);
    }

    private static Panel Backdrop(Visual view) =>
        view.GetVisualDescendants().OfType<Panel>().First(p => p.Name == "CanvasBackdrop");

    private static Image Art(Visual view) =>
        view.GetVisualDescendants().OfType<Image>().First(i => i.Name == "CanvasImage");

    /// <summary>The art's top-left corner in the backdrop's own coordinates, worked out the way the
    /// pointer mapping does: the letterbox a <c>Stretch="Uniform"</c> image leaves inside its box.</summary>
    private static (Point Origin, double Scale) ArtGeometry(Visual view)
    {
        var art = Art(view);
        var bmp = (Bitmap)art.Source!;
        double scale = Math.Min(art.Bounds.Width / bmp.PixelSize.Width,
                                art.Bounds.Height / bmp.PixelSize.Height);
        double offX = (art.Bounds.Width - bmp.PixelSize.Width * scale) / 2;
        double offY = (art.Bounds.Height - bmp.PixelSize.Height * scale) / 2;
        return (art.TranslatePoint(new Point(offX, offY), Backdrop(view))!.Value, scale);
    }

    /// <summary>How far the lattice is out of phase with the art's corner, along one axis.</summary>
    private static double PhaseError(double artEdge, double tileStart, double tile)
    {
        double d = (artEdge - tileStart) % tile;
        if (d < 0) d += tile;
        return Math.Min(d, tile - d);
    }

    // ---- the fix ---------------------------------------------------------------------------------

    /// <summary>
    /// A square is one canvas pixel, and the lattice starts on the art's corner.
    /// </summary>
    [AvaloniaFact]
    public void The_backdrop_is_one_square_per_pixel_and_in_phase_with_the_art()
    {
        var (window, vm, view) = Render(canvas: 8);
        try
        {
            var brush = Assert.IsType<DrawingBrush>(Backdrop(view).Background);
            var (origin, scale) = ArtGeometry(view);

            // A checker macro-tile is two squares across, so the square is half of it.
            double tile = brush.DestinationRect.Rect.Width;
            Assert.Equal(scale * vm.GridSize, tile / 2, 3);

            Assert.True(PhaseError(origin.X, brush.DestinationRect.Rect.X, tile) < 0.01,
                $"the lattice is {PhaseError(origin.X, brush.DestinationRect.Rect.X, tile):F2}px out of "
                + $"phase horizontally (art at {origin.X:F2}, tiles from {brush.DestinationRect.Rect.X:F2}, "
                + $"tile {tile:F2})");
            Assert.True(PhaseError(origin.Y, brush.DestinationRect.Rect.Y, tile) < 0.01,
                $"the lattice is {PhaseError(origin.Y, brush.DestinationRect.Rect.Y, tile):F2}px out of "
                + "phase vertically");
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>The step is honoured: eight pixels to a square means squares eight times as wide.</summary>
    [AvaloniaFact]
    public void Raising_the_step_widens_the_square_by_exactly_that_many_pixels()
    {
        var (window, vm, view) = Render(canvas: 32);
        try
        {
            double one = ((DrawingBrush)Backdrop(view).Background!).DestinationRect.Rect.Width;

            vm.GridSize = 8;
            Dispatcher.UIThread.RunJobs();
            var brush = Assert.IsType<DrawingBrush>(Backdrop(view).Background);

            Assert.Equal(one * 8, brush.DestinationRect.Rect.Width, 3);

            // And still in phase - a wider square that starts in the wrong place is the same bug.
            var (origin, _) = ArtGeometry(view);
            double tile = brush.DestinationRect.Rect.Width;
            Assert.True(PhaseError(origin.X, brush.DestinationRect.Rect.X, tile) < 0.01);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>The step cannot be set past what the canvas holds, or below one square per pixel.
    /// The ceiling belongs to the PROPERTY, the way the brush size's does.</summary>
    [AvaloniaFact]
    public void The_step_is_clamped_to_the_canvas()
    {
        var (window, vm, _) = Render(canvas: 16);
        try
        {
            Assert.Equal(16, vm.GridSizeMax);

            vm.GridSize = 999;
            Assert.Equal(16, vm.GridSize);

            vm.GridSize = 0;
            Assert.Equal(1, vm.GridSize);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    // ---- the flat ground -------------------------------------------------------------------------

    /// <summary>Turning the grid off paints the theme's own ground rather than a lattice.</summary>
    [AvaloniaFact]
    public void Turning_the_grid_off_paints_a_flat_ground()
    {
        var (window, vm, view) = Render();
        try
        {
            Assert.IsType<DrawingBrush>(Backdrop(view).Background);

            vm.ToggleGridCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.False(vm.ShowPixelGrid);
            var flat = Assert.IsAssignableFrom<ISolidColorBrush>(Backdrop(view).Background);
            Assert.NotEqual(default, flat.Color);

            vm.ToggleGridCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.IsType<DrawingBrush>(Backdrop(view).Background);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>
    /// A square finer than two device pixels is not a square, so the flat ground is drawn instead.
    /// </summary>
    /// <remarks>
    /// Half a lattice that fine averages to a flat tone and the other half aliases into a moire that
    /// crawls as the window moves — and what it averages to IS the flat ground, so drawing that is
    /// the honest version of the same picture. A 512px canvas in a 320px tile puts a pixel at 0.62
    /// device pixels, which is the case the step control exists for.
    /// </remarks>
    [AvaloniaFact]
    public void A_canvas_too_fine_to_draw_a_lattice_for_gets_the_flat_ground()
    {
        var (window, vm, view) = Render(canvas: 512);
        try
        {
            Assert.True(vm.ShowPixelGrid);                       // the grid is still ON
            Assert.IsAssignableFrom<ISolidColorBrush>(Backdrop(view).Background);

            // Raising the step past the floor brings the lattice back, which is what it is for.
            vm.GridSize = 8;
            Dispatcher.UIThread.RunJobs();
            var brush = Assert.IsType<DrawingBrush>(Backdrop(view).Background);
            Assert.True(brush.DestinationRect.Rect.Width / 2 >= 2);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    // ---- it is view state, not the layer ----------------------------------------------------------

    /// <summary>
    /// The grid is how the canvas is DRAWN, not what the layer holds — so touching it must not
    /// offer to save anything.
    /// </summary>
    /// <remarks>
    /// The colorize rail's controls are the opposite case and had the opposite bug: the layer kind
    /// was part of the layer and did NOT mark the editor dirty, so it could not be saved at all.
    /// Both directions are worth pinning.
    /// </remarks>
    [AvaloniaFact]
    public void The_grid_is_view_state_and_never_dirties_the_layer()
    {
        var (window, vm, _) = Render();
        try
        {
            Assert.False(vm.IsDirty);

            vm.GridSize = 4;
            vm.ToggleGridCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.False(vm.IsDirty);
            Assert.False(vm.SaveCommand.CanExecute(null));
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>Both controls are reachable from the canvas pane rather than from either strip —
    /// the two strips already wrap at the window sizes this app allows.</summary>
    [AvaloniaFact]
    public void The_grid_controls_sit_on_the_canvas_and_not_in_a_strip()
    {
        var (window, vm, view) = Render();
        try
        {
            var chip = view.GetVisualDescendants().OfType<Border>()
                .FirstOrDefault(b => b.Classes.Contains("gridchip"));
            Assert.NotNull(chip);

            var backdrop = Backdrop(view);
            Assert.Contains(chip!.GetVisualAncestors(), a => ReferenceEquals(a, backdrop));

            // Inside the pane, and not overlapping the blip it is paired with.
            var blip = view.GetVisualDescendants().OfType<Border>()
                .First(b => b.Name == "PreviewTile");
            var chipRight = chip.TranslatePoint(new Point(chip.Bounds.Width, 0), backdrop)!.Value.X;
            var blipLeft = blip.TranslatePoint(new Point(0, 0), backdrop)!.Value.X;
            Assert.True(chipRight < blipLeft,
                $"the grid chip ends at {chipRight:F0} and the blip starts at {blipLeft:F0}");
        }
        finally { window.Close(); vm.Dispose(); }
    }
}
