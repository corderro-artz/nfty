using System.Globalization;
using System.Text.Json;
using Nfty.Core;

namespace Nfty.App.Services;

/// <summary>
/// How many worker threads the app's heavy operations may use, as the reader chose it on the
/// Settings card.
/// </summary>
/// <remarks>
/// <para>Precedence: a valid <c>NFTY_THREADS</c> wins for the session and the card says so; then
/// the saved count; then <see cref="Parallelism.Default"/>. Read <see cref="Current"/> at the START
/// of each operation, so a change applies to the next one and never to a run already going.</para>
/// <para>The count can never exceed the machine's logical processors: a count saved on a bigger
/// machine stays in the file but is clamped through <see cref="Parallelism.Effective"/> here.</para>
/// </remarks>
public interface IParallelismSetting
{
    /// <summary>What an operation started now should run with.</summary>
    Parallelism Current { get; }

    /// <summary>True when nothing chose a count: no environment override and nothing saved.</summary>
    bool IsAuto { get; }

    /// <summary>The count saved on the card, or null for Auto. Unclamped: it is what the file holds.</summary>
    int? Saved { get; }

    /// <summary>The valid <c>NFTY_THREADS</c> text in force this session (a number or <c>max</c>), or
    /// null when the variable is unset or invalid. While set, <see cref="Set"/> still saves but
    /// cannot change <see cref="Current"/>.</summary>
    string? EnvironmentText { get; }

    /// <summary>Chooses a count. Null is Auto; a number is clamped to between 1 and the logical
    /// processor count.</summary>
    /// <param name="degree">The count, or null for Auto.</param>
    void Set(int? degree);

    /// <summary>Raised after <see cref="Set"/> changes what is held.</summary>
    event Action? Changed;
}

/// <inheritdoc cref="IParallelismSetting"/>
public sealed class ParallelismSettingService : IParallelismSetting
{
    /// <summary>The store file the choice lives in.</summary>
    public const string FileName = "settings.json";

    /// <summary>The environment variable that overrides the saved count for one session.</summary>
    public const string EnvironmentVariable = "NFTY_THREADS";

    // Same options as EditorViewStateService: camelCase out, case-insensitive in.
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private sealed record SettingsFile(int? Threads);

    private readonly IStateStore _store;
    private readonly Parallelism? _fromEnvironment;

    /// <summary>Creates the setting over a store.</summary>
    /// <param name="store">Where the choice is persisted.</param>
    /// <param name="env">Reads <c>NFTY_THREADS</c>. Evaluated once: the override is a property of the session.</param>
    public ParallelismSettingService(IStateStore store, Func<string?> env)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(env);
        _store = store;
        Saved = Load(store.Read(FileName));
        var raw = env()?.Trim();
        if (TryParse(raw, out var parsed))
        {
            _fromEnvironment = parsed;
            EnvironmentText = parsed.Requested == 0 ? "max" : parsed.Requested.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <inheritdoc />
    public int? Saved { get; private set; }

    /// <inheritdoc />
    public string? EnvironmentText { get; }

    /// <inheritdoc />
    public bool IsAuto => _fromEnvironment is null && Saved is null;

    /// <inheritdoc />
    public Parallelism Current =>
        _fromEnvironment ?? (Saved is { } n ? new Parallelism(n) : Parallelism.Default);

    /// <inheritdoc />
    public event Action? Changed;

    /// <inheritdoc />
    public void Set(int? degree)
    {
        int? next = degree is { } d ? Math.Clamp(d, 1, Math.Max(1, Parallelism.LogicalProcessors)) : null;
        if (next == Saved) return;
        Saved = next;
        try { _store.Write(FileName, JsonSerializer.Serialize(new SettingsFile(next), Json)); }
        catch (NotSupportedException) { /* convenience state: never surface */ }
        Changed?.Invoke();
    }

    /// <summary>A positive whole number, or <c>max</c> / <c>0</c> for every processor. Anything else is no override.</summary>
    private static bool TryParse(string? text, out Parallelism parallelism)
    {
        parallelism = Parallelism.Default;
        if (string.IsNullOrEmpty(text)) return false;
        if (text.Equals("max", StringComparison.OrdinalIgnoreCase)) { parallelism = Parallelism.All; return true; }
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return false;
        parallelism = n == 0 ? Parallelism.All : new Parallelism(n);
        return true;
    }

    private static int? Load(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var read = JsonSerializer.Deserialize<SettingsFile>(json, Json);
            return read?.Threads is >= 1 and var n ? n : null;
        }
        catch (JsonException) { return null; }
    }
}
