using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.OpenApi;

namespace Camunda.Orchestration.Sdk.Generator;

/// <summary>
/// Clearable date-time properties: a date the API clears when it receives an empty string,
/// while <c>null</c> or omitting the property preserves the persisted value
/// (<c>JobResultCorrections</c> and the user-task update <c>Changeset</c>).
///
/// <para>The specification marks them with <c>x-clearable: true</c> on the shared
/// <c>ClearableDateTime</c> schema (camunda/camunda#68710), whose <c>anyOf</c> of a date-time
/// and a <c>maxLength: 0</c> string would otherwise generate a string-valued
/// <c>ICamundaKey</c> struct. Older specifications declare the same
/// properties as a plain <c>format: date-time</c> and say so only in prose ("clear/reset by
/// providing an empty String"). Either way the property keeps its
/// <see cref="DateTimeOffset"/>? type, so existing callers keep compiling, and gains a
/// <c>Clear&lt;Property&gt;()</c> / <c>Is&lt;Property&gt;Cleared</c> pair. The JSON binding moves
/// to an internal <c>ClearableDateTimeWire</c>-typed property that writes <c>""</c> for a
/// clear and the date exactly as before otherwise.</para>
/// </summary>
internal static partial class CSharpClientGenerator
{
    private const string ClearableExtension = "x-clearable";

