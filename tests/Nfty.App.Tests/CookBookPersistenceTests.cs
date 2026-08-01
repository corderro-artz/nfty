using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Xunit;

namespace Nfty.App.Tests;

public class CookBookPersistenceTests
{
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
