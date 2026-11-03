using System;
using System.IO;

namespace Nfty.App.Services;

/// <summary>
/// The one place a failure becomes words the app shows: an error card's message, an inline reason
/// line, a status sentence.
/// </summary>
/// <remarks>
/// <para><b>Product wording passes through; framework wording never does.</b> An exception nfty
/// THREW (its throwing method is declared in an <c>Nfty.*</c> assembly - <c>CorruptSetException</c>,
/// the Validator's sentences, <c>UnsupportedSchemaVersionException</c>, a Core
/// <c>InvalidOperationException</c> that names the layer) already says what happened in this
/// product's words, so its message is shown verbatim, as <c>ErrorReport</c> does for the CLI. An
/// exception thrown by .NET or a library - a ZIP reader's "End of Central Directory record could
/// not be found", a JSON parser's byte offset, a sharing violation's HRESULT text - is a
/// sentence about the framework's internals, so it is replaced by one naming what went wrong in
/// the reader's terms and what to do next.</para>
///
/// <para>Decided by WHO THREW, not by type: <c>InvalidOperationException</c> is both Core's product
/// refusal and LINQ's "Sequence contains no elements". The throwing method is the only fact that
/// tells them apart.</para>
/// </remarks>
public static class ErrorText
{
    /// <summary>The sentence to show for <paramref name="ex"/>.</summary>
    /// <param name="ex">The failure, as caught.</param>
    /// <returns>Product wording: verbatim when nfty threw it, a mapped sentence otherwise.</returns>
    public static string For(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        while (ex is AggregateException { InnerExceptions.Count: 1 } agg) ex = agg.InnerExceptions[0];
        if (ex is System.Reflection.TargetInvocationException { InnerException: { } inner }) ex = inner;

        if (IsOwnWording(ex)) return ex.Message;
        return Mapped(ex);
    }

    /// <summary>Whether nfty itself threw <paramref name="ex"/>, and so wrote its message.</summary>
    /// <param name="ex">The failure.</param>
    /// <returns>True when the exception type, or the method that threw it, is nfty's own.</returns>
    public static bool IsOwnWording(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        if (IsNfty(ex.GetType().Assembly.GetName().Name)) return true;
        var thrower = ex.TargetSite?.DeclaringType?.Assembly.GetName().Name;
        // No stack (constructed, never thrown): a message someone built by hand, which in this
        // codebase means ours.
        if (thrower is null) return ex.StackTrace is null;
        return IsNfty(thrower);
    }

    private static bool IsNfty(string? assembly) =>
        assembly is not null && assembly.StartsWith("Nfty", StringComparison.Ordinal);

    private const int SharingViolation = unchecked((int)0x80070020);
    private const int LockViolation = unchecked((int)0x80070021);
    private const int DiskFull = unchecked((int)0x80070070);
    private const int HandleDiskFull = unchecked((int)0x80070027);

    private static string Mapped(Exception ex)
    {
        var ns = ex.GetType().Namespace ?? "";
        return ex switch
        {
            OperationCanceledException => "It was stopped before it finished. Nothing was changed by the part that did not run.",
            TimeoutException => "The other side did not answer in time. Check the connection and try again.",
            FileNotFoundException f => $"{Named(f.FileName, "The file")} is no longer there — it may have been moved or deleted. Choose it again.",
            DirectoryNotFoundException => "That folder is no longer there — it may have been moved or deleted. Choose another.",
            PathTooLongException => "The path is too long for this system. Choose a folder nearer the top of the drive.",
            UnauthorizedAccessException => "nfty is not allowed to read or write there. Choose a folder you can write to, or check the file is not read-only.",
            InvalidDataException => "The file is damaged, or is not an nfty archive — it does not open as one. Check it is the right file, or export it again from its source.",
            IOException io when io.HResult is SharingViolation or LockViolation =>
                "The file is open in another program. Close it there and try again.",
            IOException io when io.HResult is DiskFull or HandleDiskFull =>
                "The disk is full. Free some space and try again.",
            IOException => "The file could not be read or written. Check the drive is still connected and try again.",
            System.Text.Json.JsonException => "The file's contents are damaged — part of it is not readable. Check it is the right file, or export it again from its source.",
            System.Net.Http.HttpRequestException => "The service could not be reached. Check the connection and the address in Settings, then try again.",
            OutOfMemoryException => "There was not enough memory for this. Close other work, or try a smaller canvas or collection.",
            _ when ns.StartsWith("SixLabors", StringComparison.Ordinal) =>
                "The picture is not a readable image. Use a PNG or JPEG file.",
            _ => $"nfty hit a problem it did not expect ({ex.GetType().Name}). Try again; if it happens again, please report what you were doing.",
        };
    }

    private static string Named(string? path, string fallback) =>
        string.IsNullOrEmpty(path) ? fallback : $"“{Path.GetFileName(path)}”";
}
