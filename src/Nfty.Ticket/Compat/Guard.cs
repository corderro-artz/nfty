namespace Nfty.Ticket;

/// <summary>
/// Argument and state checks, written out because netstandard2.1 lacks <c>ThrowIfNull</c> and its
/// siblings. Used on every target (never behind #if), with the same exceptions and parameter names
/// the framework helpers throw.
/// </summary>
internal static class Guard
{
    public static void NotNull(object? value, string name)
    {
        if (value is null) throw new ArgumentNullException(name);
    }

    public static void NotBlank(string? value, string name)
    {
        if (value is null) throw new ArgumentNullException(name);
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("The value cannot be an empty string or composed entirely of whitespace.", name);
    }

    public static void NotDisposed(bool disposed, object instance)
    {
        if (disposed) throw new ObjectDisposedException(instance.GetType().FullName);
    }

    public static void Positive(int value, string name)
    {
        if (value <= 0) throw new ArgumentOutOfRangeException(name, value, $"{name} ('{value}') must be a non-negative and non-zero value.");
    }
}
