using System.CommandLine;
using System.CommandLine.Parsing;

namespace Nfty.Cli;

/// <summary>
/// The parse-time and action-time guards every command shares: "was this option typed?" and the
/// refusals built on it. One definition, so the commands that split into forms (or gate options on
/// a flag) cannot word or decide "given" differently.
/// </summary>
internal static class CliGuards
{
    /// <summary>Whether the user typed <paramref name="option"/>. An option holding only its default
    /// is NOT given: at validation time an unset option has not even run its default factory.</summary>
    /// <param name="result">The command (or other symbol) result being validated.</param>
    /// <param name="option">The option asked about.</param>
    /// <returns>True when the option appeared on the command line.</returns>
    internal static bool IsGiven(this SymbolResult result, Option option) =>
        result.GetResult(option) is { Implicit: false };

    /// <summary>The same question asked of a finished parse, from inside an action.</summary>
    /// <param name="parse">The parse.</param>
    /// <param name="option">The option asked about.</param>
    /// <returns>True when the option appeared on the command line.</returns>
    internal static bool IsGiven(this ParseResult parse, Option option) =>
        parse.GetResult(option) is { Implicit: false };

    /// <summary>Reads an option during validation, retaining the parser's diagnostic when lazy
    /// conversion fails instead of turning invalid input into a thrown validator failure.</summary>
    /// <typeparam name="T">The option value type.</typeparam>
    /// <param name="result">The result whose option is being validated.</param>
    /// <param name="option">The option to read.</param>
    /// <param name="value">The converted value, or its default when conversion failed.</param>
    /// <returns>True when the value was read successfully.</returns>
    internal static bool TryGetValue<T>(this SymbolResult result, Option<T> option, out T value)
    {
        try { value = result.GetValue(option)!; return true; }
        catch (InvalidOperationException) when (result.GetResult(option)?.Errors.Any() == true)
        {
            value = default!;
            return false;
        }
    }

    /// <summary>Reads the option being validated while preserving its conversion errors.</summary>
    /// <typeparam name="T">The option value type.</typeparam>
    /// <param name="result">The option result being validated.</param>
    /// <param name="value">The converted value, or its default when conversion failed.</param>
    /// <returns>True when the value was read successfully.</returns>
    internal static bool TryGetValue<T>(this OptionResult result, out T value)
    {
        try { value = result.GetValueOrDefault<T>()!; return true; }
        catch (InvalidOperationException) when (result.Errors.Any())
        {
            value = default!;
            return false;
        }
    }

    /// <summary>Refuses each given option when <paramref name="allowed"/> is false, naming it. An option
    /// that silently does nothing is worse than one that says no.</summary>
    /// <param name="result">The command result being validated.</param>
    /// <param name="allowed">Whether the options make sense in this form.</param>
    /// <param name="why">The reason, worded to follow the option's name.</param>
    /// <param name="options">The options that only make sense when <paramref name="allowed"/>.</param>
    internal static void RejectUnless(this SymbolResult result, bool allowed, string why, params Option[] options)
    {
        if (allowed) return;
        foreach (var option in options)
            if (result.IsGiven(option))
                result.AddError($"{option.Name} {why}");
    }

    /// <summary>Refuses an option that belongs to the other form of a command, never ignores it.</summary>
    /// <param name="value">The option's value, null when not typed. An empty string is supplied.</param>
    /// <param name="option">The option's name as typed.</param>
    /// <param name="why">The reason, worded to follow the option's name.</param>
    /// <exception cref="InvalidOperationException"><paramref name="value"/> is not null.</exception>
    internal static void RejectGiven(string? value, string option, string why)
    {
        if (value is not null)
            throw new InvalidOperationException($"{option} {why}");
    }

    /// <summary>The value of an option the current form requires.</summary>
    /// <param name="value">The option's value, null when not typed. An empty string is supplied.</param>
    /// <param name="option">The option's name as typed.</param>
    /// <param name="why">The condition, worded to follow "is required".</param>
    /// <returns>The value.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="value"/> is null.</exception>
    internal static string RequireGiven(string? value, string option, string why) =>
        value ?? throw new InvalidOperationException($"{option} is required {why}");
}
