namespace Nfty.App.ViewModels;

/// <summary>
/// The ratio a linked canvas keeps: the one rule behind every <see cref="Controls.CanvasSizeField"/>, held by the view
/// model so New CookBook and New Ingredient cannot link two ways.
/// </summary>
/// <remarks>Locking captures W:H as it stands; while linked, a changed side returns the other side's new value; while
/// unlinked the ratio follows the current sides, so linking later keeps what the reader sees.</remarks>
public sealed class AspectLink
{
    private double _ratio = 1.0;
    private bool _syncing;

    /// <summary>Captures the current ratio (on link, or while unlinked).</summary>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    public void Capture(int width, int height)
    {
        if (width > 0 && height > 0) _ratio = (double)width / height;
    }

    /// <summary>Width changed: the height to set, or null when nothing follows.</summary>
    /// <param name="linked">Whether the sides are linked.</param>
    /// <param name="width">The new width.</param>
    /// <param name="height">The current height.</param>
    /// <returns>The height that keeps the ratio, or null.</returns>
    public int? WidthChanged(bool linked, int width, int height)
    {
        if (_syncing) return null;
        if (!linked) { Capture(width, height); return null; }
        return Math.Max(1, (int)Math.Round(width / _ratio));
    }

    /// <summary>Height changed: the width to set, or null when nothing follows.</summary>
    /// <param name="linked">Whether the sides are linked.</param>
    /// <param name="width">The current width.</param>
    /// <param name="height">The new height.</param>
    /// <returns>The width that keeps the ratio, or null.</returns>
    public int? HeightChanged(bool linked, int width, int height)
    {
        if (_syncing) return null;
        if (!linked) { Capture(width, height); return null; }
        return Math.Max(1, (int)Math.Round(height * _ratio));
    }

    /// <summary>Applies the follower's value without it echoing back.</summary>
    /// <param name="set">The setter of the other side.</param>
    public void Follow(Action set)
    {
        _syncing = true;
        try { set(); } finally { _syncing = false; }
    }
}
