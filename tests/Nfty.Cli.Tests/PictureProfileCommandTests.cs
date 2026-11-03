using System.CommandLine;
using System.Text.Json;
using Nfty.Cli;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Nfty.Core.Tests;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Cli.Tests;

/// <summary>A picture drawn in Display P3 is converted to sRGB on import unless <c>--keep-profile</c>
/// says otherwise, through the same Core rule the Import image form uses.</summary>
public class PictureProfileCommandTests
{
    private static readonly InvocationConfiguration NonThrowing = new() { EnableDefaultExceptionHandler = false };

    private static int Run(params string[] args) =>
        CommandFactory.Build().Parse(args).Invoke(NonThrowing);

    private static string CustomIngredient(string dir, byte[]? profile)
    {
        var manifest = new IngredientManifest("art", "Art", LayerKind.Custom, null, [new Variant("one", "One", 1)]);
        string manifestPath = Path.Combine(dir, "art.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, Json.Options));
        string images = Path.Combine(dir, "img");
        Directory.CreateDirectory(images);
        File.WriteAllBytes(Path.Combine(images, "one.png"), IccProfiles.PngFilled(profile, 4, 4, new Rgba32(200, 150, 100, 255)));
        return manifestPath;
    }

    [Fact]
    public void New_ingredient_converts_a_p3_picture_to_srgb_by_default()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string m = CustomIngredient(tmp.FullName, IccProfiles.DisplayP3);
            string outPath = Path.Combine(tmp.FullName, "art.igt");
            Assert.Equal(0, Run("new", "ingredient", outPath, "--manifest", m, "--images", Path.Combine(tmp.FullName, "img")));
            using var loaded = IngredientArchive.Read(outPath);
            Assert.Equal(new Rgba32(209, 147, 91, 255), loaded.VariantImages["one"][0, 0]);
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Keep_profile_imports_the_pixels_as_stored()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string m = CustomIngredient(tmp.FullName, IccProfiles.DisplayP3);
            string outPath = Path.Combine(tmp.FullName, "art.igt");
            Assert.Equal(0, Run("new", "ingredient", outPath, "--manifest", m, "--images", Path.Combine(tmp.FullName, "img"), "--keep-profile"));
            using var loaded = IngredientArchive.Read(outPath);
            Assert.Equal(new Rgba32(200, 150, 100, 255), loaded.VariantImages["one"][0, 0]);
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Add_variant_converts_and_an_unreadable_picture_is_one_product_line()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string m = CustomIngredient(tmp.FullName, null);
            string igt = Path.Combine(tmp.FullName, "art.igt");
            Assert.Equal(0, Run("new", "ingredient", igt, "--manifest", m, "--images", Path.Combine(tmp.FullName, "img")));

            string p3 = Path.Combine(tmp.FullName, "p3.png");
            File.WriteAllBytes(p3, IccProfiles.PngFilled(IccProfiles.DisplayP3, 4, 4, new Rgba32(200, 150, 100, 255)));
            Assert.Equal(0, Run("add", "variant", igt, "--id", "two", "--weight", "1", "--image", p3));
            using (var loaded = IngredientArchive.Read(igt))
                Assert.Equal(new Rgba32(209, 147, 91, 255), loaded.VariantImages["two"][0, 0]);

            string junk = Path.Combine(tmp.FullName, "junk.png");
            File.WriteAllBytes(junk, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
            var ex = Assert.Throws<PictureUnreadableException>(() =>
                Run("add", "variant", igt, "--id", "three", "--weight", "1", "--image", junk));
            string text = ErrorReport.Format(ex, verbose: false);
            Assert.StartsWith("error: “junk.png” is not a PNG or JPEG picture. Save it as PNG from your drawing program and import that.", text);
            Assert.DoesNotContain("caused by", text);
        }
        finally { tmp.Delete(recursive: true); }
    }
}
