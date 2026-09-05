using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Imaging;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The bridge from the ABSENT field to the command, exercised as GESTURES.
///
/// <para>Every other test of this feature — the pane tests, the journey test — calls
/// <c>CommitAbsentAsync</c> directly. That proves the command is right and proves nothing about
/// whether anything on screen invokes it, which is the exact shape of the bug this project has
/// already shipped twice: Landing's "+ Recipe" was a correct wizard whose result was dropped, and
/// the rules panel's row actions were correct buttons no pointer could reach. Both were found by a
/// person driving the app. Nothing here calls a command by name — the test types into the real
/// control, moves focus or presses Enter, and asks the FILE what happened.</para>
/// </summary>
public class ChanceFieldGestureTests
{
    internal static LoadedIngredient PubIng(string id, params string[] v) => Ing(id, v);

    private static LoadedIngredient Ing(string id, params string[] variants) => new()
    {
        Manifest = new IngredientManifest(id, id.ToUpperInvariant(), LayerKind.Custom, null,
            variants.Select(v => new Variant(v, v, 1)).ToArray()),
        VariantImages = variants.ToDictionary(v => v,
            _ => new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(8, 8)),
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
                    System.Array.Empty<IncompatibilityRule>()),
                Ingredients = new[] { Ing("bg", "day", "night"), Ing("aura", "none", "glow") },
            },
        },
    };

    /// <summary>
    /// The pane on screen over a real recipe, with the chance column revealed and a recording seam
    /// standing in for the Explorer's.
    /// </summary>
    /// <remarks>
    /// The seam records rather than writes, because what is under test is whether the GESTURE
    /// reaches it at all. <see cref="OptionalLayerJourneyTests"/> owns the other half — that an edit
    /// which reaches the seam lands in a real archive and changes real pixels.
    /// </remarks>
    private static (Window Window, Views.RecipeDetailView View, List<(string Id, double Pct)> Writes)
        Render()
    {
        var book = MemoryBook();
        var writes = new List<(string, double)>();

        var vm = new RecipeDetailViewModel(book.Recipes[0], book, new ImageBridge(), _ => { },
            moveLayer: (_, _) => Task.FromResult<LoadedCookBook?>(null),
            canReorder: true,
            editRules: (edit, _) =>
            {
                // Applying the edit is how the seam learns WHAT was asked for: AbsentChance.Set is
                // the only thing the pane hands it, and reading the result is more honest than
                // trusting the caller to also report it.
                var after = edit(book.Recipes[0].Manifest);
                foreach (var layer in after.LayerOrder)
                {
                    double now = after.AbsentPercentOf(layer);
                    if (now != book.Recipes[0].Manifest.AbsentPercentOf(layer))
                        writes.Add((layer, now));
                }
                return Task.FromResult<LoadedCookBook?>(null);
            },
            dialogs: new FakeDialogs());

        vm.ToggleOptionalLayersCommand.Execute(null);

        var view = new Views.RecipeDetailView { DataContext = vm };
        var window = new Window { Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view, writes);
    }

    /// <summary>The editable field for the top layer — the one the fixture leaves at zero.</summary>
    private static NumericUpDown Field(Visual view) => view.GetVisualDescendants()
        .OfType<NumericUpDown>()
        .Last(n => n.Name == "AbsentField");

    /// <summary>
    /// The box inside the field's template. Focus rests HERE, not on the NumericUpDown, so this is
    /// what a real gesture is sourced at — the distinction the lost-focus handler got wrong.
    /// </summary>
    private static TextBox Box(NumericUpDown field) =>
        field.GetVisualDescendants().OfType<TextBox>().First();

    /// <summary>
    /// Typing a chance and moving focus away writes it. No command is named anywhere in this test:
    /// the only thing that can carry the value from the box to the seam is the view's own
    /// <c>LostFocus</c> handler.
    /// </summary>
    [AvaloniaFact]
    public void Tabbing_out_of_the_field_commits_what_was_typed()
    {
        var (window, view, writes) = Render();
        try
        {
            var field = Field(view);
            field.Focus();
            Dispatcher.UIThread.RunJobs();

            field.Value = 40;
            Assert.Empty(writes);   // still being edited: a stepper spin must not write per step

            // Real focus movement, not a synthesized event: LostFocus is raised by the focus manager
            // as a consequence, which is the causal chain the running app has.
            var elsewhere = view.GetVisualDescendants().OfType<Button>().First(b => b.Focusable);
            elsewhere.Focus();
            Dispatcher.UIThread.RunJobs();

            var write = Assert.Single(writes);
            Assert.Equal("aura", write.Id);
            Assert.Equal(40, write.Pct);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// Enter commits without waiting for focus to go anywhere — the other half of "done being
    /// edited", and the half a user reaches for when the field is the last thing they touched.
    /// </summary>
    [AvaloniaFact]
    public void Pressing_enter_in_the_field_commits_without_leaving_it()
    {
        var (window, view, writes) = Render();
        try
        {
            var field = Field(view);
            field.Focus();
            Dispatcher.UIThread.RunJobs();

            field.Value = 65;
            // Let the binding reach the row before the key arrives. In the running app the ordering
            // comes for free: NumericUpDown's own Enter handling sits closer to the source than this
            // view's bubbled one, so the text is parsed into Value before we read the row.
            Dispatcher.UIThread.RunJobs();

            // Raised from the INNER box, which is where focus actually rests and therefore where a
            // real key press is sourced. Sourcing it at the NumericUpDown would be a gesture nobody
            // performs, and it is what hid the lost-focus bug for as long as it did.
            var box = Box(field);
            box.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Enter,
                Source = box,
            });
            Dispatcher.UIThread.RunJobs();

            var write = Assert.Single(writes);
            Assert.Equal("aura", write.Id);
            Assert.Equal(65, write.Pct);

            // And focus stayed put — Enter means "I am done with this number", not "leave".
            Assert.True(field.IsKeyboardFocusWithin);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// Enter in the chance field must not fall through to the reorder keys, which share a handler
    /// with it and would move a layer under a user who was typing a percentage.
    /// </summary>
    /// <remarks>
    /// Asserting <c>e.Handled</c> is NOT enough and was the first form of this test: the inner
    /// TextBox marks Enter handled on its own account, so the assertion passed at a time when
    /// nothing of ours ran at all. What proves this is the reorder NOT happening while a commit
    /// does — two observable consequences, neither of which the TextBox can produce.
    /// </remarks>
    [AvaloniaFact]
    public void Enter_belongs_to_the_field_and_does_not_reach_the_table()
    {
        var (window, view, writes) = Render();
        try
        {
            var vm = (RecipeDetailViewModel)view.DataContext!;
            var before = vm.Layers.Select(l => l.Id).ToList();

            var field = Field(view);
            field.Focus();
            Dispatcher.UIThread.RunJobs();
            field.Value = 30;
            Dispatcher.UIThread.RunJobs();

            var box = Box(field);
            box.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Enter,
                Source = box,
            });
            Dispatcher.UIThread.RunJobs();

            Assert.Single(writes);                                  // ours ran
            Assert.Equal(before, vm.Layers.Select(l => l.Id));      // and the table did not move
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// Leaving a field nobody changed writes nothing. Focus crosses these boxes constantly — tabbing
    /// through the table touches every one — and a write per visit would rewrite every PNG in the
    /// book for a value that did not move.
    /// </summary>
    [AvaloniaFact]
    public void Leaving_an_untouched_field_writes_nothing()
    {
        var (window, view, writes) = Render();
        try
        {
            var field = Field(view);
            field.Focus();
            Dispatcher.UIThread.RunJobs();

            var elsewhere = view.GetVisualDescendants().OfType<Button>().First(b => b.Focusable);
            elsewhere.Focus();
            Dispatcher.UIThread.RunJobs();

            Assert.Empty(writes);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// A locked book offers plain text where the field was, so there is no control to type into and
    /// nothing to commit. Asserted from the visual tree rather than from <c>CanEditChances</c>: the
    /// property is the mechanism, and having no field is the requirement.
    /// </summary>
    [AvaloniaFact]
    public void A_locked_pane_has_no_field_to_type_into()
    {
        using var book = MemoryBook();
        var vm = new RecipeDetailViewModel(book.Recipes[0], book, new ImageBridge(), _ => { },
            moveLayer: (_, _) => Task.FromResult<LoadedCookBook?>(null),
            canReorder: false,
            editRules: (_, _) => Task.FromResult<LoadedCookBook?>(null),
            dialogs: new FakeDialogs());

        var view = new Views.RecipeDetailView { DataContext = vm };
        var window = new Window { Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<NumericUpDown>(),
                n => n.Name == "AbsentField" && n.IsVisible);
        }
        finally { window.Close(); }
    }
}
