using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>
/// A hand-edited (or otherwise malformed) .igt can carry two variants with the same id.
/// The manifest itself doesn't forbid this — <see cref="Validator"/> catches it for a loaded
/// ingredient — but loading must not die first with the framework's raw
/// "An item with the same key has already been added" message. Both the sync and async
/// readers must reject it, with the same wording, before either reaches
/// <see cref="Validator.Validate"/>.
/// </summary>
public class IngredientArchiveDuplicateVariantIdTests
{
    private static string TempPath(string name) =>
        Path.Combine(Directory.CreateTempSubdirectory().FullName, name);

    // Two variants sharing id "a". The dictionary of variant images only needs one entry for
    // that id, since the fix must reject the manifest before either reader ever tries to
    // build that dictionary.
    private static IngredientManifest DuplicateManifest() => new(
        "bg", "Background", LayerKind.Custom, null,
        new[] { new Variant("a", "A", 1), new Variant("a", "A again", 2) });

    private static IReadOnlyDictionary<string, Image<Rgba32>> OneImage() =>
        new Dictionary<string, Image<Rgba32>> { ["a"] = new Image<Rgba32>(2, 2, new Rgba32(1, 2, 3, 255)) };

    [Fact]
    public void Duplicate_variant_id_is_rejected_by_the_sync_reader_with_a_named_message()
    {
        var path = TempPath("dup.igt");
        IngredientArchive.Write(path, DuplicateManifest(), OneImage());

        var ex = Assert.Throws<InvalidDataException>(() => IngredientArchive.Read(path));

        Assert.Equal(
            "Ingredient 'bg' has duplicate variant id 'a'; every variant must have a unique id.",
            ex.Message);
    }

    [Fact]
    public async Task Duplicate_variant_id_is_rejected_by_the_async_reader_with_the_same_message()
    {
        var path = TempPath("dup.igt");
        IngredientArchive.Write(path, DuplicateManifest(), OneImage());

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => IngredientArchive.ReadAsync(path));

        Assert.Equal(
            "Ingredient 'bg' has duplicate variant id 'a'; every variant must have a unique id.",
            ex.Message);
    }
}
