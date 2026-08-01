using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Xunit;

namespace Nfty.App.Tests;

public class CookBookPersistenceTests
{
    private static CookBookManifest EmptyManifest() => new("vp", "VaporPets",
        new Dimensions(64, 64), new Collection("VaporPets", "desc", "VP"),
        new Dictionary<string, double>());

    [AvaloniaFact]
    public void WriteNew_writes_a_readable_empty_cookbook()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "book.cbk");
        try
        {
            CookBookPersistence.WriteNew(path, EmptyManifest(), System.Array.Empty<LoadedRecipe>());
            Assert.False(File.Exists(path + ".tmp"));
            using var reread = CookBookArchive.Read(path);
            Assert.Equal("vp", reread.Manifest.Id);
            Assert.Equal("VaporPets", reread.Manifest.Name);
            Assert.Equal(64, reread.Manifest.Canvas.Width);
            Assert.Equal("VP", reread.Manifest.Collection.Symbol);
            Assert.Empty(reread.Recipes);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public void WriteNew_replaces_an_existing_file()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "book.cbk");
        try
        {
            File.WriteAllText(path, "stale");
            CookBookPersistence.WriteNew(path, EmptyManifest(), System.Array.Empty<LoadedRecipe>());
            using var reread = CookBookArchive.Read(path);   // replaced, not rejected
            Assert.Equal("vp", reread.Manifest.Id);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
    [AvaloniaFact]
    public async Task PersistAsync_writes_the_spliced_book_and_replaces_the_session()
    {
        (var path, var session, _, _) = IngredientEditorSaveTests.OnDisk();
        try
        {
            var book2 = CookBookEdits.RemoveRecipe(session.Current!, "cat");   // any real mutation
            var book3 = await CookBookPersistence.PersistAsync(session, book2);
            Assert.Same(book3, session.Current);                 // session replaced
            Assert.False(File.Exists(path + ".tmp"));            // temp cleaned
            using var reread = CookBookArchive.Read(path);
            Assert.DoesNotContain(reread.Recipes, r => r.Manifest.Id == "cat");
            Assert.Equal(reread.SourceSha256, book3.SourceSha256); // hash matches the written file
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [AvaloniaFact]
    public async Task PersistAsync_throws_without_a_source_path()
    {
        (var path, var session, _, _) = IngredientEditorSaveTests.OnDisk();
        try
        {
            var noSource = new CookBookSession();
            noSource.Open(session.Current!, null);   // no source path
            await Assert.ThrowsAsync<System.InvalidOperationException>(
                () => CookBookPersistence.PersistAsync(noSource, noSource.Current!));
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }
}
