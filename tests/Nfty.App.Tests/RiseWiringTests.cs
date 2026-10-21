using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The Rise page end to end: a stroke on the canvas, through the Explorer's edit lock, through the
/// page's Save, into the <c>.cbk</c> on disk, and out again from a fresh read of that file.
///
/// <para>Same shape and same reason as <c>OptionalLayerJourneyTests</c>: the unit tests cover the surface,
/// the history, the source and the persistence one at a time, and none of them answers whether pressing
/// the thing does the thing. The last assertion reads the file, because a depth that reached the
/// ViewModel and never the archive passes every one of them.</para>
/// </summary>
public class RiseWiringTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "nfty.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void Every_rise_command_is_bound_in_the_rise_view()
    {
        var root = RepoRoot();
        var markup = Regex.Replace(File.ReadAllText(Path.Combine(root, "src/Nfty.App/Views/RiseView.axaml")),
            "<!--.*?-->", "", RegexOptions.Singleline);
        var code = File.ReadAllText(Path.Combine(root, "src/Nfty.App/Views/RiseView.axaml.cs"));
        var missing = typeof(RiseViewModel).GetProperties()
            .Where(p => typeof(ICommand).IsAssignableFrom(p.PropertyType))
            .Select(p => p.Name)
            .Where(n => !Regex.IsMatch(markup, $@"\b{n}\b") && !Regex.IsMatch(code, $@"\b{n}\s*\.\s*Execute"))
            .ToList();
        Assert.True(missing.Count == 0, "Rise commands nothing on the Rise page invokes: " + string.Join(", ", missing));
    }

    private static ExplorerViewModel ExplorerOver(CookBookSession session)
    {
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        return new ExplorerViewModel(session.Current!, nav, dialogs, new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
            ExplorerViewModelTests.CookFactory(dialogs), session, new FilePickerService(),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), new StatusService());
    }

    private static (int Front, int Back) StoredOak(string path, int x, int y)
    {
        using var book = CookBookArchive.Read(path);
        var body = book.Recipes[0].Ingredients.Single(i => i.Manifest.Id == "body");
        return body.Depths.TryGetValue("oak", out var m) ? (m.Front(x, y), m.Back(x, y)) : (0, 0);
    }

    [AvaloniaFact]
    public async Task Depth_painted_on_the_page_reaches_the_archive_only_once_the_lock_is_open()
    {
        var (session, path) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
        try
        {
            using var explorer = ExplorerOver(session);
            var src = RiseFixtures.Variant(session, "oak", () => explorer.IsEditing);
            using var vm = RisePages.Vm(src);
            using var page = RisePages.Show(vm, 1180, 720);

            // Locked: the page paints (it is also how you look), but Save is refused and nothing lands.
            Assert.False(explorer.IsEditing);
            vm.ApplyDepthStroke(new[] { (2, 2) }, DepthSide.Front, false);
            Assert.True(vm.HasUnsavedDepth);
            Assert.False(vm.SaveCommand.CanExecute(null));
            await vm.SaveCommand.ExecuteAsync(null);
            Assert.Equal((0, 0), StoredOak(path, 2, 2));

            // The Explorer's lock opens; the page is told, as the Explorer will tell it.
            explorer.IsEditing = true;
            src.NotifyLockChanged();
            vm.ApplyDepthStroke(new[] { (3, 2) }, DepthSide.Back, false);
            Assert.True(vm.SaveCommand.CanExecute(null));
            await vm.SaveCommand.ExecuteAsync(null);
            Assert.False(vm.HasUnsavedDepth);

            // On disk, which is the only claim that matters: both strokes, front one and back one.
            Assert.Equal((1, 0), StoredOak(path, 2, 2));
            Assert.Equal((0, 1), StoredOak(path, 3, 2));
            // And the untouched variant gained nothing.
            using var reread = CookBookArchive.Read(path);
            var body = reread.Recipes[0].Ingredients.Single(i => i.Manifest.Id == "body");
            Assert.DoesNotContain("ghost", body.Depths.Keys);
            // The book still validates after a depth-only save.
            Assert.Empty(Validator.Validate(reread));
        }
        finally { session.Dispose(); }
    }
}
