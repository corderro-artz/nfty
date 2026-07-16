namespace Nfty.Core.Output;

/// <summary>
/// An existing Set on disk is not in a shape <c>extend</c> can read — for example a rich
/// <c>nfty/NNNN.json</c> whose standards-pure <c>metadata/NNNN.json</c> sibling is missing.
/// Names the offending file, so the user is not left guessing at a raw IO error.
/// </summary>
public class CorruptSetException : InvalidOperationException
{
    /// <summary>The file whose absence or contents made the Set unreadable.</summary>
    public string Path { get; }

    public CorruptSetException(string path, string message) : base(message) => Path = path;
}
