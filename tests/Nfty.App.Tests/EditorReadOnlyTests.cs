using System.IO;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// THE PENCIL IS NOT GATED BY THE EDIT LOCK AND SAVE IS.
/// </summary>
/// <remarks>
/// <para>Opening the editor on a read-only book is deliberate and documented — it is also how you
/// LOOK at a layer. Saving from it was not: <c>CanSave</c> asked only whether the draft was dirty
/// and whether there was somewhere to write, so a book the Explorer was refusing to add to, delete
/// from or reorder could have a layer's manifest rewritten, its KIND changed and the whole archive
/// persisted, from a screen one click away, with the titlebar still saying read-only.</para>
///
/// <para>These tests are the two halves of that: the gate itself, and the journey that proves the
/// bytes on disk do not move. The journey is the one that matters — a property test would pass on a
/// <c>CanSave</c> that lied, and every previous test of this path built its editor with no lock at
/// all.</para>
/// </remarks>
public class EditorReadOnlyTests
{
    /// <summary>A dirty editor over a real on-disk book, with the lock in a stated position.</summary>
    private static (string Path, CookBookSession Session, IngredientEditorViewModel Editor) Dirty(bool unlocked)
    {
        var (path, session, recipe, ing) = IngredientEditorSaveTests.OnDisk();
        var editor = new IngredientEditorViewModel(ing, recipe, session.Current!, new ImageBridge(),
            new FakeNav(), session, new FakeDialogs(), new FilePickerService(),
            isEditing: () => unlocked);
        editor.ActiveTool = EditorTool.Fill;
        editor.BrushValue = 123;
        editor.ApplyToolStroke(new[] { (0, 0) });
        return (path, session, editor);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Save_is_offered_only_while_the_cookbook_is_unlocked(bool unlocked)
    {
        var (path, session, editor) = Dirty(unlocked);
        try
        {
            Assert.True(editor.IsDirty, "the fixture did not actually edit anything");
            Assert.Equal(!unlocked, editor.IsReadOnly);
            Assert.Equal(unlocked, editor.CanSave);
            Assert.Equal(unlocked, editor.SaveCommand.CanExecute(null));
        }
        finally { editor.Dispose(); session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    /// <summary>A disabled button with no reason given is the defect this app has fixed twice
    /// elsewhere, so the note says why from the moment the page opens — not at the press.</summary>
    [AvaloniaFact]
    public void A_read_only_editor_says_why_it_cannot_save()
    {
        var (path, session, editor) = Dirty(unlocked: false);
        try
        {
            Assert.NotNull(editor.SaveNoteText);
            Assert.Contains("read-only", editor.SaveNoteText!, System.StringComparison.OrdinalIgnoreCase);
            // And it does not scold: painting is exactly what a read-only editor is FOR.
            Assert.Contains("Paint", editor.SaveNoteText!, System.StringComparison.OrdinalIgnoreCase);
        }
        finally { editor.Dispose(); session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    /// <summary>
    /// THE ARCHIVE DOES NOT MOVE. Executing the command directly is what a bypassed
    /// <c>CanExecute</c> would do, and it is the only version of this test that can fail if the gate
    /// is a label rather than a rule.
    /// </summary>
    [AvaloniaFact]
    public async Task Executing_save_on_a_locked_book_writes_nothing()
    {
        var (path, session, editor) = Dirty(unlocked: false);
        try
        {
            var before = File.ReadAllBytes(path);
            await editor.SaveCommand.ExecuteAsync(null);
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.True(editor.IsDirty, "the edit was silently dropped rather than simply not saved");
        }
        finally { editor.Dispose(); session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    /// <summary>The probe: the same journey with the lock open must actually write, or the test
    /// above would pass on an editor that can never save anything.</summary>
    [AvaloniaFact]
    public async Task The_same_edit_saves_once_the_lock_is_open()
    {
        var (path, session, editor) = Dirty(unlocked: true);
        try
        {
            var before = File.ReadAllBytes(path);
            await editor.SaveCommand.ExecuteAsync(null);
            Assert.NotEqual(before, File.ReadAllBytes(path));
            Assert.False(editor.IsDirty);
        }
        finally { editor.Dispose(); session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    /// <summary>A loose <c>.igt</c> belongs to no CookBook, so there is no lock to consult and the
    /// editor saves freely — the null branch of the gate, which is also every test fixture's.</summary>
    [AvaloniaFact]
    public void A_loose_ingredient_has_no_lock_and_saves_freely()
    {
        var (path, session, recipe, ing) = IngredientEditorSaveTests.OnDisk();
        try
        {
            using var editor = new IngredientEditorViewModel(ing, recipe, session.Current!,
                new ImageBridge(), new FakeNav(), session, new FakeDialogs(), new FilePickerService(),
                looseSavePath: Path.Combine(Path.GetDirectoryName(path)!, "loose.igt"));
            editor.ActiveTool = EditorTool.Fill;
            editor.BrushValue = 90;
            editor.ApplyToolStroke(new[] { (0, 0) });

            Assert.False(editor.IsReadOnly);
            Assert.True(editor.CanSave);
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }
}
