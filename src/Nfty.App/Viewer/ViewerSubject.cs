using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.App.Viewer;

/// <summary>What the pop-out viewer shows: a title, some facts, 2D art and optionally a 3D model.
/// The subject owns its images.</summary>
public abstract class ViewerSubject : IDisposable
{
    /// <summary>The window title.</summary>
    public abstract string Title { get; }

    /// <summary>The facts panel lines.</summary>
    public abstract IReadOnlyList<ViewerFact> Facts { get; }

    /// <summary>The 2D art, owned by the subject; null when there is none.</summary>
    public abstract Image<Rgba32>? Art { get; }

    /// <summary>The 3D model, or null for a 2D-only subject.</summary>
    public abstract ViewerModel? Model { get; }

    /// <summary>Whether the subject changes under the viewer (a live page) rather than being a
    /// frozen copy.</summary>
    public virtual bool IsLive => false;

    /// <summary>False when saving or copying is refused, such as inside a sealed export.</summary>
    public bool AllowExport { get; init; } = true;

    /// <summary>Raised when a live subject's content changed.</summary>
    public event Action? Changed;

    /// <summary>Raises <see cref="Changed"/>.</summary>
    protected void RaiseChanged() => Changed?.Invoke();

    /// <summary>Frees what the subject owns.</summary>
    public abstract void Dispose();
}
