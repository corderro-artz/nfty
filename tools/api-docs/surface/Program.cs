// Prints the public surface of Nfty.Ticket, Nfty.Core and Nfty.Model as JSON on stdout.
//
// One object per assembly -> types -> members, each carrying its XML documentation id (the key
// build.py joins to the compiler's .xml file) and a C# declaration. Nothing here is prose.
//
//   dotnet run --project tools/api-docs/surface -c Release

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

string[] assemblyNames = ["Nfty.Ticket", "Nfty.Core", "Nfty.Model"];
var result = new List<object>();

foreach (string name in assemblyNames)
{
    var asm = Assembly.Load(name);
    var types = new List<object>();
    foreach (var t in asm.GetExportedTypes())
    {
        if (t.GetCustomAttribute<CompilerGeneratedAttribute>() is not null) continue;
        types.Add(Describe(t));
    }
    result.Add(new Dictionary<string, object> { ["assembly"] = name, ["types"] = types });
}

Console.OutputEncoding = new UTF8Encoding(false);
Console.Out.Write(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
return 0;

// ------------------------------------------------------------------------------------------------

static Dictionary<string, object> Describe(Type t)
{
    var d = new Dictionary<string, object>
    {
        ["id"] = "T:" + DefName(t),
        ["namespace"] = t.Namespace ?? "",
        ["name"] = DisplayName(t),
        ["kind"] = KindOf(t),
        ["declaration"] = TypeDeclaration(t),
    };
    var members = new List<object>();
    const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                             | BindingFlags.Static | BindingFlags.DeclaredOnly;

    if (t.IsEnum)
    {
        foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            members.Add(Member("F:" + DefName(t) + "." + f.Name, "field", f.Name + " = " + Convert.ToInt64(f.GetRawConstantValue()), f.Name));
        d["members"] = members;
        return d;
    }

    foreach (var c in t.GetConstructors(all))
    {
        if (!Visible(c) || Generated(c)) continue;
        if (t.IsValueType && c.GetParameters().Length == 0) continue;
        // The record copy constructor is protected and takes the record itself; nobody calls it.
        if (c.IsFamily && c.GetParameters() is [{ } p] && p.ParameterType == t) continue;
        string id = "M:" + DefName(t) + "." + (c.IsStatic ? "#cctor" : "#ctor") + ParamIds(c.GetParameters());
        members.Add(Member(id, "constructor", Access(c) + (c.IsStatic ? "static " : "") + t.Name.Split('`')[0]
                                                + "(" + ParamList(c.GetParameters()) + ")",
            t.Name.Split('`')[0] + "(" + TypeList(c.GetParameters()) + ")", c.GetParameters()));
    }

    foreach (var f in t.GetFields(all))
    {
        if (!VisibleF(f) || Generated(f) || f.IsSpecialName) continue;
        string mods = AccessF(f) + (f.IsLiteral ? "const " : f.IsStatic ? "static " : "") + (f.IsInitOnly ? "readonly " : "");
        string value = f.IsLiteral ? " = " + Literal(f.GetRawConstantValue()) : "";
        members.Add(Member("F:" + DefName(t) + "." + f.Name, "field",
            mods + Fmt(f.FieldType, Nullability(f)) + " " + f.Name + value, f.Name));
    }

    foreach (var p in t.GetProperties(all))
    {
        var acc = new[] { p.GetMethod, p.SetMethod }.Where(m => m is not null && Visible(m)).ToArray();
        if (acc.Length == 0 || Generated(p)) continue;
        var any = acc[0]!;
        var idx = p.GetIndexParameters();
        string name = idx.Length > 0 ? "this[" + ParamList(idx) + "]" : p.Name;
        var gm = p.GetMethod is { } g && Visible(g) ? g : null;
        var sm = p.SetMethod is { } s && Visible(s) ? s : null;
        string top = Access(any);
        // Accessor visibility: show the get/set that is narrower than the property itself.
        string Accessor(MethodInfo m, string kw)
        {
            string a = Access(m);
            string kwIn = m.ReturnParameter.GetRequiredCustomModifiers().Any(x => x.FullName == "System.Runtime.CompilerServices.IsExternalInit") ? "init" : kw;
            return (a == top ? "" : a) + kwIn + ";";
        }
        var parts = new List<string>();
        if (gm is not null) parts.Add(Accessor(gm, "get"));
        if (sm is not null) parts.Add(Accessor(sm, "set"));
        bool required = p.GetCustomAttributes().Any(a => a.GetType().FullName == "System.Runtime.CompilerServices.RequiredMemberAttribute");
        string mods = top + Modifiers(any) + (required ? "required " : "");
        members.Add(Member("P:" + DefName(t) + "." + (idx.Length > 0 ? "Item" + ParamIds(idx) : p.Name), "property",
            mods + Fmt(p.PropertyType, Nullability(p)) + " " + name + " { " + string.Join(" ", parts) + " }",
            idx.Length > 0 ? "this[" + TypeList(idx) + "]" : p.Name, idx, Inherits(any, isProperty: true)));
    }

    foreach (var e in t.GetEvents(all))
    {
        var add = e.AddMethod;
        if (add is null || !Visible(add) || Generated(e)) continue;
        members.Add(Member("E:" + DefName(t) + "." + e.Name, "event",
            Access(add) + Modifiers(add) + "event " + Fmt(e.EventHandlerType!, Nullability(e)) + " " + e.Name, e.Name));
    }

    foreach (var m in t.GetMethods(all))
    {
        if (!Visible(m) || Generated(m) || m.Name.Contains('<')) continue;
        bool op = m.IsSpecialName && m.Name.StartsWith("op_", StringComparison.Ordinal);
        if (m.IsSpecialName && !op) continue; // accessors
        if (t.IsSubclassOf(typeof(Delegate)) || t.IsSubclassOf(typeof(MulticastDelegate))) continue;
        string g = m.IsGenericMethodDefinition ? "``" + m.GetGenericArguments().Length : "";
        string id = "M:" + DefName(t) + "." + m.Name + g + ParamIds(m.GetParameters());
        if (op && (m.Name is "op_Implicit" or "op_Explicit")) id += "~" + RefId(m.ReturnType);
        string tp = m.IsGenericMethodDefinition ? "<" + string.Join(", ", m.GetGenericArguments().Select(a => a.Name)) + ">" : "";
        string ret = Fmt(m.ReturnType, Nullability(m.ReturnParameter));
        string display = op ? OperatorName(m) : m.Name;
        string sig = Access(m) + Modifiers(m) + (op ? OperatorKeyword(m) : ret + " " + display + tp)
                     + "(" + ParamList(m.GetParameters()) + ")";
        members.Add(Member(id, op ? "operator" : "method", sig, display + tp + "(" + TypeList(m.GetParameters()) + ")",
            m.GetParameters(), Inherits(m, isProperty: false)));
    }

    d["members"] = members;
    return d;
}

static Dictionary<string, object> Member(string id, string kind, string signature, string display,
    ParameterInfo[]? ps = null, List<string>? inherits = null)
{
    var d = new Dictionary<string, object> { ["id"] = id, ["kind"] = kind, ["signature"] = signature, ["display"] = display };
    if (ps is { Length: > 0 })
        d["params"] = ps.Select(p => new Dictionary<string, string>
        {
            ["name"] = p.Name ?? "",
            ["type"] = Fmt(p.ParameterType, Nullability(p), write: true),
        }).ToList();
    if (inherits is { Count: > 0 }) d["inherits"] = inherits;
    return d;
}

// ---- visibility ----------------------------------------------------------------------------------

static bool Visible(MethodBase m) => m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly;
static bool VisibleF(FieldInfo f) => f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly;
static bool Generated(MemberInfo m) => m.GetCustomAttribute<CompilerGeneratedAttribute>() is not null;

static string Access(MethodBase m) => m.IsPublic ? "public " : m.IsFamilyOrAssembly ? "protected internal " : "protected ";
static string AccessF(FieldInfo f) => f.IsPublic ? "public " : f.IsFamilyOrAssembly ? "protected internal " : "protected ";

static string Modifiers(MethodInfo m)
{
    if (m.IsStatic) return "static ";
    if (m.IsAbstract) return "abstract ";
    if (m.IsVirtual && !m.IsFinal)
        return m.GetBaseDefinition().DeclaringType != m.DeclaringType ? "override " : "virtual ";
    if (m.IsVirtual && m.IsFinal && m.GetBaseDefinition().DeclaringType != m.DeclaringType && !m.DeclaringType!.IsInterface)
        return m.Attributes.HasFlag(MethodAttributes.NewSlot) ? "" : "sealed override ";
    return "";
}

// ---- type declarations ---------------------------------------------------------------------------

static string KindOf(Type t) =>
    t.IsEnum ? "enum"
    : t.IsInterface ? "interface"
    : typeof(MulticastDelegate).IsAssignableFrom(t) ? "delegate"
    : IsRecord(t) ? (t.IsValueType ? "record struct" : "record")
    : t.IsValueType ? "struct"
    : "class";

static bool IsRecord(Type t) => t.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) is not null
                                || (t.IsValueType && t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                                     .Any(m => m.Name == "PrintMembers" || m.Name.Contains("PrintMembers")));