    // Owning-schema description bullet: "* `dueDate` - clear by providing an empty String".
    private static readonly Regex ClearableBullet = new(
        @"`(?<prop>\w+)`\s*-\s*(?:clear|reset)\s+by\s+providing\s+an\s+empty\s+String",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Property description: "The due date of the task. Reset by providing an empty String."
    private static readonly Regex ClearableSentence = new(
        @"\b(?:clear|reset)\s+by\s+providing\s+an\s+empty\s+String",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// True when the schema itself carries <c>x-clearable: true</c>.
    /// </summary>
    internal static bool HasClearableMarker(IOpenApiSchema? schema) =>
        schema?.Extensions is { } extensions
        && extensions.TryGetValue(ClearableExtension, out var ext)
        && ext is JsonNodeExtension { Node: JsonValue value }
        && value.TryGetValue<bool>(out var flag)
        && flag;

    /// <summary>
    /// Resolves <paramref name="schema"/> to the <c>x-clearable</c> schema it is, references,
    /// or wraps in a single-element <c>allOf</c> (the description-overlay pattern). Returns
    /// <c>null</c> when it is not clearable.
    /// </summary>
    private static IOpenApiSchema? ResolveClearableSchema(IOpenApiSchema? schema, OpenApiDocument? doc, int depth = 0)
    {
        if (schema == null || depth > 8)
            return null;

        var refId = GetRefId(schema);
        if (refId != null)
        {
            var target = doc?.Components?.Schemas != null
                && doc.Components.Schemas.TryGetValue(refId, out var resolved)
                ? resolved
                : null;
            if (HasClearableMarker(target) || (target == null && HasClearableMarker(schema)))
                return target ?? schema;
            return target != null ? ResolveClearableSchema(target, doc, depth + 1) : null;
        }

        if (HasClearableMarker(schema))
            return schema;

        if (schema.AllOf is { Count: 1 } && (schema.Properties == null || schema.Properties.Count == 0))
            return ResolveClearableSchema(schema.AllOf[0], doc, depth + 1);

        return null;
    }

    /// <summary>
    /// True when an <c>x-clearable</c> schema clears a date-time: it is itself
    /// <c>format: date-time</c>, or one of its <c>anyOf</c>/<c>oneOf</c> branches is.
    /// </summary>
    private static bool IsDateTimeClearable(IOpenApiSchema clearable, OpenApiDocument? doc)
    {
        static bool IsDateTime(IOpenApiSchema? s) => s?.Format == "date-time";

        if (IsDateTime(clearable))
            return true;
        foreach (var branch in (clearable.AnyOf ?? []).Concat(clearable.OneOf ?? []))
        {
            var refId = GetRefId(branch);
            var resolved = refId != null && doc?.Components?.Schemas != null
                && doc.Components.Schemas.TryGetValue(refId, out var r) ? r : branch;
            if (IsDateTime(resolved))
                return true;
        }
        return false;
    }

    /// <summary>
    /// True when <paramref name="propSchema"/>, declared as <paramref name="propName"/> on a
    /// schema described by <paramref name="ownerDescription"/>, is a clearable date-time.
    /// Throws for an <c>x-clearable</c> property that is not a date-time: the generator has no
    /// representation for it, and silently typing it as a key would repeat the defect.
    /// </summary>
    internal static bool IsClearableDateTimeProperty(
        IOpenApiSchema propSchema, OpenApiDocument? doc, string? ownerDescription, string propName, string ownerTypeName)
    {
        var clearable = ResolveClearableSchema(propSchema, doc);
        if (clearable != null)
        {
            if (!IsDateTimeClearable(clearable, doc))
            {
                throw new InvalidOperationException(
                    $"[generator] {ownerTypeName}.{propName} is marked {ClearableExtension} but is not a date-time. " +
                    "Only clearable date-times are supported; extend CSharpClientGenerator.ClearableDateTime.cs.");
            }
            return true;
        }

        // Older specifications: a plain date-time the prose says is cleared with "".
        if (GetRefId(propSchema) != null || SchemaTypeName(propSchema) != "string" || propSchema.Format != "date-time")
            return false;
        if (propSchema.Description is { } d && ClearableSentence.IsMatch(d))
            return true;
        return ownerDescription != null
            && ClearableBullet.Matches(ownerDescription).Any(m => m.Groups["prop"].Value == propName);
    }

    /// <summary>
    /// Maps a reference to (or single-<c>allOf</c> wrapper of) an <c>x-clearable</c> date-time
    /// schema to <c>DateTimeOffset</c>, so it never surfaces as a key struct.
    /// </summary>
    private static bool TryMapClearableDateTime(IOpenApiSchema schema, OpenApiDocument? doc)
    {
        var clearable = ResolveClearableSchema(schema, doc);
        return clearable != null && IsDateTimeClearable(clearable, doc);
    }

    /// <summary>
    /// Emits a clearable date-time property: a <c>DateTimeOffset?</c> property and a
    /// <c>Clear&lt;Prop&gt;()</c> / <c>Is&lt;Prop&gt;Cleared</c> pair, all excluded from JSON, plus
    /// the internal JSON-bound <c>&lt;Prop&gt;Wire</c>. Setting the date and clearing it each
    /// replace the other: the last call wins.
    /// </summary>
    private static void EmitClearableDateTimeProperty(
        StringBuilder sb, string typeName, string propName, string csharpPropName, string? description)
    {
        var field = "_" + char.ToLowerInvariant(csharpPropName[0]) + csharpPropName[1..];
        var clearedField = field + "Cleared";
        var wireType = "global::Camunda.Orchestration.Sdk.ClearableDateTimeWire";

        sb.AppendLine($"    private DateTimeOffset? {field};");
        sb.AppendLine($"    private bool {clearedField};");
        sb.AppendLine();

        sb.AppendLine("    /// <summary>");
        if (!string.IsNullOrEmpty(description))
            AppendXmlDocLines(sb, description);
        else
            sb.AppendLine($"    /// {SafeEmit.SafeXmlDocText(csharpPropName)}.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    /// <remarks>");
        sb.AppendLine("    /// <c>null</c> (the default) sends no value, which preserves the persisted value.");
        sb.AppendLine($"    /// To clear it, call <see cref=\"Clear{csharpPropName}\"/>; assigning this property");
        sb.AppendLine("    /// afterwards replaces the clear.");
        sb.AppendLine("    /// </remarks>");
        sb.AppendLine("    [JsonIgnore]");
        sb.AppendLine($"    public DateTimeOffset? {csharpPropName}");
        sb.AppendLine("    {");
        sb.AppendLine($"        get => {field};");
        sb.AppendLine("        set");
        sb.AppendLine("        {");
        sb.AppendLine($"            {field} = value;");
        sb.AppendLine($"            {clearedField} = false;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.AppendLine("    /// <summary>");
        sb.AppendLine($"    /// True when <see cref=\"{csharpPropName}\"/> is to be cleared, i.e. <see cref=\"Clear{csharpPropName}\"/>");
        sb.AppendLine("    /// was the last change made to it. A cleared value is sent as an empty string.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    [JsonIgnore]");
        sb.AppendLine($"    public bool Is{csharpPropName}Cleared => {clearedField};");
        sb.AppendLine();

        sb.AppendLine("    /// <summary>");
        sb.AppendLine($"    /// Clears the persisted <c>{SafeEmit.SafeXmlDocText(propName)}</c>: it is sent as an empty string, which the API");
        sb.AppendLine("    /// reads as \"remove the value\" (<c>null</c> or omitting it instead preserves the value).");
        sb.AppendLine($"    /// Sets <see cref=\"{csharpPropName}\"/> to <c>null</c>; assigning it afterwards replaces the clear.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    /// <returns>This instance, for chaining.</returns>");
        sb.AppendLine($"    public {typeName} Clear{csharpPropName}()");
        sb.AppendLine("    {");
        sb.AppendLine($"        {field} = null;");
        sb.AppendLine($"        {clearedField} = true;");
        sb.AppendLine("        return this;");
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.AppendLine("    [JsonInclude]");
        sb.AppendLine($"    [JsonPropertyName(\"{SafeEmit.SafeCSharpStringLiteral(propName)}\")]");
        sb.AppendLine($"    internal {wireType}? {csharpPropName}Wire");
        sb.AppendLine("    {");
        sb.AppendLine($"        get => {wireType}.From({field}, {clearedField});");
        sb.AppendLine("        set");
        sb.AppendLine("        {");
        sb.AppendLine($"            {field} = value?.Value;");
        sb.AppendLine($"            {clearedField} = value?.IsCleared == true;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
    }
}
