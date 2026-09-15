using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Globalization;
using System.IO;
using Avalonia.Media;
using Nfty.App.Converters;
using Xunit;
using Point = Avalonia.Point;
// Both System.IO and Avalonia.Controls.Shapes define Path; this file measures a glyph.
using Path = Avalonia.Controls.Shapes.Path;

namespace Nfty.App.Tests;

/// <summary>
/// The recipe portrait's reroll: a centred glyph, at the size its icon class says.
/// </summary>
/// <remarks>
/// <para>It used to read "sample 1" beside the die — a label printing the reroll COUNT, which is not
/// a fact about the recipe and invites being read as a stored sample id. Icon only now, centred
/// under the picture it rolls.</para>
///
/// <para>The first cut of that rotated the <c>Path</c> itself for its click animation, which
/// REPLACED the icon system's own transform: every glyph here is authored in a 24-unit box and
/// mapped onto its size class by <c>scale(size/24)</c>, so overriding <c>RenderTransform</c> drew
/// the die at raw 24 units inside an 18px box — overflowing down and right, and reading from a frame
/// as a glyph nudged off centre. Reported exactly that way. The rotation lives on a wrapper Panel
/// now; these are the two assertions that keep it there.</para>
/// </remarks>
public class RerollButtonTests
{
    private static LoadedCookBook Book()
    {
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "BG", LayerKind.Custom, null,
                new[] { new Variant("a", "A", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["a"] = new(8, 8) },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "bg" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(8, 8),
                new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 }),
            Recipes = new[] { recipe },
        };
    }

    private static (Window Window, Views.RecipeDetailView View, RecipeDetailViewModel Vm) Show()
    {
        var book = Book();
        var vm = new RecipeDetailViewModel(book.Recipes[0], book, new ImageBridge(), _ => { }, null, false);
        var view = new Views.RecipeDetailView { DataContext = vm };
        var window = new Window { Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view, vm);
    }

    private static Button Reroll(Visual view) => view.GetVisualDescendants()
        .OfType<Button>().First(b => b.Classes.Contains("sampleroll"));

    private static double CentreX(Visual c, Visual root) =>
        c.TranslatePoint(new Point(c.Bounds.Width / 2, 0), root)?.X ?? double.NaN;

    /// <summary>
    /// THE DIE SITS ON THE PORTRAIT'S OWN CENTRE LINE. Reported from a frame as looking "off centre
    /// from the image canvas above it".
    /// </summary>
    [AvaloniaFact]
    public void The_reroll_is_centred_under_the_portrait()
    {
        var (window, view, vm) = Show();
        try
        {
            var button = Reroll(view);
            // The portrait: the 92px tile the sample is drawn into, above this button.
            var portrait = view.GetVisualDescendants().OfType<Border>()
                .First(b => Math.Abs(b.Bounds.Width - 92) < 0.5 && Math.Abs(b.Bounds.Height - 92) < 0.5);

            Assert.Equal(CentreX(portrait, view), CentreX(button, view), precision: 0);
        }
        finally { vm.Dispose(); window.Close(); }
    }

    /// <summary>
    /// AND THE GLYPH IS STILL AT ITS ICON SIZE. This is the assertion the centring one cannot make:
    /// replacing a <c>Path.ico</c>'s RenderTransform loses the 24-unit-box scale and draws the glyph
    /// oversized, while its LAYOUT box — and therefore its centre — does not move at all. Measured on
    /// the transform rather than on the bounds for exactly that reason.
    /// </summary>
    [AvaloniaFact]
    public void The_die_keeps_the_icon_systems_own_scale()
    {
        var (window, view, vm) = Show();
        try
        {
            var path = Reroll(view).GetVisualDescendants().OfType<Path>()
                .First(p => p.Classes.Contains("ico"));

            Assert.Contains("ti", path.Classes);                 // the 18px size class
            var m = path.RenderTransform?.Value ?? Matrix.Identity;
            // 18/24. A rotation put here instead would report 1 — which is the bug this names.
            Assert.Equal(0.75, m.M11, precision: 2);
            Assert.Equal(0.75, m.M22, precision: 2);

            // The rotation the click animation uses lives on the wrapper, where it costs nothing.
            var wrapper = Reroll(view).GetVisualDescendants().OfType<Panel>()
                .First(p => p.Classes.Contains("dieroll"));
            Assert.NotNull(wrapper.Transitions);
        }
        finally { vm.Dispose(); window.Close(); }
    }

    /// <summary>
    /// THE DIE LANDS ON A NEW FACE EVERY PRESS, and never the one it is already showing.
    /// </summary>
    /// <remarks>
    /// The face is the ONLY confirmation this button gives — the picture it rerolls can come back
    /// looking much like the last one, since a recipe with few variants rolls few pictures. A fair
    /// d6 repeats about one press in six, and a glyph that did not change reads as a button that did
    /// not work, so the five it is not showing are what it draws from. Rolled two hundred times
    /// here, which makes a repeat a certainty rather than a coin flip if the guard is removed.
    /// </remarks>
    [AvaloniaFact]
    public async System.Threading.Tasks.Task Every_reroll_lands_on_a_face_it_was_not_showing()
    {
        var (window, view, vm) = Show();
        try
        {
            var seen = new HashSet<int>();
            int previous = vm.DieFace;
            Assert.InRange(previous, 1, DieFaceConverter.Faces);

            for (int i = 0; i < 200; i++)
            {
                await vm.RerollCommand.ExecuteAsync(null);
                Assert.InRange(vm.DieFace, 1, DieFaceConverter.Faces);
                Assert.NotEqual(previous, vm.DieFace);
                seen.Add(vm.DieFace);
                previous = vm.DieFace;
            }

            // And it is a die rather than a two-state toggle: every face comes up.
            Assert.Equal(DieFaceConverter.Faces, seen.Count);
        }
        finally { vm.Dispose(); window.Close(); }
    }

    /// <summary>Each face resolves to a real glyph, and a value outside the die falls back rather
    /// than throwing — a decoration with no geometry should draw something, not take a screen down.</summary>
    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(0)]
    [InlineData(99)]
    public void Every_face_resolves_to_a_glyph(int face)
    {
        var geometry = DieFaceConverter.Instance.Convert(
            face, typeof(Geometry), null, CultureInfo.InvariantCulture);

        var g = Assert.IsAssignableFrom<Geometry>(geometry);
        Assert.True(g.Bounds.Width > 0 && g.Bounds.Height > 0, "the glyph resolved but draws nothing");
    }

    /// <summary>
    /// The six are six DIFFERENT drawings, and face N really carries N pips.
    /// </summary>
    /// <remarks>
    /// Measured on the SVG sources rather than on the resolved geometry, because a
    /// <see cref="StreamGeometry"/> does not hand its path data back — <c>ToString</c> returns the
    /// type name, so comparing the six that way reports one distinct glyph however they are drawn
    /// (it did). <c>IconSourceTests</c> already proves the SVGs and the generated Icons.axaml say
    /// the same thing, so a check on the drawings is a check on the glyphs. A pip is one arc pair,
    /// past the body's own single lowercase arc.
    /// </remarks>
    [AvaloniaFact]
    public void Face_n_carries_n_pips_and_the_six_are_distinct()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(System.IO.Path.Combine(dir.FullName, "nfty.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        string Drawing(string file)
        {
            var svg = File.ReadAllText(System.IO.Path.Combine(dir!.FullName, "assets", "icons", file));
            int open = svg.IndexOf("<path d=\"", StringComparison.Ordinal) + 9;
            return svg[open..svg.IndexOf('"', open)];
        }
        static int Pips(string d) => (d.Split('a').Length - 1 - BodyArcs) / ArcsPerPip;

        var drawings = new List<string>();
        for (int n = 1; n <= DieFaceConverter.Faces; n++)
        {
            var d = Drawing($"die-{n}.svg");
            drawings.Add(d);
            // THE ONE FACE IS THE EXCEPTION AND IT IS DELIBERATE: its body carries no pip at all,
            // because the pip is a second drawing stroked in the accent over it. A Path.ico is one
            // stroked path with one brush, so the red pip cannot live in the same geometry.
            Assert.Equal(n == 1 ? 0 : n, Pips(d));
        }

        // And that separate pip is exactly one pip, on the same centre as every other.
        var accentPip = Drawing("die-1-pip.svg");
        Assert.Equal(1, (accentPip.Split('a').Length - 1) / ArcsPerPip);
        Assert.Contains("M12.8 12", accentPip, StringComparison.Ordinal);

        Assert.Equal(DieFaceConverter.Faces, drawings.Distinct(StringComparer.Ordinal).Count());

        // And the resolved resources are six distinct objects, not one shared glyph.
        var resolved = Enumerable.Range(1, DieFaceConverter.Faces)
            .Select(n => DieFaceConverter.Instance.Convert(n, typeof(Geometry), null,
                CultureInfo.InvariantCulture))
            .ToList();
        Assert.Equal(DieFaceConverter.Faces, resolved.Distinct().Count());
    }

    /// <summary>The shared body's own lowercase arc — its three other corners are written with an
    /// uppercase A — so a pip count can be taken off the rest.</summary>
    private const int BodyArcs = 1;

    /// <summary>A pip is a circle, and a StreamGeometry expresses none: it is two half arcs.</summary>
    private const int ArcsPerPip = 2;

    /// <summary>
    /// THE ONE FACE'S PIP IS RED, in both themes, and no other face has one.
    /// </summary>
    /// <remarks>
    /// The body's own ink says nothing — it is white in dark and black in light. The accent is what
    /// gives the die the character a die has, and it is a SECOND Path because a <c>Path.ico</c> is
    /// one stroked path with one brush.
    /// </remarks>
    [AvaloniaFact]
    public void Only_the_one_face_draws_an_accent_pip()
    {
        var (window, view, vm) = Show();
        try
        {
            var pip = Reroll(view).GetVisualDescendants().OfType<Path>()
                .First(p => p.Classes.Contains("pip1"));

            for (int face = 1; face <= DieFaceConverter.Faces; face++)
            {
                vm.DieFace = face;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(face == 1, pip.Data is not null);
            }

            vm.DieFace = 1;
            Dispatcher.UIThread.RunJobs();
            // The accent token, not the body's ink - and it resolves, which is the half a
            // hard-coded hex could not claim in two themes.
            var app = Application.Current!;
            var expected = app.FindResource(app.ActualThemeVariant, "AccentTextBrush");
            Assert.Equal(expected, pip.Stroke);

            var body = Reroll(view).GetVisualDescendants().OfType<Path>()
                .First(p => !p.Classes.Contains("pip1"));
            Assert.NotEqual(pip.Stroke, body.Stroke);
        }
        finally { vm.Dispose(); window.Close(); }
    }

    /// <summary>The glyph's ink stays inside the button it is drawn in — the visible half of the
    /// scale bug, where an un-scaled 24-unit die overflowed a 28x24 box down and to the right.</summary>
    [AvaloniaFact]
    public void The_die_fits_inside_its_button()
    {
        var (window, view, vm) = Show();
        try
        {
            var button = Reroll(view);
            var path = button.GetVisualDescendants().OfType<Path>().First(p => p.Classes.Contains("ico"));

            var m = path.RenderTransform?.Value ?? Matrix.Identity;
            // The drawn extent: the geometry's own bounds through the icon transform.
            var drawn = path.Data!.Bounds.TransformToAABB(m);
            var origin = path.TranslatePoint(default, button) ?? default;

            Assert.True(origin.X + drawn.Right <= button.Bounds.Width + 0.5,
                $"the die reaches {origin.X + drawn.Right:0.0} in a {button.Bounds.Width:0}px button");
            Assert.True(origin.Y + drawn.Bottom <= button.Bounds.Height + 0.5,
                $"the die reaches {origin.Y + drawn.Bottom:0.0} in a {button.Bounds.Height:0}px button");
        }
        finally { vm.Dispose(); window.Close(); }
    }
}