static string TypeDeclaration(Type t)
{
    var sb = new StringBuilder();
    sb.Append(t.IsPublic || t.IsNestedPublic ? "public " : t.IsNestedFamily ? "protected " : "public ");
    string kind = KindOf(t);
    if (kind == "delegate")
    {
        var inv = t.GetMethod("Invoke")!;
        return sb + "delegate " + Fmt(inv.ReturnType, Nullability(inv.ReturnParameter)) + " " + DisplayName(t)
               + "(" + ParamList(inv.GetParameters()) + ")";
    }
    if (t.IsClass && t.IsAbstract && t.IsSealed) sb.Append("static ");
    else if (t.IsClass && t.IsAbstract && kind is "class" or "record") sb.Append("abstract ");
    else if (t.IsClass && t.IsSealed) sb.Append("sealed ");
    if (t.IsValueType && !t.IsEnum && t.GetCustomAttributes().Any(a => a.GetType().Name == "IsReadOnlyAttribute")) sb.Append("readonly ");
    sb.Append(kind).Append(' ').Append(DisplayName(t));
    var bases = new List<string>();
    if (t.BaseType is { } b && b != typeof(object) && b != typeof(ValueType) && b != typeof(Enum))
        bases.Add(Fmt(b, null));
    var inherited = t.BaseType?.GetInterfaces() ?? [];
    foreach (var i in t.GetInterfaces().Where(i => !inherited.Contains(i)).OrderBy(i => Fmt(i, null), StringComparer.Ordinal))
    {
        // The equality plumbing every record gets is not something a reader chose.
        if (kind.StartsWith("record", StringComparison.Ordinal) && i.IsGenericType
            && i.GetGenericTypeDefinition() == typeof(IEquatable<>)) continue;
        bases.Add(Fmt(i, null));
    }
    if (bases.Count > 0 && !t.IsInterface) sb.Append(" : ").Append(string.Join(", ", bases));
    else if (bases.Count > 0) sb.Append(" : ").Append(string.Join(", ", bases));
    return sb.ToString();
}

