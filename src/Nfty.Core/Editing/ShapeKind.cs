namespace Nfty.Core.Editing;

/// <summary>The shapes the editor can draw.</summary>
public enum ShapeKind
{
    /// <summary>A filled rectangle.</summary>
    Rectangle,

    /// <summary>A filled ellipse inscribed in the bounding box.</summary>
    Ellipse,

    /// <summary>A filled triangle inscribed in the bounding box.</summary>
    Triangle,
}
