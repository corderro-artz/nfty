using System.IO.Compression;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>The Kitchen: the top-level workspace, a .ktn file that names the folder it sits in.
///
/// The design decision most of these pin is that membership is **discovered, not recorded**. The
/// manifest carries identity only; what the Kitchen contains is whatever is in its folder, read at
/// open. A recorded list would go stale the moment a file was renamed or moved outside the app, and
/// the Kitchen would then describe a workspace that no longer exists — so the tests that matter most
/// here are the ones that change the folder behind the Kitchen's back and check it keeps up.</summary>
public class KitchenTests
{
    private static string NewDir() => Directory.CreateTempSubdirectory().FullName;

    private static KitchenManifest Manifest(string name = "VaporStudio") =>
        new(name.ToLowerInvariant(), name, "a workspace");

    private static string MakeKitchen(string dir, string name = "VaporStudio")
    {
        var path = Path.Combine(dir, name + Kitchen.Extension);
        Kitchen.Create(path, Manifest(name));
        return path;
    }

    /// <summary>A real .cbk, so the scan is finding archives rather than any file with the right name.</summary>
    private static void PutCookBook(string dir, string id)
    {
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "BG", LayerKind.Custom, null,
                new[] { new Variant("a", "A", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["a"] = new Image<Rgba32>(2, 2) },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "bg" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        using var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest(id, id, new Dimensions(2, 2),
                new Collection(id, "", "X"), new Dictionary<string, double> { ["cat"] = 1 }),
            Recipes = new[] { recipe },
        };
        CookBookArchive.Write(Path.Combine(dir, id + Archives.CookBookExtension), book.Manifest, book.Recipes);
    }