// ---- names and ids -------------------------------------------------------------------------------

/// <summary>The XML-doc spelling of a type definition: namespace.Outer.Inner`1.</summary>
static string DefName(Type t) => (t.FullName ?? t.Name).Replace('+', '.');

static string DisplayName(Type t)
{
    string n = t.Name;
    int tick = n.IndexOf('`');
    if (tick >= 0) n = n[..tick];
    var args = t.IsGenericTypeDefinition ? t.GetGenericArguments() : [];
    int outer = t.DeclaringType is { IsGenericTypeDefinition: true } dt ? dt.GetGenericArguments().Length : 0;
    var own = args.Skip(outer).ToArray();
    if (own.Length > 0) n += "<" + string.Join(", ", own.Select(a => a.Name)) + ">";
    return t.DeclaringType is { } d ? DisplayName(d) + "." + n : n;
}

static string ParamIds(ParameterInfo[] ps) =>
    ps.Length == 0 ? "" : "(" + string.Join(",", ps.Select(p => RefId(p.ParameterType))) + ")";

static string RefId(Type t)
{
    if (t.IsGenericParameter)
        return (t.DeclaringMethod is not null ? "``" : "`") + t.GenericParameterPosition;
    if (t.IsByRef) return RefId(t.GetElementType()!) + "@";
    if (t.IsPointer) return RefId(t.GetElementType()!) + "*";
    if (t.IsArray)
    {
        int r = t.GetArrayRank();
        return RefId(t.GetElementType()!) + (r == 1 ? "[]" : "[" + string.Join(",", Enumerable.Repeat("0:", r)) + "]");
    }
    if (t.IsGenericType && !t.IsGenericTypeDefinition)
    {
        string def = DefName(t.GetGenericTypeDefinition());
        int tick = def.IndexOf('`');
        if (tick >= 0) def = def[..tick];
        return def + "{" + string.Join(",", t.GetGenericArguments().Select(RefId)) + "}";
    }
    return DefName(t);
}

