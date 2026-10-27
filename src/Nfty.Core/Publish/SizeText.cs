using System.Globalization;

namespace Nfty.Core.Publish;

/// <summary>One wording for a byte count, so every surface that prints a file size prints it alike.</summary>
public static class SizeText
{
    /// <summary>A byte count for a person: <c>636 KB</c>, <c>1.2 MB</c>, <c>12 bytes</c>.</summary>
    /// <param name="bytes">The count.</param>
    /// <returns>The size as text, invariant like every other figure this product prints.</returns>
    public static string Of(long bytes) => bytes switch
    {
        >= 1024L * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0 / 1024:0.0} MB"),
        >= 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:0} KB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes} bytes"),
    };
}
