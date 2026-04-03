using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Camunda.Orchestration.Sdk.Changelog;

/// <summary>
/// Generates Markdown and JSON reports from diff results.
/// </summary>
public static class Reporter
{
    // ── Markdown ─────────────────────────────────────────────

    public static string GenerateMarkdown(DiffResult diff)
    {
        var sb = new StringBuilder();

        sb.AppendLine(CultureInfo.InvariantCulture, $"# API Changelog: {diff.OldVersion} → {diff.NewVersion}");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"> **{diff.Breaking}** breaking, **{diff.Warnings}** warning, **{diff.Additive}** additive, **{diff.Info}** info — **{diff.Total}** total changes");
        sb.AppendLine();

        if (diff.Total == 0)
        {
            sb.AppendLine("No changes detected.");
            return sb.ToString();
        }

        // Group by severity
        var grouped = diff.Changes
            .GroupBy(c => c.Severity)
            .OrderBy(g => g.Key);

        foreach (var severityGroup in grouped)
        {
            var (icon, heading) = severityGroup.Key switch
            {
                Severity.Breaking => ("🔴", "Breaking Changes"),
                Severity.Warning => ("🟡", "Warnings"),
                Severity.Additive => ("🟢", "Additive Changes"),
                Severity.Info => ("ℹ️", "Informational"),
                _ => ("", "Other"),
            };

            sb.AppendLine(CultureInfo.InvariantCulture, $"## {icon} {heading}");
            sb.AppendLine();

            // Group by type within severity
            var byType = severityGroup
                .GroupBy(c => c.TypeName)
                .OrderBy(g => g.Key, StringComparer.Ordinal);

            foreach (var typeGroup in byType)
            {
                var typeName = typeGroup.Key;
                var role = typeGroup.First().Role;
                var roleLabel = role != TypeRole.Unknown ? $" `[{role.ToString().ToLowerInvariant()}]`" : "";

                // Determine type category for heading
                var category = GetTypeCategory(typeGroup);
                sb.AppendLine(CultureInfo.InvariantCulture, $"### `{typeName}`{category}{roleLabel}");
                sb.AppendLine();

                foreach (var change in typeGroup.OrderBy(c => c.Kind).ThenBy(c => c.Field))
                {
                    var changeIcon = GetChangeIcon(change.Kind);
                    sb.AppendLine(CultureInfo.InvariantCulture, $"- {changeIcon} {change.Detail}");
                }

                sb.AppendLine();
            }
        }

        // Summary table
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("| Category | Count |");
        sb.AppendLine("|----------|------:|");

        var kindCounts = diff.Changes
            .GroupBy(c => c.Kind)
            .OrderBy(g => g.Key)
            .Select(g => (Kind: FormatKind(g.Key), Count: g.Count()));