static List<string>? Inherits(MethodInfo m, bool isProperty)
{
    var ids = new List<string>();
    var decl = m.DeclaringType!;
    void Add(MethodInfo target)
    {
        if (target.DeclaringType is null || target == m) return;
        if (isProperty)
        {
            var prop = target.DeclaringType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .FirstOrDefault(p => p.GetMethod == target || p.SetMethod == target);
            if (prop is null) return;
            var idx = prop.GetIndexParameters();
            ids.Add("P:" + DefName(target.DeclaringType) + "." + (idx.Length > 0 ? "Item" + ParamIds(idx) : prop.Name));
        }
        else
        {
            string g = target.IsGenericMethodDefinition ? "``" + target.GetGenericArguments().Length : "";
            ids.Add("M:" + DefName(target.DeclaringType) + "." + target.Name + g + ParamIds(target.GetParameters()));
        }
    }
    if (!m.IsStatic)
    {
        var bd = m.GetBaseDefinition();
        if (bd != m) Add(bd);
        foreach (var i in decl.GetInterfaces())
        {
            var map = decl.GetInterfaceMap(i);
            for (int k = 0; k < map.TargetMethods.Length; k++)
                if (map.TargetMethods[k] == m) Add(map.InterfaceMethods[k]);
        }
    }
    return ids.Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();
}

// ---- C# spelling ---------------------------------------------------------------------------------

static string TypeList(ParameterInfo[] ps) =>
    string.Join(", ", ps.Select(p => Fmt(p.ParameterType, Nullability(p), write: true)));

static string ParamList(ParameterInfo[] ps) =>
    string.Join(", ", ps.Select(p =>
    {
        var sb = new StringBuilder();
        if (p.GetCustomAttribute<ParamArrayAttribute>() is not null) sb.Append("params ");
        var type = p.ParameterType;
        if (type.IsByRef)
        {
            sb.Append(p.IsOut ? "out " : p.IsIn ? "in " : "ref ");
            type = type.GetElementType()!;
        }
        sb.Append(Fmt(type, Nullability(p), write: true)).Append(' ').Append(p.Name);
        if (p.HasDefaultValue) sb.Append(" = ").Append(Literal(p.DefaultValue, type));
        return sb.ToString();
    }));

static string Literal(object? v, Type? type = null)
{
    if (v is null || v is DBNull) return type is { IsValueType: true } && Nullable.GetUnderlyingType(type) is null ? "default" : "null";
    if (v is string s) return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    if (v is bool b) return b ? "true" : "false";
    if (v is char c) return "'" + c + "'";
    if (type is { IsEnum: true }) return Enum.ToObject(type, v) is { } e ? type.Name + "." + e : v.ToString()!;
    if (v is float f) return f.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "f";
    if (v is double dd) return dd.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    return Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)!;
}

static string OperatorName(MethodInfo m) => m.Name;

