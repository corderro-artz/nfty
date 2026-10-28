using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.App.Viewer;

/// <summary>A frozen subject: it clones the art and texture it is given, so the caller may dispose
/// its own images the moment the constructor returns.</summary>
public sealed class SnapshotSubject : ViewerSubject
{
    private readonly string _title;
    private readonly IReadOnlyList<ViewerFact> _facts;
    private Image<Rgba32>? _art;
    private ViewerModel? _model;

    /// <summary>Builds a snapshot.</summary>
    /// <param name="title">The window title.</param>
    /// <param name="facts">The facts panel lines.</param>
    /// <param name="art">The 2D art, cloned; null for none.</param>
    /// <param name="model">The 3D model; its texture is cloned, the mesh (immutable) is shared.</param>
    public SnapshotSubject(string title, IReadOnlyList<ViewerFact> facts, Image<Rgba32>? art, ViewerModel? model)
    {
        _title = title;
        _facts = facts;
        _art = art?.Clone();
        _model = model is null ? null : model with { Texture = model.Texture.Clone() };
    }

    /// <inheritdoc />
    public override string Title => _title;
    /// <inheritdoc />
    public override IReadOnlyList<ViewerFact> Facts => _facts;
    /// <inheritdoc />
    public override Image<Rgba32>? Art => _art;
    /// <inheritdoc />
    public override ViewerModel? Model => _model;

    /// <inheritdoc />
    public override void Dispose()
    {
        _art?.Dispose();
        _model?.Texture.Dispose();
        _art = null;
        _model = null;
    }
}