        foreach (var (kind, count) in kindCounts)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"| {kind} | {count} |");
        }

        sb.AppendLine(CultureInfo.InvariantCulture, $"| **Total** | **{diff.Total}** |");

        return sb.ToString();
    }

    private static string GetTypeCategory(IGrouping<string, Change> typeGroup)
    {
        var kinds = typeGroup.Select(c => c.Kind).ToHashSet();

        if (kinds.Contains(ChangeKind.EnumRemoved) || kinds.Contains(ChangeKind.EnumAdded) ||
            kinds.Contains(ChangeKind.EnumMemberRemoved) || kinds.Contains(ChangeKind.EnumMemberAdded))
            return " (enum)";

        if (kinds.Contains(ChangeKind.StructRemoved) || kinds.Contains(ChangeKind.StructAdded) ||
            kinds.Contains(ChangeKind.StructInterfaceRemoved) || kinds.Contains(ChangeKind.StructImplicitConversionRemoved))
            return " (struct)";

        if (kinds.Contains(ChangeKind.MethodRemoved) || kinds.Contains(ChangeKind.MethodAdded) ||
            kinds.Contains(ChangeKind.MethodReturnTypeChanged) || kinds.Contains(ChangeKind.MethodParameterChanged))
            return " (methods)";

        return "";
    }

    private static string GetChangeIcon(ChangeKind kind) => kind switch
    {
        ChangeKind.TypeRemoved => "❌",
        ChangeKind.TypeAdded => "✅",
        ChangeKind.PropertyRemoved => "❌",
        ChangeKind.PropertyAdded => "✅",
        ChangeKind.PropertyTypeChanged => "🔄",
        ChangeKind.PropertyBecameOptional => "◻️",
        ChangeKind.PropertyBecameRequired => "◼️",
        ChangeKind.PropertyJsonNameChanged => "🏷️",
        ChangeKind.EnumRemoved => "❌",
        ChangeKind.EnumAdded => "✅",
        ChangeKind.EnumMemberRemoved => "❌",
        ChangeKind.EnumMemberAdded => "✅",
        ChangeKind.EnumMemberMarkedObsolete => "⚠️",
        ChangeKind.EnumMemberJsonNameChanged => "🏷️",
        ChangeKind.StructRemoved => "❌",
        ChangeKind.StructAdded => "✅",
        ChangeKind.StructInterfaceRemoved => "❌",
        ChangeKind.StructInterfaceAdded => "✅",
        ChangeKind.StructImplicitConversionRemoved => "❌",
        ChangeKind.StructImplicitConversionAdded => "✅",
        ChangeKind.MethodRemoved => "❌",
        ChangeKind.MethodAdded => "✅",
        ChangeKind.MethodReturnTypeChanged => "🔄",
        ChangeKind.MethodParameterChanged => "🔄",
        ChangeKind.PolymorphicDiscriminatorChanged => "🔄",
        ChangeKind.PolymorphicDerivedTypeRemoved => "❌",
        ChangeKind.PolymorphicDerivedTypeAdded => "✅",
        ChangeKind.BaseClassChanged => "🔄",
        ChangeKind.InterfaceRemoved => "❌",
        ChangeKind.InterfaceAdded => "✅",
        _ => "•",
    };

    private static string FormatKind(ChangeKind kind) => kind switch
    {
        ChangeKind.TypeRemoved => "Type removed",
        ChangeKind.TypeAdded => "Type added",
        ChangeKind.PropertyRemoved => "Property removed",
        ChangeKind.PropertyAdded => "Property added",
        ChangeKind.PropertyTypeChanged => "Property type changed",
        ChangeKind.PropertyBecameOptional => "Property became optional",
        ChangeKind.PropertyBecameRequired => "Property became required",
        ChangeKind.PropertyJsonNameChanged => "Property JSON name changed",
        ChangeKind.EnumRemoved => "Enum removed",
        ChangeKind.EnumAdded => "Enum added",
        ChangeKind.EnumMemberRemoved => "Enum member removed",
        ChangeKind.EnumMemberAdded => "Enum member added",
        ChangeKind.EnumMemberMarkedObsolete => "Enum member marked obsolete",
        ChangeKind.EnumMemberJsonNameChanged => "Enum member JSON name changed",
        ChangeKind.StructRemoved => "Struct removed",
        ChangeKind.StructAdded => "Struct added",
        ChangeKind.StructInterfaceRemoved => "Struct interface removed",
        ChangeKind.StructInterfaceAdded => "Struct interface added",
        ChangeKind.StructImplicitConversionRemoved => "Implicit conversion removed",
        ChangeKind.StructImplicitConversionAdded => "Implicit conversion added",
        ChangeKind.MethodRemoved => "Method removed",
        ChangeKind.MethodAdded => "Method added",
        ChangeKind.MethodReturnTypeChanged => "Method return type changed",
        ChangeKind.MethodParameterChanged => "Method parameter changed",
        ChangeKind.PolymorphicDiscriminatorChanged => "Polymorphic discriminator changed",
        ChangeKind.PolymorphicDerivedTypeRemoved => "Polymorphic derived type removed",
        ChangeKind.PolymorphicDerivedTypeAdded => "Polymorphic derived type added",
        ChangeKind.BaseClassChanged => "Base class changed",
        ChangeKind.InterfaceRemoved => "Interface removed",
        ChangeKind.InterfaceAdded => "Interface added",
        _ => kind.ToString(),
    };

    // ── JSON ─────────────────────────────────────────────────

    public static string GenerateJson(DiffResult diff)
    {
        var report = new JsonReport
        {
            OldVersion = diff.OldVersion,
            NewVersion = diff.NewVersion,
            Stats = new JsonStats
            {
                Breaking = diff.Breaking,
                Warning = diff.Warnings,
                Additive = diff.Additive,
                Info = diff.Info,
                Total = diff.Total,
            },
            Changes = diff.Changes.Select(c => new JsonChange
            {
                Kind = c.Kind.ToString(),
                Severity = c.Severity.ToString().ToLowerInvariant(),
                Type = c.TypeName,
                Field = c.Field,
                Old = c.OldValue,
                New = c.NewValue,
                Detail = c.Detail,
                Role = c.Role.ToString().ToLowerInvariant(),
            }).ToList(),
        };

        return JsonSerializer.Serialize(report, JsonContext.Default.JsonReport);
    }
}

// JSON serialization types

internal sealed class JsonReport
{
    [JsonPropertyName("oldVersion")]
    public required string OldVersion { get; set; }

    [JsonPropertyName("newVersion")]
    public required string NewVersion { get; set; }

    [JsonPropertyName("stats")]
    public required JsonStats Stats { get; set; }

    [JsonPropertyName("changes")]
    public required List<JsonChange> Changes { get; set; }
}

internal sealed class JsonStats
{
    [JsonPropertyName("breaking")]
    public int Breaking { get; set; }

    [JsonPropertyName("warning")]
    public int Warning { get; set; }

    [JsonPropertyName("additive")]
    public int Additive { get; set; }

    [JsonPropertyName("info")]
    public int Info { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }
}

internal sealed class JsonChange
{
    [JsonPropertyName("kind")]
    public required string Kind { get; set; }

    [JsonPropertyName("severity")]
    public required string Severity { get; set; }

    [JsonPropertyName("type")]
    public required string Type { get; set; }

    [JsonPropertyName("field")]
    public string? Field { get; set; }

    [JsonPropertyName("old")]
    public string? Old { get; set; }

    [JsonPropertyName("new")]
    public string? New { get; set; }

    [JsonPropertyName("detail")]
    public required string Detail { get; set; }

    [JsonPropertyName("role")]
    public required string Role { get; set; }
}

[JsonSerializable(typeof(JsonReport))]
[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class JsonContext : JsonSerializerContext { }
