using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// Both handles of a range band can be dragged.
/// </summary>
/// <remarks>
/// <para>Two Sliders stacked in one Panel are each the full width of the band, so the second one
/// declared covers the first completely. Every press — including one directly on the low handle —
/// hit-tested to the HIGH slider and dragged it to the pointer, and the low handle could not be
/// moved by mouse at all: reported as "only one saturation slider is clickable… the range is stuck
/// with a minimum somewhere around a third of the way", which is the field default of 40 out of
/// 100 nothing could move.</para>
///
/// <para>Driven from the WINDOW, because the whole defect is hit testing — the ViewModel was right
/// the entire time, and any test that set <c>SatMin</c> directly passed while the app could not.
/// A move precedes each press, as a real pointer's does: the hover is what arms a handle.</para>
/// </remarks>
public class DualRangeBandTests
{
    private static (Window Window, IngredientEditorViewModel Vm, Views.IngredientEditorView View) Render()
    {
        var (book, recipe, ing) = VisualCapture.DynamicIngredient();
        var vm = new IngredientEditorViewModel(ing, recipe, book, new ImageBridge(), new FakeNav(),
            new CookBookSession(), new FakeDialogs(), new FilePickerService());
        var view = new Views.IngredientEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, view);
    }

    /// <summary>The band holding the two sliders bound to the given properties.</summary>
    private static Panel Band(Visual view, params double[] values)
    {
        var bands = view.GetVisualDescendants().OfType<Panel>()
            .Where(p => p.Classes.Contains("band") && p.Children.OfType<Slider>().Count() == 2)
            .Where(p => p.Bounds.Width > 0)
            .ToList();
        Assert.NotEmpty(bands);
        var match = bands.FirstOrDefault(p =>
        {
            var s = p.Children.OfType<Slider>().ToArray();
            return Math.Abs(s[0].Value - values[0]) < 0.01 && Math.Abs(s[1].Value - values[1]) < 0.01;
        });
        Assert.NotNull(match);
        return match!;
    }

    /// <summary>A window-space point at the given fraction along a band.</summary>
    private static Point At(Window window, Panel band, double fraction) =>
        band.TranslatePoint(new Point(band.Bounds.Width * fraction, band.Bounds.Height / 2), window)!.Value;

    private static void Drag(Window window, Point from, Point to)
    {
        window.MouseMove(from);                 // the hover is what arms a handle
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(to);
        window.MouseUp(to, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The reported bug: the low handle takes the press when the pointer is nearer it.</summary>
    [AvaloniaFact]
    public void The_low_handle_of_the_saturation_band_can_be_dragged()
    {
        var (window, vm, view) = Render();
        try
        {
            // The fixture's layer is authored 55..95, so the low end of the track is nearer SatMin.
            Assert.Equal(55, vm.SatMin);
            Assert.Equal(95, vm.SatMax);

            var band = Band(view, vm.SatMin, vm.SatMax);
            Drag(window, At(window, band, 0.20), At(window, band, 0.20));

            Assert.True(vm.SatMin < 40, $"the low handle stayed at {vm.SatMin}");
            Assert.Equal(95, vm.SatMax);          // and the high one did not move to the pointer
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>The other half: arming by proximity must not cost the high handle its own drag.</summary>
    [AvaloniaFact]
    public void The_high_handle_still_takes_a_press_nearer_it()
    {
        var (window, vm, view) = Render();
        try
        {
            var band = Band(view, vm.SatMin, vm.SatMax);
            Drag(window, At(window, band, 0.99), At(window, band, 0.99));

            Assert.Equal(55, vm.SatMin);
            Assert.True(vm.SatMax > 95, $"the high handle stayed at {vm.SatMax}");
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>Same control, same bug, on the hue band above it.</summary>
    [AvaloniaFact]
    public void The_low_handle_of_the_hue_band_can_be_dragged()
    {
        var (window, vm, view) = Render();
        try
        {
            Assert.Equal(190, vm.HueMin);
            var band = Band(view, vm.HueMin, vm.HueMax);
            Drag(window, At(window, band, 0.10), At(window, band, 0.10));

            Assert.True(vm.HueMin < 100, $"the low handle stayed at {vm.HueMin}");
            Assert.Equal(320, vm.HueMax);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>
    /// A collapsed range can be opened again.
    /// </summary>
    /// <remarks>
    /// With both handles on one value, proximity alone is a tie — so the side the pointer is on
    /// decides which handle it is trying to pull away. Without that, one of the two directions is a
    /// dead end and a range dragged shut stays shut.
    /// </remarks>
    [AvaloniaFact]
    public void A_range_dragged_shut_can_be_opened_from_either_side()
    {
        var (window, vm, view) = Render();
        try
        {
            vm.SatMin = 50;
            vm.SatMax = 50;
            Dispatcher.UIThread.RunJobs();

            var band = Band(view, 50, 50);
            Drag(window, At(window, band, 0.10), At(window, band, 0.10));
            Assert.True(vm.SatMin < 50, $"pulling left moved nothing; min is {vm.SatMin}");
            Assert.Equal(50, vm.SatMax);

            Drag(window, At(window, band, 0.95), At(window, band, 0.95));
            Assert.True(vm.SatMax > 50, $"pulling right moved nothing; max is {vm.SatMax}");
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>
    /// The range runs ascending whatever is typed into the boxes.
    /// </summary>
    /// <remarks>
    /// <c>Validator</c> refuses an inverted range outright — the roller samples
    /// <c>Min + r*(Max-Min)</c>, so one walks backwards off its axis — and the number boxes beneath
    /// each band wrote whatever they were given. An ingredient authored that way makes every book it
    /// joins fail to validate.
    /// </remarks>
    [AvaloniaFact]
    public void Neither_end_can_be_pushed_past_the_other()
    {
        var (window, vm, _) = Render();
        try
        {
            vm.SatMin = 99;                       // above SatMax (95)
            Assert.Equal(95, vm.SatMin);

            vm.SatMax = 3;                        // below SatMin
            Assert.Equal(95, vm.SatMax);

            vm.HueMin = 359;                      // above HueMax (320)
            Assert.Equal(320, vm.HueMin);

            vm.HueMax = 1;
            Assert.Equal(320, vm.HueMax);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>
    /// The New Ingredient wizard carries the same pair of bands and had the same bug.
    /// </summary>
    [AvaloniaFact]
    public void The_wizards_low_handle_can_be_dragged_too()
    {
        var vm = new NewIngredientViewModel(new FakeDialogs());
        var view = new Views.NewIngredientView { DataContext = vm };
        var window = new Window { Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.Equal(40, vm.SatMin);          // the "stuck at about a third" default
            var band = Band(view, vm.SatMin, vm.SatMax);
            Drag(window, At(window, band, 0.10), At(window, band, 0.10));

            Assert.True(vm.SatMin < 30, $"the low handle stayed at {vm.SatMin}");
            Assert.Equal(100, vm.SatMax);
        }
        finally { window.Close(); }
    }
}
