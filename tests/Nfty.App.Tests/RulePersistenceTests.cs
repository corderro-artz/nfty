using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// Rule authoring all the way to the FILE. Everything else about the feature is covered a level up —
/// the dialog builds a rule, the pane hands an edit to a seam — and none of it proves a byte ever
/// reached disk. This does, and it also pins the gate, because the gate is what guards the file
/// rather than the button.
/// </summary>
public class RulePersistenceTests
{
    private static LoadedIngredient Ing(string id, params string[] variants) => new()
    {
        Manifest = new IngredientManifest(id, id.ToUpperInvariant(), LayerKind.Custom, null,
            variants.Select(v => new Variant(v, v, 1)).ToArray()),
        VariantImages = variants.ToDictionary(v => v, _ => new Image<Rgba32>(8, 8)),
    };

    private static LoadedCookBook MemoryBook() => new()
    {
        Manifest = new CookBookManifest("cb", "Book", new Dimensions(8, 8),
            new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 }),
        Recipes = new[]
        {
            new LoadedRecipe
            {
                Manifest = new RecipeManifest("cat", "Cat", new[] { "bg", "aura" },
                    Array.Empty<IncompatibilityRule>()),
                Ingredients = new[] { Ing("bg", "day", "night"), Ing("aura", "none", "glow") },
            },
        },
    };

    private static (string Path, CookBookSession Session) OnDisk()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "book.cbk");
        using (var seed = MemoryBook())
            CookBookArchive.Write(path, seed.Manifest, seed.Recipes);
        var session = new CookBookSession();
        session.Open(CookBookArchive.Read(path), path);
        return (path, session);
    }

    private static ExplorerViewModel Explorer(CookBookSession session, IStatusService status)
    {
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        return new ExplorerViewModel(session.Current!, nav, dialogs, new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
            ExplorerViewModelTests.CookFactory(dialogs), session, new FilePickerService(),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), status);
    }

    private static void Cleanup(CookBookSession session, string path)
    {
        session.Dispose();
        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }

    private static IncompatibilityRule Rule() => new(RuleType.Exclude,
        new RuleTarget("bg", "day"), new[] { new RuleTarget("aura", "none") });

    [AvaloniaFact]
    public async Task An_added_rule_reaches_the_archive_and_the_open_pane()
    {
        var (path, session) = OnDisk();
        try
        {
            using var explorer = Explorer(session, new StatusService());
            explorer.ToggleLockCommand.Execute(null);   // rules are structure: the lock governs them
            explorer.SelectNodeCommand.Execute(explorer.Root.Children[0]);

            var saved = await explorer.EditRulesAsync("cat", m => RuleEdits.Add(m, Rule()), "add a rule");
            Assert.NotNull(saved);

            // On disk, which is the only claim that matters.
            using var reread = CookBookArchive.Read(path);
            var written = Assert.Single(reread.Recipes[0].Manifest.Rules);
            // RuleEdits.AreSame, not Assert.Equal: IncompatibilityRule is a record whose Targets is
            // an IReadOnlyList, and a record compares that by REFERENCE — so a rule that round-trips
            // through JSON as a List never equals the same rule built from an array. That is exactly
            // why AreSame exists, and it compares the targets as a set.
            Assert.True(RuleEdits.AreSame(Rule(), written));

            // And the pane the user is looking at shows it, without them navigating away and back.
            var pane = Assert.IsType<RecipeDetailViewModel>(explorer.CurrentDetail);
            Assert.Single(pane.Rules);
            Assert.Equal("day", pane.Rules[0].When.Variant);
        }
        finally { Cleanup(session, path); }
    }

    [AvaloniaFact]
    public async Task A_removed_rule_leaves_the_archive()
    {
        var (path, session) = OnDisk();
        try
        {
            using var explorer = Explorer(session, new StatusService());
            explorer.ToggleLockCommand.Execute(null);
            explorer.SelectNodeCommand.Execute(explorer.Root.Children[0]);

            await explorer.EditRulesAsync("cat", m => RuleEdits.Add(m, Rule()), "add a rule");
            await explorer.EditRulesAsync("cat", m => RuleEdits.RemoveAt(m, 0), "delete a rule");

            using var reread = CookBookArchive.Read(path);
            Assert.Empty(reread.Recipes[0].Manifest.Rules);
        }
        finally { Cleanup(session, path); }
    }

    [AvaloniaFact]
    public async Task The_lock_refuses_and_says_so_and_the_file_is_untouched()
    {
        var (path, session) = OnDisk();
        var status = new StatusService();
        try
        {
            using var explorer = Explorer(session, status);   // opens LOCKED
            explorer.SelectNodeCommand.Execute(explorer.Root.Children[0]);

            var pane = Assert.IsType<RecipeDetailViewModel>(explorer.CurrentDetail);
            Assert.False(pane.CanEditRules);

            // And the Explorer refuses on its own account, not merely because the pane declined to
            // ask: this is the gate that actually guards the file.
            Assert.Null(await explorer.EditRulesAsync("cat", m => RuleEdits.Add(m, Rule()), "add a rule"));
            Assert.Contains("locked", status.Last, StringComparison.OrdinalIgnoreCase);

            using var reread = CookBookArchive.Read(path);
            Assert.Empty(reread.Recipes[0].Manifest.Rules);
        }
        finally { Cleanup(session, path); }
    }

    [AvaloniaFact]
    public async Task A_refused_edit_reports_the_reason_and_writes_nothing()
    {
        var (path, session) = OnDisk();
        try
        {
            using var explorer = Explorer(session, new StatusService());
            explorer.ToggleLockCommand.Execute(null);
            explorer.SelectNodeCommand.Execute(explorer.Root.Children[0]);

            // A layer against itself: RuleEdits throws, and the seam has to turn that into a
            // reported error rather than an unhandled exception out of a fire-and-forget command.
            var bad = new IncompatibilityRule(RuleType.Exclude,
                new RuleTarget("bg", "day"), new[] { new RuleTarget("bg", "night") });
            Assert.Null(await explorer.EditRulesAsync("cat", m => RuleEdits.Add(m, bad), "add a rule"));

            using var reread = CookBookArchive.Read(path);
            Assert.Empty(reread.Recipes[0].Manifest.Rules);
        }
        finally { Cleanup(session, path); }
    }
}
