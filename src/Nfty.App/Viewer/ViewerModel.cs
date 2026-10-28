using Nfty.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.App.Viewer;

/// <summary>The 3D half of a viewer subject.</summary>
/// <param name="Mesh">The solid.</param>
/// <param name="Texture">Its texture, owned by the subject.</param>
/// <param name="Files">The model's file bytes when it came from a file; null when built in memory.</param>
public sealed record ViewerModel(Mesh Mesh, Image<Rgba32> Texture, IReadOnlyList<ModelFile>? Files);
