using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Editing;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using Nfty.Core.Tests;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The Import image form on a picture with an embedded color profile: Display P3 is converted to sRGB
/// with the box on by default, the preview follows the box, a profile nfty can't convert says so in
/// muted ink, and the row is the same height whatever the picture carries.
/// </summary>
public class ImportImageColorProfileTests
{
    private static ImportImageViewModel Form(byte[]? profile, Rgba32 fill, out string dir)
    {
        dir = Directory.CreateTempSubdirectory("nfty-icc-").FullName;
        string picture = Path.Combine(dir, "art.png");
        File.WriteAllBytes(picture, IccProfiles.PngFilled(profile, 4, 4, fill));
        var form = new ImportImageViewModel(new FakeDialogs(), picture, new Dimensions(4, 4), new ImageBridge());
        Assert.Null(form.TryLoad());
        return form;
    }

    private static readonly Rgba32 Ink = new(200, 150, 100, 255);

    [AvaloniaFact]
    public void A_p3_picture_is_converted_by_default_and_kept_when_the_box_is_cleared()
    {
        using var form = Form(IccProfiles.DisplayP3, Ink, out _);
        Assert.True(form.CanConvertProfile);
        Assert.True(form.ConvertToSrgb);
        Assert.Equal("This picture uses Display P3. Convert to sRGB so it looks the same everywhere.", form.ProfileNoteConvertible);
        Assert.Equal("", form.ProfileNoteUnsupported);

        using (var built = form.Build()) Assert.Equal(new Rgba32(209, 147, 91, 255), built.VariantImages["variant-1"][0, 0]);

        var before = form.Preview;
        form.ConvertToSrgb = false;
        Assert.NotSame(before, form.Preview);   // the preview follows the box
        using (var built = form.Build()) Assert.Equal(Ink, built.VariantImages["variant-1"][0, 0]);
    }

    [AvaloniaFact]
    public void On_a_value_map_kind_the_picture_is_converted_before_its_lightness_is_taken()
    {
        using var form = Form(IccProfiles.DisplayP3, Ink, out string dir);
        form.Kind = LayerKind.Dynamic;
        using var built = form.Build();
        using var converted = ImageImport.Load(Path.Combine(dir, "art.png"), convertToSrgb: true, out _);
        using var expected = ImageImport.ToValueMap(converted).ToImage();
        Assert.Equal(expected[0, 0], built.VariantImages["variant-1"][0, 0]);
        using var unconverted = ImageImport.Load(Path.Combine(dir, "art.png"), convertToSrgb: false, out _);
        using var wrongOrder = ImageImport.ToValueMap(unconverted).ToImage();
        Assert.NotEqual(wrongOrder[0, 0], built.VariantImages["variant-1"][0, 0]);
    }

    [AvaloniaFact]
    public void A_profile_nfty_cannot_convert_is_named_and_imported_as_is()
    {
        using var form = Form(IccProfiles.LutOnlyProfile("Fogra39 Coated"), Ink, out _);
        Assert.False(form.CanConvertProfile);
        Assert.Equal(0, form.ConvertBoxOpacity);
        Assert.Equal("Uses Fogra39 Coated; nfty can't convert this profile — imported as is.", form.ProfileNoteUnsupported);
        using var built = form.Build();
        Assert.Equal(Ink, built.VariantImages["variant-1"][0, 0]);
    }

    [AvaloniaFact]
    public void An_unreadable_picture_is_refused_in_the_products_words()
    {
        string dir = Directory.CreateTempSubdirectory("nfty-icc-").FullName;
        string junk = Path.Combine(dir, "junk.png");
        File.WriteAllText(junk, "this is not a picture");
        using var form = new ImportImageViewModel(new FakeDialogs(), junk, new Dimensions(4, 4), new ImageBridge());
        Assert.Equal("“junk.png” is not a PNG or JPEG picture. Save it as PNG from your drawing program and import that.",
            form.TryLoad());

        using var gone = new ImportImageViewModel(new FakeDialogs(), Path.Combine(dir, "gone.png"), new Dimensions(4, 4), new ImageBridge());
        Assert.Equal("“gone.png” is no longer there. Choose the file again.", gone.TryLoad());
    }

    /// <summary>Reserve space, toggle ink: the NAME field sits at the same place with no profile, a
    /// convertible one and an unconvertible one.</summary>
    [AvaloniaFact]
    public void The_profile_row_moves_nothing()
    {
        double? first = null;
        foreach (var profile in new[] { null, IccProfiles.DisplayP3, IccProfiles.LutOnlyProfile("Fogra39 Coated") })
        {
            using var form = Form(profile, Ink, out _);
            var view = new Views.ImportImageView { DataContext = form };
            var window = new Window { Content = view, Width = RisePages.MinArea.Width, Height = RisePages.MinArea.Height };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                var name = view.FindControl<TextBox>("NameField")!;
                double y = name.TranslatePoint(new Point(0, 0), view)!.Value.Y;
                first ??= y;
                Assert.Equal(first.Value, y, 3);
                var box = view.FindControl<CheckBox>("ConvertBox")!;
                Assert.Equal(profile is not null && form.CanConvertProfile, box.IsHitTestVisible);
            }
            finally { window.Close(); }
        }
    }

    [Fact]
    public void The_editor_says_what_it_did_with_the_profile()
    {
        var p3 = PictureProfile.FromProfile(IccProfiles.DisplayP3);
        Assert.Equal("“art.png” uses Display P3; nfty converted it to sRGB so it looks the same everywhere.",
            IngredientEditorViewModel.ProfileNotice(p3, "art.png"));
        Assert.Null(IngredientEditorViewModel.ProfileNotice(PictureProfile.NoProfile, "art.png"));
    }
}
