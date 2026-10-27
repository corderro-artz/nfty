using Nfty.Core.Editing;

namespace Nfty.App.Rise;

/// <summary>
/// What the Rise page paints depth through: a surface, an undo history and a way to bring the model
/// up to date. A single picture edits its own surface; a recipe edits the surface of one layer's
/// variant and recombines the stack from it.
/// </summary>
public interface IDepthEditor
{
    /// <summary>The surface a stroke edits and the page reads while painting.</summary>
    DepthSurface Surface { get; }

    /// <summary>Applies an edit to <see cref="Surface"/> and records it.</summary>
    /// <param name="cmd">The edit.</param>
    /// <returns>True when it changed something and was recorded; false for a no-op or a refusal.</returns>
    bool Do(IEditCommand<DepthPixel> cmd);

    /// <summary>Whether there is an applied step to undo.</summary>
    bool CanUndo { get; }

    /// <summary>Whether an undone step is available to reapply.</summary>
    bool CanRedo { get; }

    /// <summary>Undoes the most recent step.</summary>
    void Undo();

    /// <summary>Reapplies the most recently undone step.</summary>
    void Redo();

    /// <summary>Brings the subject's depth up to date after the surface was changed outside
    /// <see cref="Do"/> (a standing preview). A no-op for a single picture, which is its surface.</summary>
    void Sync();
}

/// <summary>The editor of a single picture: its subject's surface and history, as the page has always used them.</summary>
/// <param name="subject">The picture being painted.</param>
public sealed class SubjectDepthEditor(RiseSubject subject) : IDepthEditor
{
    /// <inheritdoc />
    public DepthSurface Surface => subject.Depth;

    /// <inheritdoc />
    public bool Do(IEditCommand<DepthPixel> cmd) => subject.History.Do(cmd, subject.Depth);

    /// <inheritdoc />
    public bool CanUndo => subject.History.CanUndo;

    /// <inheritdoc />
    public bool CanRedo => subject.History.CanRedo;

    /// <inheritdoc />
    public void Undo() => subject.History.Undo(subject.Depth);

    /// <inheritdoc />
    public void Redo() => subject.History.Redo(subject.Depth);

    /// <inheritdoc />
    public void Sync() { }
}
