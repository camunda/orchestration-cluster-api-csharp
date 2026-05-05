using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Locks the public API surface of <c>Camunda.Orchestration.Sdk</c>.
///
/// Generates a deterministic textual snapshot of every public type and member
/// in the SDK assembly and asserts equality with the checked-in baseline at
/// <c>PublicApi.shipped.txt</c>. Any change to the public surface (renames,
/// added/removed members, changed signatures, changed nullability, changed
/// JSON wire attributes, changed enum members, changed polymorphic
/// dispatch metadata) fails this test with a precise diff.
///
/// To refresh the baseline after an intentional change:
/// <code>UPDATE_PUBLIC_API_BASELINE=1 dotnet test --framework net8.0</code>
///
/// Refresh is only honored locally — the gate refuses to write when
/// <c>CI=true</c> and only writes from the <c>net8.0</c> TFM to avoid
/// double-writes from the multi-TFM matrix.
/// </summary>
public class PublicApiSurfaceTests
{
    private const string BaselineFileName = "PublicApi.shipped.txt";

    [Fact]
    public void PublicApiSurface_MatchesShippedBaseline()
    {
        var actual = PublicApiSnapshotter.Snapshot(typeof(CamundaClient).Assembly);
        var baselinePath = LocateBaselineFile();

        if (ShouldUpdateBaseline())
        {
            Directory.CreateDirectory(Path.GetDirectoryName(baselinePath)!);
            File.WriteAllText(baselinePath, actual);
            // Surface a clear message so the developer notices the rewrite.
            Assert.Fail(
                $"PublicApi baseline rewritten at {baselinePath}. Inspect the diff and commit it. " +
                "Re-run the test without UPDATE_PUBLIC_API_BASELINE to verify.");
        }

        Assert.True(File.Exists(baselinePath),
            $"Baseline file not found at {baselinePath}. Run with UPDATE_PUBLIC_API_BASELINE=1 to generate it.");

        var expected = File.ReadAllText(baselinePath);
        if (!string.Equals(NormalizeNewlines(expected), NormalizeNewlines(actual), StringComparison.Ordinal))
        {
            var diff = ComputeFirstDiff(expected, actual);
            Assert.Fail(
                "Public API surface drift detected.\n\n" +
                $"Baseline: {baselinePath}\n" +
                "If this change is intentional, refresh the baseline with:\n" +
                "  UPDATE_PUBLIC_API_BASELINE=1 dotnet test --framework net8.0\n\n" +
                "First difference:\n" +
                diff);
        }
    }

