using System.Globalization;
using System.Text;
using Nfty.Core.Model;

namespace Nfty.Ticket;

/// <summary>The manifest was not JSON, or not the shape a <see cref="MenuBook"/> takes.</summary>
internal sealed class MenuJsonException : Exception
{
    public MenuJsonException(string message) : base(message) { }
}

/// <summary>
/// Reads a Menu manifest with no JSON library: <c>System.Text.Json</c> is a package, not in-box, on
/// netstandard2.1, and this assembly takes no package. Used on EVERY target (never behind #if), so the
/// assembly an engine embeds and the one the desktop app loads parse a Menu identically.
/// </summary>
/// <remarks>
/// It reads what <c>System.Text.Json</c> wrote with the options the Menu was written under, and
/// keeps that reader's rules: strict RFC 8259 (no comments, no trailing commas), camelCase property
/// names matched case-sensitively, unknown properties ignored, a missing property left at its default
/// (the caller's <c>ValidateBook</c> names it), <c>null</c> refused where the member is not nullable,
/// enums only as their camelCase NAME (an integer is refused), and the last of two duplicate
/// properties wins.
/// </remarks>
internal static class MenuJson
{
    /// <summary>Parses a whole document into a tree of <see cref="Dictionary{TKey,TValue}"/>,
    /// <see cref="List{T}"/>, <see cref="string"/>, <see cref="JsonNumber"/>, <see cref="bool"/> and null.</summary>
    public static object? Parse(byte[] utf8)
    {
        string text;
        try { text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(utf8); }
        catch (DecoderFallbackException) { throw new MenuJsonException("manifest.json is not valid UTF-8."); }
        var p = new Parser(text);
        p.SkipWs();
        object? value = p.Value(0);
        p.SkipWs();
        if (!p.AtEnd) throw p.Fail("unexpected text after the JSON value");
        return value;
    }

    /// <summary>Reads <c>menuSchemaVersion</c> off the root without binding anything else.</summary>
    public static int? SchemaVersion(object? root, out bool present)
    {
        present = false;
        if (root is not Dictionary<string, object?> obj || !obj.TryGetValue("menuSchemaVersion", out var v))
            return null;
        present = true;
        return v is JsonNumber n && n.TryInt(out int i) ? i : null;
    }

    /// <summary>Binds the parsed root to a <see cref="MenuBook"/>.</summary>
    public static MenuBook Book(object? root)
    {
        var o = Obj(root, "$");
        return new MenuBook(
            Int(o, "menuSchemaVersion", "$"),
            NullableStr(o, "sourceCookBookSha256", "$"),
            Child(o, "canvas", "$", (x, at) => { var d = Obj(x, at); return new Dimensions(Int(d, "width", at), Int(d, "height", at)); })!,
            Str(o, "collectionName", "$")!,
            Str(o, "collectionSymbol", "$")!,
            List(o, "recipes", "$", Recipe)!,
            Child(o, "art", "$", Art)!,
            Bool(o, "partialAlpha", "$"));
    }

    private static MenuArt Art(object? x, string at)
    {
        var o = Obj(x, at);
        return new MenuArt(
            Enum<MenuArtMode>(o, "mode", at),
            Int(o, "atlasWidth", at),
            Int(o, "atlasHeight", at),
            List(o, "cells", at, (c, cat) =>
            {
                var co = Obj(c, cat);
                return new MenuCell(Str(co, "key", cat)!, Int(co, "x", cat), Int(co, "y", cat),
                    Int(co, "width", cat), Int(co, "height", cat));
            })!);
    }

    private static MenuRecipe Recipe(object? x, string at)
    {
        var o = Obj(x, at);
        return new MenuRecipe(
            Str(o, "id", at)!,
            Str(o, "name", at)!,
            Num(o, "weight", at),
            List(o, "layerOrder", at, (s, sat) => AsStr(s, sat))!,
            List(o, "layers", at, Layer)!,
            List(o, "rules", at, Rule)!,
            Child(o, "absentPercent", at, (d, dat) =>
            {
                var map = new Dictionary<string, double>(StringComparer.Ordinal);
                foreach (var kv in Obj(d, dat)) map[kv.Key] = AsNum(kv.Value, dat + "." + kv.Key);
                return (IReadOnlyDictionary<string, double>)map;
            })!);
    }

    private static MenuLayer Layer(object? x, string at)
    {
        var o = Obj(x, at);
        return new MenuLayer(
            Str(o, "id", at)!,
            Str(o, "name", at)!,
            Enum<LayerKind>(o, "kind", at),
            Child(o, "colorization", at, Colorization, nullable: true),
            List(o, "variants", at, (v, vat) =>
            {
                var vo = Obj(v, vat);
                return new Variant(Str(vo, "id", vat)!, Str(vo, "name", vat)!, Num(vo, "weight", vat));
            })!);
    }