static string OperatorKeyword(MethodInfo m)
{
    string ret = Fmt(m.ReturnType, Nullability(m.ReturnParameter));
    return m.Name switch
    {
        "op_Implicit" => "implicit operator " + ret,
        "op_Explicit" => "explicit operator " + ret,
        _ => ret + " operator " + OperatorSymbol(m.Name),
    };
}

static string OperatorSymbol(string name) => name switch
{
    "op_Equality" => "==", "op_Inequality" => "!=", "op_Addition" => "+", "op_Subtraction" => "-",
    "op_Multiply" => "*", "op_Division" => "/", "op_LessThan" => "<", "op_GreaterThan" => ">",
    "op_LessThanOrEqual" => "<=", "op_GreaterThanOrEqual" => ">=", "op_UnaryNegation" => "-",
    _ => name,
};

static NullabilityInfo? Nullability(object member)
{
    var ctx = new NullabilityInfoContext();
    try
    {
        return member switch
        {
            ParameterInfo p => ctx.Create(p),
            PropertyInfo p => ctx.Create(p),
            FieldInfo f => ctx.Create(f),
            EventInfo e => ctx.Create(e),
            _ => null,
        };
    }
    catch (Exception)
    {
        return null;
    }
}

static string Fmt(Type t, NullabilityInfo? n, bool write = false)
{
    bool nullable = n is not null && (write ? n.WriteState : n.ReadState) == NullabilityState.Nullable;
    if (t.IsByRef) return Fmt(t.GetElementType()!, n, write);
    if (Nullable.GetUnderlyingType(t) is { } under) return Fmt(under, null) + "?";
    string core;
    if (t.IsArray) core = Fmt(t.GetElementType()!, n?.ElementType, write) + "[" + new string(',', t.GetArrayRank() - 1) + "]";
    else if (t.IsPointer) core = Fmt(t.GetElementType()!, null) + "*";
    else if (t.IsGenericParameter) core = t.Name;
    else if (Keyword(t) is { } k) core = k;
    else if (t.IsGenericType)
    {
        var args = t.GetGenericArguments();
        var argInfos = n?.GenericTypeArguments;
        string[] fm = args.Select((a, i) => Fmt(a, argInfos is { Length: > 0 } && i < argInfos.Length ? argInfos[i] : null, write)).ToArray();
        string def = t.GetGenericTypeDefinition().Name;
        if (def.StartsWith("ValueTuple`", StringComparison.Ordinal)) core = "(" + string.Join(", ", fm) + ")";
        else
        {
            int tick = def.IndexOf('`');
            string baseName = tick >= 0 ? def[..tick] : def;
            int outer = t.DeclaringType is { IsGenericTypeDefinition: true } dt ? dt.GetGenericArguments().Length : 0;
            string prefix = t.DeclaringType is { } d && !t.IsGenericParameter ? Fmt(d.IsGenericTypeDefinition ? d.MakeGenericType(args.Take(outer).ToArray()) : d, null) + "." : "";
            core = prefix + baseName + "<" + string.Join(", ", fm.Skip(outer)) + ">";
        }
    }
    else core = t.DeclaringType is { } d2 ? Fmt(d2, null) + "." + t.Name : t.Name;
    return !t.IsValueType && nullable ? core + "?" : core;
}

static string? Keyword(Type t)
{
    if (t == typeof(void)) return "void";
    if (t == typeof(bool)) return "bool";
    if (t == typeof(byte)) return "byte";
    if (t == typeof(sbyte)) return "sbyte";
    if (t == typeof(short)) return "short";
    if (t == typeof(ushort)) return "ushort";
    if (t == typeof(int)) return "int";
    if (t == typeof(uint)) return "uint";
    if (t == typeof(long)) return "long";
    if (t == typeof(ulong)) return "ulong";
    if (t == typeof(float)) return "float";
    if (t == typeof(double)) return "double";
    if (t == typeof(decimal)) return "decimal";
    if (t == typeof(char)) return "char";
    if (t == typeof(string)) return "string";
    if (t == typeof(object)) return "object";
    return null;
}
