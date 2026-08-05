using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nfty.Core.Formats;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },

        // A manifest record declares which of its members may be absent, in the type: `Colorization?`
        // and `int? TargetSupply` are optional, `string Id` is not. Without this the declaration was
        // decorative — a manifest of `{"schemaVersion":1}` deserialized happily into a record with
        // every non-nullable member null, the archive reader returned it, and the first thing to
        // touch it threw NullReferenceException. Validator was usually that thing, which is the
        // worst place for it: answering "what is wrong with this book?" is the one job it exists
        // for, and it crashed instead of answering.
        //
        // Turning this on moves the failure to the boundary, where the exception can name the
        // property that is missing. It costs nothing in format compatibility, because the rule
        // CLAUDE.md already sets for schema evolution — every field added after v1 is optional and
        // defaults to absent — is expressed precisely by declaring those fields nullable.
        RespectNullableAnnotations = true,

        // The companion rule, and the one that actually catches a truncated manifest: nullable
        // annotations only reject a member explicitly set to null, while a member that is simply
        // ABSENT leaves the record's constructor parameter at its default. Every optional member
        // already carries a real default in its declaration (`int SchemaVersion = Schema.Current`,
        // `int? TargetSupply = null`), so "has a default" is exactly the line between optional and
        // required here — which is the same line CLAUDE.md draws for schema evolution.
        RespectRequiredConstructorParameters = true,
    };
}