    private static Colorization Colorization(object? x, string at)
    {
        var o = Obj(x, at);
        return new Colorization(
            Enum<ColorModel>(o, "model", at),
            Int(o, "hueQuantize", at),
            Int(o, "satQuantize", at),
            List(o, "entries", at, (e, eat) =>
            {
                var eo = Obj(e, eat);
                return new ColorEntry(
                    Num(eo, "weight", eat),
                    Child(eo, "range", eat, (r, rat) =>
                    {
                        var ro = Obj(r, rat);
                        return new ColorRange(Num(ro, "hueMin", rat), Num(ro, "hueMax", rat),
                            Num(ro, "satMin", rat), Num(ro, "satMax", rat));
                    }, nullable: true),
                    NullableStr(eo, "fixed", eat));
            })!);
    }

    private static IncompatibilityRule Rule(object? x, string at)
    {
        var o = Obj(x, at);
        return new IncompatibilityRule(
            Enum<RuleType>(o, "type", at),
            Child(o, "when", at, Target)!,
            List(o, "targets", at, Target)!);
    }

    private static RuleTarget Target(object? x, string at)
    {
        var o = Obj(x, at);
        return new RuleTarget(Str(o, "ingredientId", at)!, Str(o, "variantId", at)!);
    }

    // --- member readers: missing = default, null refused unless the member is nullable ---

    private static Dictionary<string, object?> Obj(object? x, string at) =>
        x as Dictionary<string, object?> ?? throw new MenuJsonException($"{at} must be an object.");

    private static bool Has(Dictionary<string, object?> o, string name, string at, bool nullable, out object? value)
    {
        if (!o.TryGetValue(name, out value)) return false;
        if (value is null)
        {
            if (nullable) return false;
            throw new MenuJsonException($"{at}.{name} must not be null.");
        }
        return true;
    }

    private static T? Child<T>(Dictionary<string, object?> o, string name, string at,
        Func<object?, string, T> read, bool nullable = false) where T : class =>
        Has(o, name, at, nullable, out var v) ? read(v, at + "." + name) : null;