    private static void PutIngredient(string dir, string id)
    {
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest(id, id, LayerKind.Custom, null,
                new[] { new Variant("a", "A", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["a"] = new Image<Rgba32>(2, 2) },
        };
        using (ing)
            IngredientArchive.Write(Path.Combine(dir, id + Archives.IngredientExtension),
                ing.Manifest, ing.VariantImages);
    }

    private static void PutRecipe(string dir, string id)
    {
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "BG", LayerKind.Custom, null,
                new[] { new Variant("a", "A", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["a"] = new Image<Rgba32>(2, 2) },
        };
        using (ing)
            RecipeArchive.Write(Path.Combine(dir, id + Archives.RecipeExtension),
                new RecipeManifest(id, id, new[] { "bg" }, Array.Empty<IncompatibilityRule>()),
                new[] { ing });
    }

    // ---- the archive itself ---------------------------------------------------------------------

    [Fact]
    public void A_kitchen_round_trips_through_its_archive()
    {
        var dir = NewDir();
        var path = Path.Combine(dir, "studio.ktn");
        Kitchen.Create(path, new KitchenManifest("studio", "VaporStudio", "where the work happens"));

        var read = KitchenArchive.Read(path);

        Assert.Equal("studio", read.Id);
        Assert.Equal("VaporStudio", read.Name);
        Assert.Equal("where the work happens", read.Description);
        Assert.Equal(Schema.Current, read.SchemaVersion);
    }

    [Fact]
    public async Task The_async_pair_reads_what_the_async_writer_wrote()
    {
        var path = Path.Combine(NewDir(), "studio.ktn");
        await KitchenArchive.WriteAsync(path, Manifest());

        var read = await KitchenArchive.ReadAsync(path);
        Assert.Equal("VaporStudio", read.Name);
    }

    [Fact]
    public void Sync_and_async_writers_produce_the_same_manifest()
    {
        var a = Path.Combine(NewDir(), "a.ktn");
        var b = Path.Combine(NewDir(), "b.ktn");
        KitchenArchive.Write(a, Manifest());
        KitchenArchive.WriteAsync(b, Manifest()).GetAwaiter().GetResult();

        Assert.Equal(KitchenArchive.Read(a), KitchenArchive.Read(b));
    }

    [Fact]
    public void A_kitchen_is_a_zip_with_a_manifest_like_every_other_archive()
    {
        var path = Path.Combine(NewDir(), "studio.ktn");
        Kitchen.Create(path, Manifest());

        using var zip = ZipFile.OpenRead(path);
        Assert.NotNull(zip.GetEntry("manifest.json"));
    }

    [Fact]
    public void A_future_schema_version_is_refused_like_every_other_archive()
    {
        var path = Path.Combine(NewDir(), "future.ktn");
        KitchenArchive.Write(path, Manifest() with { SchemaVersion = Schema.Current + 1 });

        var ex = Assert.Throws<UnsupportedSchemaVersionException>(() => KitchenArchive.Read(path));
        Assert.Equal(Schema.Current + 1, ex.Found);
    }

    [Fact]
    public void Create_makes_the_folder_when_it_is_missing()
    {
        // The folder IS the workspace, so a .ktn without one is not a meaningful state. The
        // creation-flows spec calls the Kitchen "a predetermined workspace folder, created if absent".
        var nested = Path.Combine(NewDir(), "does", "not", "exist");
        var path = Path.Combine(nested, "studio.ktn");

        Kitchen.Create(path, Manifest());

        Assert.True(Directory.Exists(nested));
        Assert.True(File.Exists(path));
    }

    // ---- membership is DISCOVERED ---------------------------------------------------------------

    [Fact]
    public void A_new_kitchen_is_empty_rather_than_broken()
    {
        var path = MakeKitchen(NewDir());
        var contents = Kitchen.Open(path);

        Assert.True(contents.IsEmpty);
        Assert.Equal(0, contents.ItemCount);
        Assert.Empty(contents.CookBooks);
        Assert.Empty(contents.Recipes);
        Assert.Empty(contents.Ingredients);
    }

    [Fact]
    public void It_lists_the_cookbooks_recipes_and_ingredients_beside_it()
    {
        var dir = NewDir();
        var path = MakeKitchen(dir);
        PutCookBook(dir, "vaporpets");
        PutRecipe(dir, "fox");
        PutIngredient(dir, "aura");
        PutIngredient(dir, "body");

        var contents = Kitchen.Open(path);

        Assert.Single(contents.CookBooks);
        Assert.Single(contents.Recipes);
        Assert.Equal(2, contents.Ingredients.Count);
        Assert.Equal(4, contents.ItemCount);
        Assert.False(contents.IsEmpty);
        Assert.Equal(dir, contents.Directory);
    }

    /// <summary>The point of discovering rather than recording. A file added by any other means —
    /// the OS, another tool, a previous run — is simply there next time.</summary>
    [Fact]
    public void A_file_added_behind_the_kitchens_back_appears_on_the_next_open()
    {
        var dir = NewDir();
        var path = MakeKitchen(dir);
        Assert.True(Kitchen.Open(path).IsEmpty);

        PutIngredient(dir, "aura");

        Assert.Single(Kitchen.Open(path).Ingredients);
    }

    /// <summary>And the converse, which a recorded list gets wrong: a file deleted outside the app
    /// must not linger as a phantom member.</summary>
    [Fact]
    public void A_file_removed_behind_the_kitchens_back_disappears_on_the_next_open()
    {
        var dir = NewDir();
        var path = MakeKitchen(dir);
        PutIngredient(dir, "aura");
        Assert.Single(Kitchen.Open(path).Ingredients);

        File.Delete(Path.Combine(dir, "aura.igt"));

        Assert.Empty(Kitchen.Open(path).Ingredients);
    }

    [Fact]
    public void The_manifest_records_no_membership_at_all()
    {
        // If a member list is ever added to the manifest, the two staleness tests above stop being
        // the whole story and this is the reminder to think about reconciliation.
        var dir = NewDir();
        var path = MakeKitchen(dir);
        PutCookBook(dir, "vaporpets");

        var manifest = KitchenArchive.Read(path);
        var props = manifest.GetType().GetProperties().Select(p => p.Name).ToList();

        Assert.Equal(new[] { "Id", "Name", "Description", "SchemaVersion" }.OrderBy(x => x),
            props.OrderBy(x => x));
    }

    [Fact]
    public void Unrelated_files_are_ignored()
    {
        var dir = NewDir();
        var path = MakeKitchen(dir);
        File.WriteAllText(Path.Combine(dir, "notes.txt"), "hello");
        File.WriteAllText(Path.Combine(dir, "cover.png"), "not really a png");
        Directory.CreateDirectory(Path.Combine(dir, "scratch"));

        var contents = Kitchen.Open(path);
        Assert.True(contents.IsEmpty);
    }

    [Fact]
    public void The_kitchen_file_is_not_listed_as_one_of_its_own_members()
    {
        var dir = NewDir();
        var path = MakeKitchen(dir);
        var contents = Kitchen.Open(path);

        Assert.DoesNotContain(path, contents.CookBooks);
        Assert.DoesNotContain(path, contents.Recipes);
        Assert.DoesNotContain(path, contents.Ingredients);
    }

    /// <summary>Only the immediate folder. Recursing would make a Kitchen opened high in a tree
    /// swallow everything beneath it, and nested Kitchens are explicitly out of scope.</summary>
    [Fact]
    public void A_subfolders_contents_belong_to_the_subfolder_not_to_this_kitchen()
    {
        var dir = NewDir();
        var path = MakeKitchen(dir);
        var sub = Path.Combine(dir, "nested");
        Directory.CreateDirectory(sub);
        PutIngredient(sub, "deep");

        Assert.True(Kitchen.Open(path).IsEmpty);
    }

    [Fact]
    public void Listings_are_ordinally_sorted_so_they_do_not_reorder_by_locale()
    {
        var dir = NewDir();
        var path = MakeKitchen(dir);
        foreach (var id in new[] { "zeta", "Alpha", "beta" }) PutIngredient(dir, id);

        var listed = Kitchen.Open(path).Ingredients;
        Assert.Equal(listed.OrderBy(p => p, StringComparer.Ordinal), listed);
    }

    [Fact]
    public async Task Open_and_OpenAsync_agree()
    {
        var dir = NewDir();
        var path = MakeKitchen(dir);
        PutCookBook(dir, "vaporpets");
        PutIngredient(dir, "aura");

        var sync = Kitchen.Open(path);
        var async = await Kitchen.OpenAsync(path);

        Assert.Equal(sync.Manifest, async.Manifest);
        Assert.Equal(sync.CookBooks, async.CookBooks);
        Assert.Equal(sync.Ingredients, async.Ingredients);
        Assert.Equal(sync.ItemCount, async.ItemCount);
    }

    /// <summary>Listing returns PATHS, so opening a workspace does not decode every PNG in it.</summary>
    [Fact]
    public void Listing_does_not_load_the_archives()
    {
        var dir = NewDir();
        var path = MakeKitchen(dir);
        PutCookBook(dir, "vaporpets");

        var contents = Kitchen.Open(path);

        Assert.All(contents.CookBooks, p => Assert.True(File.Exists(p)));
        Assert.IsType<string>(Assert.Single(contents.CookBooks));
    }

    // ---- finding the Kitchen for a folder --------------------------------------------------------

    [Fact]
    public void FindIn_returns_the_single_kitchen_in_a_folder()
    {
        var dir = NewDir();
        var path = MakeKitchen(dir);
        Assert.Equal(path, Kitchen.FindIn(dir));
    }

    [Fact]
    public void FindIn_returns_null_for_a_plain_folder()
    {
        Assert.Null(Kitchen.FindIn(NewDir()));
    }

    [Fact]
    public void FindIn_returns_null_for_a_folder_that_does_not_exist()
    {
        Assert.Null(Kitchen.FindIn(Path.Combine(NewDir(), "nope")));
    }

    /// <summary>Two .ktn files in one folder is ambiguous, not "first one wins": picking silently
    /// would give two Kitchens identical contents and neither would be wrong.</summary>
    [Fact]
    public void FindIn_refuses_to_guess_between_two_kitchens()
    {
        var dir = NewDir();
        MakeKitchen(dir, "One");
        MakeKitchen(dir, "Two");

        Assert.Null(Kitchen.FindIn(dir));
    }

    // ---- extension registry ----------------------------------------------------------------------

    [Fact]
    public void The_extension_resolves_to_the_kitchen_kind()
    {
        Assert.Equal(ArchiveKind.Kitchen, Archives.KindOf("a/b/studio.ktn"));
        Assert.Equal(ArchiveKind.Kitchen, Archives.KindOf("STUDIO.KTN"));   // case-insensitive, as the others are
    }

    [Fact]
    public void The_unknown_extension_message_now_offers_ktn_too()
    {
        var ex = Assert.Throws<NotSupportedException>(() => Archives.KindOf("thing.zip"));
        Assert.Contains(Kitchen.Extension, ex.Message);
    }

    [Fact]
    public void The_extension_constants_agree()
    {
        // Two names for one string is exactly how they drift apart.
        Assert.Equal(Archives.KitchenExtension, Kitchen.Extension);
    }
}
