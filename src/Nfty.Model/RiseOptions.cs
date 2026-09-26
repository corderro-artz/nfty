namespace Nfty.Model;

/// <summary>The file shape a model is written in. All three are serialized from one glTF document
/// and differ only in where the binary buffer lives.</summary>
public enum ModelFormat
{
    /// <summary>One binary <c>.glb</c>: the JSON and the buffer in one self-contained file. The
    /// default — the form every viewer opens with nothing beside it.</summary>
    Glb,

    /// <summary>A text <c>.gltf</c> plus a sibling <c>.bin</c> holding the buffer; the JSON names
    /// the <c>.bin</c> by a relative URI.</summary>
    GltfWithBin,

    /// <summary>One text <c>.gltf</c> carrying its buffer inline as a base64 <c>data:</c> URI.</summary>
    GltfEmbedded,
}

/// <summary>Which point of the model's bounding box is moved to the origin.</summary>
public enum Pivot
{
    /// <summary>The center of the box's bottom face: X and Z centered, the lowest Y at zero — what
    /// a model standing on a floor wants.</summary>
    BottomCenter,

    /// <summary>The center of the box.</summary>
    Center,

    /// <summary>The box's minimum corner: every coordinate is zero or more.</summary>
    MinCorner,
}

/// <summary>How a picture is risen and written. Every member is <c>init</c> with a default, so a new
/// axis is additive at every existing call site — the rule <c>ExportOptions</c> already follows.</summary>
public sealed record RiseOptions
{
    /// <summary>World units per pixel (and per voxel of depth). Must be positive and finite.</summary>
    public float VoxelSize { get; init; } = 1f;

    /// <summary>Which point of the bounding box lands on the origin.</summary>
    public Pivot Pivot { get; init; } = Pivot.BottomCenter;

    /// <summary>The file shape <c>ModelWriter</c> produces.</summary>
    public ModelFormat Format { get; init; } = ModelFormat.Glb;
}