    private static IReadOnlyList<T>? List<T>(Dictionary<string, object?> o, string name, string at,
        Func<object?, string, T> read)
    {
        if (!Has(o, name, at, nullable: false, out var v)) return null;
        string here = at + "." + name;
        if (v is not List<object?> items) throw new MenuJsonException($"{here} must be an array.");
        var result = new List<T>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            string itemAt = here + "[" + i.ToString(CultureInfo.InvariantCulture) + "]";
            if (items[i] is null) throw new MenuJsonException($"{itemAt} must not be null.");
            result.Add(read(items[i], itemAt));
        }
        return result;
    }

    private static string? Str(Dictionary<string, object?> o, string name, string at) =>
        Has(o, name, at, nullable: false, out var v) ? AsStr(v, at + "." + name) : null;

    private static string? NullableStr(Dictionary<string, object?> o, string name, string at) =>
        Has(o, name, at, nullable: true, out var v) ? AsStr(v, at + "." + name) : null;

    private static string AsStr(object? v, string at) =>
        v as string ?? throw new MenuJsonException($"{at} must be a string.");

    private static double Num(Dictionary<string, object?> o, string name, string at) =>
        Has(o, name, at, nullable: false, out var v) ? AsNum(v, at + "." + name) : 0d;

    private static double AsNum(object? v, string at) =>
        v is JsonNumber n ? n.Double : throw new MenuJsonException($"{at} must be a number.");

    private static int Int(Dictionary<string, object?> o, string name, string at)
    {
        if (!Has(o, name, at, nullable: false, out var v)) return 0;
        return v is JsonNumber n && n.TryInt(out int i)
            ? i
            : throw new MenuJsonException($"{at}.{name} must be a whole number.");
    }

    private static bool Bool(Dictionary<string, object?> o, string name, string at)
    {
        if (!Has(o, name, at, nullable: false, out var v)) return false;
        return v is bool b ? b : throw new MenuJsonException($"{at}.{name} must be true or false.");
    }

    private static T Enum<T>(Dictionary<string, object?> o, string name, string at) where T : struct, Enum
    {
        if (!Has(o, name, at, nullable: false, out var v)) return default;
        if (v is string s)
            foreach (T value in System.Enum.GetValues(typeof(T)))
            {
                string member = value.ToString();
                if (string.Equals(s, char.ToLowerInvariant(member[0]) + member.Substring(1), StringComparison.OrdinalIgnoreCase))
                    return value;
            }
        throw new MenuJsonException($"{at}.{name} is not a known {typeof(T).Name} name.");
    }

    /// <summary>A JSON number kept as its text, so an integer and a double are each read exactly.</summary>
    internal sealed class JsonNumber
    {
        private readonly string _text;
        public JsonNumber(string text) { _text = text; }
        public double Double => double.Parse(_text, NumberStyles.Float, CultureInfo.InvariantCulture);
        public bool TryInt(out int value) =>
            int.TryParse(_text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    private sealed class Parser
    {
        private const int MaxDepth = 64; // System.Text.Json's default
        private readonly string _s;
        private int _i;

        public Parser(string s) { _s = s; }

        public bool AtEnd => _i >= _s.Length;

        public MenuJsonException Fail(string what) =>
            new($"{what} at character {_i.ToString(CultureInfo.InvariantCulture)}.");

        public void SkipWs()
        {
            while (_i < _s.Length && (_s[_i] == ' ' || _s[_i] == '\t' || _s[_i] == '\n' || _s[_i] == '\r')) _i++;
        }

        public object? Value(int depth)
        {
            if (depth > MaxDepth) throw Fail("JSON nested too deeply");
            if (AtEnd) throw Fail("unexpected end of JSON");
            char c = _s[_i];
            switch (c)
            {
                case '{': return Object(depth);
                case '[': return Array(depth);
                case '"': return String();
                case 't': Literal("true"); return true;
                case 'f': Literal("false"); return false;
                case 'n': Literal("null"); return null;
                default:
                    if (c == '-' || (c >= '0' && c <= '9')) return Number();
                    throw Fail($"unexpected character '{c}'");
            }
        }

        private void Literal(string word)
        {
            if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0) throw Fail("invalid literal");
            _i += word.Length;
        }

        private Dictionary<string, object?> Object(int depth)
        {
            var o = new Dictionary<string, object?>(StringComparer.Ordinal);
            _i++;
            SkipWs();
            if (!AtEnd && _s[_i] == '}') { _i++; return o; }
            while (true)
            {
                SkipWs();
                if (AtEnd || _s[_i] != '"') throw Fail("expected a property name");
                string key = String();
                SkipWs();
                if (AtEnd || _s[_i] != ':') throw Fail("expected ':'");
                _i++;
                SkipWs();
                o[key] = Value(depth + 1);
                SkipWs();
                if (AtEnd) throw Fail("unexpected end of JSON");
                if (_s[_i] == ',') { _i++; continue; }
                if (_s[_i] == '}') { _i++; return o; }
                throw Fail("expected ',' or '}'");
            }
        }

        private List<object?> Array(int depth)
        {
            var a = new List<object?>();
            _i++;
            SkipWs();
            if (!AtEnd && _s[_i] == ']') { _i++; return a; }
            while (true)
            {
                SkipWs();
                a.Add(Value(depth + 1));
                SkipWs();
                if (AtEnd) throw Fail("unexpected end of JSON");
                if (_s[_i] == ',') { _i++; continue; }
                if (_s[_i] == ']') { _i++; return a; }
                throw Fail("expected ',' or ']'");
            }
        }

        private string String()
        {
            _i++; // opening quote
            var sb = new StringBuilder();
            while (true)
            {
                if (AtEnd) throw Fail("unterminated string");
                char c = _s[_i++];
                if (c == '"') return sb.ToString();
                if (c < ' ') throw Fail("control character in string");
                if (c != '\\') { sb.Append(c); continue; }
                if (AtEnd) throw Fail("unterminated string");
                char e = _s[_i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (_i + 4 > _s.Length) throw Fail("bad \\u escape");
                        if (!int.TryParse(_s.Substring(_i, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int code))
                            throw Fail("bad \\u escape");
                        sb.Append((char)code);
                        _i += 4;
                        break;
                    default: throw Fail("bad escape");
                }
            }
        }

        private JsonNumber Number()
        {
            int start = _i;
            if (_s[_i] == '-') _i++;
            if (AtEnd) throw Fail("bad number");
            if (_s[_i] == '0') _i++;
            else if (_s[_i] >= '1' && _s[_i] <= '9') Digits();
            else throw Fail("bad number");
            if (!AtEnd && _s[_i] == '.')
            {
                _i++;
                if (AtEnd || _s[_i] < '0' || _s[_i] > '9') throw Fail("bad number");
                Digits();
            }
            if (!AtEnd && (_s[_i] == 'e' || _s[_i] == 'E'))
            {
                _i++;
                if (!AtEnd && (_s[_i] == '+' || _s[_i] == '-')) _i++;
                if (AtEnd || _s[_i] < '0' || _s[_i] > '9') throw Fail("bad number");
                Digits();
            }
            return new JsonNumber(_s.Substring(start, _i - start));
        }

        private void Digits()
        {
            while (!AtEnd && _s[_i] >= '0' && _s[_i] <= '9') _i++;
        }
    }
}