    private static bool ShouldUpdateBaseline()
    {
        var update = Environment.GetEnvironmentVariable("UPDATE_PUBLIC_API_BASELINE");
        if (!string.Equals(update, "1", StringComparison.Ordinal))
            return false;

        // Refuse to overwrite the baseline from CI.
        var ci = Environment.GetEnvironmentVariable("CI");
        if (string.Equals(ci, "true", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "UPDATE_PUBLIC_API_BASELINE=1 is not honored when CI=true. " +
                "Refresh the baseline locally and commit it.");
        }

        // Only write from the net8.0 TFM so multi-TFM runs don't race.
        var tfm = AppContext.TargetFrameworkName ?? "";
        if (!tfm.Contains("Version=v8.0", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static string LocateBaselineFile()
    {
        // Walk up from the test assembly's output dir to the test project root.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, BaselineFileName);
            if (File.Exists(candidate))
                return candidate;
            // Recognize the test project root by its csproj.
            if (File.Exists(Path.Combine(dir.FullName, "Camunda.Orchestration.Sdk.Tests.csproj")))
            {
                return Path.Combine(dir.FullName, BaselineFileName);
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Could not locate test project root from " + AppContext.BaseDirectory);
    }

    private static string NormalizeNewlines(string s) => s.Replace("\r\n", "\n");

    private static string ComputeFirstDiff(string expected, string actual)
    {
        var e = NormalizeNewlines(expected).Split('\n');
        var a = NormalizeNewlines(actual).Split('\n');
        var max = Math.Max(e.Length, a.Length);
        for (int i = 0; i < max; i++)
        {
            var el = i < e.Length ? e[i] : "<EOF>";
            var al = i < a.Length ? a[i] : "<EOF>";
            if (!string.Equals(el, al, StringComparison.Ordinal))
            {
                var sb = new StringBuilder();
                sb.Append(CultureInfo.InvariantCulture, $"  Line {i + 1}:").AppendLine();
                sb.Append(CultureInfo.InvariantCulture, $"    expected: {el}").AppendLine();
                sb.Append(CultureInfo.InvariantCulture, $"    actual:   {al}").AppendLine();
                // Show a few lines of context after the diff.
                for (int j = i + 1; j < Math.Min(max, i + 4); j++)
                {
                    var el2 = j < e.Length ? e[j] : "<EOF>";
                    var al2 = j < a.Length ? a[j] : "<EOF>";
                    if (string.Equals(el2, al2, StringComparison.Ordinal))
                        sb.Append(CultureInfo.InvariantCulture, $"    line {j + 1} matches: {el2}").AppendLine();
                    else
                        sb.Append(CultureInfo.InvariantCulture, $"    line {j + 1} also differs (expected: {el2} | actual: {al2})").AppendLine();
                }
                return sb.ToString();
            }
        }
        return "(no line-level diff — newline normalization mismatch?)";
    }
}

/// <summary>
/// Produces a deterministic textual snapshot of an assembly's public API.
/// The format is line-oriented, sorted, and includes everything that affects
/// either source compatibility or wire compatibility.
/// </summary>
internal static class PublicApiSnapshotter
{
    public static string Snapshot(Assembly assembly)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Camunda.Orchestration.Sdk public API surface");
        sb.AppendLine("# Generated by PublicApiSurfaceTests. Do not hand-edit.");
        sb.AppendLine("# Refresh with: UPDATE_PUBLIC_API_BASELINE=1 dotnet test --framework net8.0");
        sb.AppendLine();

        var types = assembly.GetTypes()
            .Where(IsPublicVisible)
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();

        foreach (var t in types)
        {
            EmitType(sb, t);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static bool IsPublicVisible(Type t)
    {
        if (t.IsNested)
            return t.IsNestedPublic && IsPublicVisible(t.DeclaringType!);
        return t.IsPublic;
    }

    private static void EmitType(StringBuilder sb, Type t)
    {
        sb.Append("TYPE ");
        sb.Append(FormatType(t, includeNullable: false));
        sb.Append(" : ");
        sb.Append(TypeKind(t));
        if (t.BaseType != null && t.BaseType != typeof(object) && t.BaseType != typeof(ValueType) && t.BaseType != typeof(Enum))
        {
            sb.Append(" base=").Append(FormatType(t.BaseType, includeNullable: false));
        }
        var ifaces = t.GetInterfaces()
            .Where(i => IsPublicVisible(i))
            .Select(i => FormatType(i, includeNullable: false))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
        if (ifaces.Count > 0)
        {
            sb.Append(" interfaces=[").Append(string.Join(",", ifaces)).Append(']');
        }
        sb.AppendLine();

        foreach (var attr in FormatTypeAttributes(t))
        {
            sb.Append("  ATTR ").AppendLine(attr);
        }

        if (t.IsEnum)
        {
            EmitEnumMembers(sb, t);
            return;
        }

        var ctors = t.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Select(FormatConstructor)
            .OrderBy(s => s, StringComparer.Ordinal);
        foreach (var c in ctors)
            sb.Append("  ").AppendLine(c);

        var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(p => p.GetMethod?.IsPublic == true || p.SetMethod?.IsPublic == true)
            .Select(FormatProperty)
            .OrderBy(s => s, StringComparer.Ordinal);
        foreach (var p in props)
            sb.Append("  ").AppendLine(p);

        var fields = t.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(f => f.IsPublic)
            .Select(FormatField)
            .OrderBy(s => s, StringComparer.Ordinal);
        foreach (var f in fields)
            sb.Append("  ").AppendLine(f);

        var methods = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName) // exclude property/event accessors
            .Select(FormatMethod)
            .OrderBy(s => s, StringComparer.Ordinal);
        foreach (var m in methods)
            sb.Append("  ").AppendLine(m);

        var events = t.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(e => $"EVENT {FormatType(e.EventHandlerType ?? typeof(object))} {e.Name}")
            .OrderBy(s => s, StringComparer.Ordinal);
        foreach (var e in events)
            sb.Append("  ").AppendLine(e);
    }

    private static string TypeKind(Type t)
    {
        if (t.IsEnum)
            return "enum";
        if (t.IsInterface)
            return "interface";
        if (t.IsValueType)
            return t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>) ? "nullable-struct" : "struct";
        var bits = new List<string>();
        if (t.IsAbstract && t.IsSealed)
            bits.Add("static");
        else
        {
            if (t.IsAbstract)
                bits.Add("abstract");
            if (t.IsSealed)
                bits.Add("sealed");
        }
        bits.Add("class");
        return string.Join(" ", bits);
    }

    private static IEnumerable<string> FormatTypeAttributes(Type t)
    {
        var poly = t.GetCustomAttribute<JsonPolymorphicAttribute>(inherit: false);
        if (poly != null)
        {
            yield return $"JsonPolymorphic discriminator={poly.TypeDiscriminatorPropertyName ?? "<none>"}";
        }
        foreach (var d in t.GetCustomAttributes<JsonDerivedTypeAttribute>(inherit: false)
                     .OrderBy(a => a.DerivedType.FullName, StringComparer.Ordinal))
        {
            yield return $"JsonDerivedType {FormatType(d.DerivedType)} tag={d.TypeDiscriminator ?? "<none>"}";
        }
        var conv = t.GetCustomAttribute<JsonConverterAttribute>(inherit: false);
        if (conv != null)
        {
            yield return $"JsonConverter {FormatType(conv.ConverterType ?? typeof(object))}";
        }
        var obs = t.GetCustomAttribute<ObsoleteAttribute>(inherit: false);
        if (obs != null)
        {
            yield return $"Obsolete message=\"{obs.Message ?? ""}\" error={obs.IsError}";
        }
    }

    private static void EmitEnumMembers(StringBuilder sb, Type enumType)
    {
        var underlying = Enum.GetUnderlyingType(enumType);
        sb.Append("  underlying=").Append(FormatType(underlying)).AppendLine();
        var members = enumType.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => new
            {
                f.Name,
                Value = Convert.ToInt64(f.GetValue(null), CultureInfo.InvariantCulture),
                Json = f.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name,
            })
            .OrderBy(m => m.Value)
            .ThenBy(m => m.Name, StringComparer.Ordinal);
        foreach (var m in members)
        {
            sb.Append("  MEMBER ").Append(m.Name).Append(" = ").Append(m.Value);
            if (m.Json != null)
                sb.Append(" json=\"").Append(m.Json).Append('"');
            sb.AppendLine();
        }
    }

    private static string FormatConstructor(ConstructorInfo c)
    {
        var sb = new StringBuilder("CTOR (");
        sb.Append(string.Join(", ", c.GetParameters().Select(FormatParameter)));
        sb.Append(')');
        return sb.ToString();
    }

    private static string FormatProperty(PropertyInfo p)
    {
        var sb = new StringBuilder("PROP ");
        sb.Append(FormatTypeWithNullability(p.PropertyType, new NullabilityInfoContext().Create(p)));
        sb.Append(' ').Append(p.Name).Append(" {");
        if (p.GetMethod?.IsPublic == true)
            sb.Append(" get;");
        if (p.SetMethod?.IsPublic == true)
        {
            // init-only setters carry a modreq for IsExternalInit.
            var isInit = p.SetMethod.ReturnParameter
                .GetRequiredCustomModifiers()
                .Any(t => t.FullName == "System.Runtime.CompilerServices.IsExternalInit");
            sb.Append(isInit ? " init;" : " set;");
        }
        sb.Append(" }");
        FormatPropertyAttributes(sb, p);
        return sb.ToString();
    }

    private static void FormatPropertyAttributes(StringBuilder sb, PropertyInfo p)
    {
        var jpn = p.GetCustomAttribute<JsonPropertyNameAttribute>();
        if (jpn != null)
            sb.Append(" [JsonPropertyName(\"").Append(jpn.Name).Append("\")]");
        var jpo = p.GetCustomAttribute<JsonPropertyOrderAttribute>();
        if (jpo != null)
            sb.Append(" [JsonPropertyOrder(").Append(jpo.Order).Append(")]");
        var jig = p.GetCustomAttribute<JsonIgnoreAttribute>();
        if (jig != null)
            sb.Append(" [JsonIgnore(Condition=").Append(jig.Condition).Append(")]");
        var jr = p.GetCustomAttribute<JsonRequiredAttribute>();
        if (jr != null)
            sb.Append(" [JsonRequired]");
        var jc = p.GetCustomAttribute<JsonConverterAttribute>();
        if (jc != null)
            sb.Append(" [JsonConverter(").Append(FormatType(jc.ConverterType ?? typeof(object))).Append(")]");
    }

    private static string FormatField(FieldInfo f)
    {
        var sb = new StringBuilder("FIELD ");
        sb.Append(FormatTypeWithNullability(f.FieldType, new NullabilityInfoContext().Create(f)));
        sb.Append(' ').Append(f.Name);
        if (f.IsStatic)
            sb.Append(" static");
        if (f.IsInitOnly)
            sb.Append(" readonly");
        if (f.IsLiteral)
            sb.Append(" const");
        return sb.ToString();
    }

    private static string FormatMethod(MethodInfo m)
    {
        var sb = new StringBuilder("METHOD ");
        if (m.IsStatic)
            sb.Append("static ");
        if (m.IsAbstract)
            sb.Append("abstract ");
        else if (m.IsVirtual && !m.IsFinal)
            sb.Append("virtual ");
        sb.Append(FormatTypeWithNullability(m.ReturnType, new NullabilityInfoContext().Create(m.ReturnParameter)));
        sb.Append(' ').Append(m.Name);
        if (m.IsGenericMethodDefinition)
        {
            sb.Append('<').Append(string.Join(",", m.GetGenericArguments().Select(g => g.Name))).Append('>');
        }
        sb.Append('(');
        sb.Append(string.Join(", ", m.GetParameters().Select(FormatParameter)));
        sb.Append(')');
        return sb.ToString();
    }

    private static string FormatParameter(ParameterInfo p)
    {
        var sb = new StringBuilder();
        if (p.IsOut)
            sb.Append("out ");
        else if (p.ParameterType.IsByRef)
            sb.Append(p.IsIn ? "in " : "ref ");
        if (p.GetCustomAttributes(typeof(ParamArrayAttribute), false).Length > 0)
            sb.Append("params ");
        sb.Append(FormatTypeWithNullability(p.ParameterType, new NullabilityInfoContext().Create(p)));
        sb.Append(' ').Append(p.Name);
        if (p.HasDefaultValue)
        {
            sb.Append(" = ").Append(FormatDefault(p.DefaultValue));
        }
        return sb.ToString();
    }

    private static string FormatDefault(object? v)
    {
        if (v == null)
            return "null";
        if (v is string s)
            return "\"" + s + "\"";
        if (v is bool b)
            return b ? "true" : "false";
        return v.ToString() ?? "?";
    }

    private static string FormatTypeWithNullability(Type t, NullabilityInfo info)
    {
        var s = FormatType(t, includeNullable: true);
        // For reference types, append '?' when nullable annotation says so.
        if (!t.IsValueType && info.ReadState == NullabilityState.Nullable && !s.EndsWith('?'))
        {
            s += "?";
        }
        return s;
    }

    private static string FormatType(Type t, bool includeNullable = true)
    {
        if (t.IsByRef)
            return FormatType(t.GetElementType()!, includeNullable);
        if (t.IsArray)
            return FormatType(t.GetElementType()!, includeNullable) + "[]";
        if (Nullable.GetUnderlyingType(t) is { } u)
        {
            return includeNullable ? FormatType(u, includeNullable) + "?" : FormatType(u, false);
        }
        if (t.IsGenericType)
        {
            var def = t.GetGenericTypeDefinition();
            var name = StripGenericArity(def.FullName ?? def.Name);
            var args = string.Join(",", t.GetGenericArguments().Select(a => FormatType(a, includeNullable)));
            return $"{name}<{args}>";
        }
        if (t.IsGenericParameter)
            return t.Name;
        return t.FullName ?? t.Name;
    }

    private static string StripGenericArity(string s)
    {
        var idx = s.IndexOf('`');
        return idx < 0 ? s : s.Substring(0, idx);
    }
}
