using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Camunda.Orchestration.Sdk.Tests.Internals;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Behavioural guard for clearable date-time properties: dates the API clears when it
/// receives an empty string, while <c>null</c> or omitting the property preserves the
/// persisted value (<c>JobResultCorrections</c> and the user-task update <c>Changeset</c>,
/// marked <c>x-clearable</c> upstream in camunda/camunda#68710).
///
/// <para>The defect class: a clearable date typed only as <see cref="DateTimeOffset"/>? cannot
/// express the clear (no <see cref="DateTimeOffset"/> serialises as <c>""</c>), and the
/// <c>ClearableDateTime</c> schema generated naively becomes a string-valued key struct that
/// no longer accepts a <see cref="DateTimeOffset"/>. Every clearable property must keep its
/// <see cref="DateTimeOffset"/>? type, write a set date exactly as a plain
/// <see cref="DateTimeOffset"/>? does, omit <c>null</c>, write <c>""</c> after a clear, and read
/// <c>""</c> back as a clear.</para>
/// </summary>
public class ClearableDateTimeSerializationTests
{
    private static readonly JsonSerializerOptions Options = SdkJsonOptions.Create();

    private static readonly DateTimeOffset Date = new(2026, 11, 13, 9, 30, 15, 123, TimeSpan.FromHours(13));

    // ── JobResultCorrections ────────────────────────────────────────────────

    [Fact]
    public void JobResultCorrections_set_date_serializes_as_before()
    {
        var corrections = new JobResultCorrections { DueDate = Date, FollowUpDate = Date };

        var json = JsonSerializer.Serialize(corrections, Options);

        var expected = JsonSerializer.Serialize(Date, Options);
        Assert.Equal($"{{\"dueDate\":{expected},\"followUpDate\":{expected}}}", json);
        Assert.False(corrections.IsDueDateCleared);
    }

    [Fact]
    public void JobResultCorrections_clear_serializes_empty_string()
    {
        var corrections = new JobResultCorrections().ClearDueDate().ClearFollowUpDate();

        Assert.True(corrections.IsDueDateCleared);
        Assert.True(corrections.IsFollowUpDateCleared);
        Assert.Null(corrections.DueDate);
        Assert.Equal("{\"dueDate\":\"\",\"followUpDate\":\"\"}", JsonSerializer.Serialize(corrections, Options));
    }

    [Fact]
    public void JobResultCorrections_unset_preserves_by_omitting()
    {
        var corrections = new JobResultCorrections { Assignee = "demo", DueDate = null };

        Assert.Equal("{\"assignee\":\"demo\"}", JsonSerializer.Serialize(corrections, Options));
        Assert.False(corrections.IsDueDateCleared);
    }

    [Fact]
    public void JobResultCorrections_empty_string_deserializes_as_cleared()
    {
        var corrections = JsonSerializer.Deserialize<JobResultCorrections>(
            "{\"dueDate\":\"\",\"followUpDate\":\"2026-11-13T09:30:15.123+13:00\"}", Options)!;

        Assert.True(corrections.IsDueDateCleared);
        Assert.Null(corrections.DueDate);
        Assert.False(corrections.IsFollowUpDateCleared);
        Assert.Equal(Date, corrections.FollowUpDate);
    }

    // ── Changeset ───────────────────────────────────────────────────────────

    [Fact]
    public void Changeset_set_date_serializes_as_before()
    {
        var changeset = new Changeset { DueDate = Date };

        Assert.Equal(
            $"{{\"dueDate\":{JsonSerializer.Serialize(Date, Options)}}}",
            JsonSerializer.Serialize(changeset, Options));
    }

    [Fact]
    public void Changeset_clear_serializes_empty_string()
    {
        var changeset = new Changeset { Priority = 10 }.ClearFollowUpDate();

        Assert.Equal("{\"followUpDate\":\"\",\"priority\":10}", JsonSerializer.Serialize(changeset, Options));
    }

    [Fact]
    public void Changeset_unset_preserves_by_omitting()
    {
        Assert.Equal("{}", JsonSerializer.Serialize(new Changeset(), Options));
    }

    [Fact]
    public void Changeset_round_trips_clear_date_and_null()
    {
        var original = new Changeset { DueDate = Date }.ClearFollowUpDate();

        var json = JsonSerializer.Serialize(original, Options);
        var read = JsonSerializer.Deserialize<Changeset>(json, Options)!;

        Assert.Equal(Date, read.DueDate);
        Assert.False(read.IsDueDateCleared);
        Assert.True(read.IsFollowUpDateCleared);

        var preserved = JsonSerializer.Deserialize<Changeset>("{\"dueDate\":null}", Options)!;
        Assert.Null(preserved.DueDate);
        Assert.False(preserved.IsDueDateCleared);
    }

    // ── Last call wins ──────────────────────────────────────────────────────

    [Fact]
    public void Setting_after_clear_replaces_the_clear()
    {
        var changeset = new Changeset().ClearDueDate();
        changeset.DueDate = Date;

        Assert.False(changeset.IsDueDateCleared);
        Assert.Equal(
            $"{{\"dueDate\":{JsonSerializer.Serialize(Date, Options)}}}",
            JsonSerializer.Serialize(changeset, Options));
    }

    [Fact]
    public void Clearing_after_set_replaces_the_date()
    {
        var corrections = new JobResultCorrections { DueDate = Date }.ClearDueDate();

        Assert.Null(corrections.DueDate);
        Assert.Equal("{\"dueDate\":\"\"}", JsonSerializer.Serialize(corrections, Options));
    }

    [Fact]
    public void Assigning_null_after_clear_preserves()
    {
        var corrections = new JobResultCorrections().ClearDueDate();
        corrections.DueDate = null;

        Assert.False(corrections.IsDueDateCleared);
        Assert.Equal("{}", JsonSerializer.Serialize(corrections, Options));
    }

    [Fact]
    public void Default_options_without_null_omission_still_preserve_with_null()
    {
        // A caller serialising with default options gets "dueDate": null for an unset date,
        // exactly as with the plain DateTimeOffset? property this replaced: null preserves.
        var json = JsonSerializer.Serialize(new Changeset { Priority = 1 }.ClearFollowUpDate());

        var node = JsonNode.Parse(json)!.AsObject();
        Assert.Null(node["DueDate"]);
        Assert.Equal("", node["followUpDate"]!.GetValue<string>());
        Assert.True(node.ContainsKey("dueDate"));
        Assert.Null(node["dueDate"]);
    }

    // ── Defect-class sweep against the real bundled spec ───────────────────

    /// <summary>
    /// Every property the bundled spec declares clearable — an <c>x-clearable</c> schema reached
    /// directly, by <c>$ref</c> or through a single-element <c>allOf</c>, or (older specs) a
    /// date-time the prose says is cleared "by providing an empty String" — must be generated
    /// as a <see cref="DateTimeOffset"/>? with a working clear, and no <c>x-clearable</c> schema
    /// may surface as a type of its own.
    /// </summary>
    [Fact]
    public void Every_clearable_property_in_the_bundled_spec_is_a_clearable_date()
    {
        var spec = LoadBundledSpec();
        var schemas = spec["components"]!["schemas"]!.AsObject();
        var assembly = typeof(CamundaClient).Assembly;

        var clearableComponents = schemas
            .Where(kv => kv.Value?["x-clearable"]?.GetValue<bool>() == true)
            .Select(kv => kv.Key)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var name in clearableComponents)
        {
            Assert.True(
                assembly.GetType($"Camunda.Orchestration.Sdk.{name}") == null,
                $"x-clearable schema '{name}' must not be generated as a type of its own.");
        }

        var found = new List<string>();
        foreach (var (schemaName, schemaNode) in schemas)
        {
            if (schemaNode?["properties"] is not JsonObject props)
                continue;
            var ownerDescription = schemaNode["description"]?.GetValue<string>() ?? "";

            foreach (var (propName, propNode) in props)
            {
                if (propNode is not JsonObject prop || !IsClearable(prop, propName, ownerDescription, clearableComponents))
                    continue;

                found.Add($"{schemaName}.{propName}");
                AssertClearableDate(assembly, schemaName, propName);
            }
        }

        Assert.Contains("JobResultCorrections.dueDate", found);
        Assert.Contains("JobResultCorrections.followUpDate", found);
        Assert.Contains("Changeset.dueDate", found);
        Assert.Contains("Changeset.followUpDate", found);
    }

    private static bool IsClearable(JsonObject prop, string propName, string ownerDescription, HashSet<string> clearableComponents)
    {
        if (prop["x-clearable"]?.GetValue<bool>() == true)
            return true;
        if (RefName(prop) is { } direct && clearableComponents.Contains(direct))
            return true;
        if (prop["allOf"] is JsonArray { Count: 1 } allOf && allOf[0] is JsonObject only
            && RefName(only) is { } wrapped && clearableComponents.Contains(wrapped))
            return true;

        if (prop["format"]?.GetValue<string>() != "date-time")
            return false;
        var description = prop["description"]?.GetValue<string>() ?? "";
        return System.Text.RegularExpressions.Regex.IsMatch(
                   description, @"\b(?:clear|reset)\s+by\s+providing\s+an\s+empty\s+String",
                   System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            || System.Text.RegularExpressions.Regex.IsMatch(
                   ownerDescription,
                   $@"`{System.Text.RegularExpressions.Regex.Escape(propName)}`\s*-\s*(?:clear|reset)\s+by\s+providing\s+an\s+empty\s+String",
                   System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static string? RefName(JsonObject node) =>
        node["$ref"]?.GetValue<string>() is { } r && r.StartsWith("#/components/schemas/", StringComparison.Ordinal)
            ? r["#/components/schemas/".Length..]
            : null;

    private static void AssertClearableDate(Assembly assembly, string schemaName, string propName)
    {
        var where = $"{schemaName}.{propName}";
        var type = assembly.GetType($"Camunda.Orchestration.Sdk.{schemaName}");
        Assert.True(type != null, $"{where}: no generated type '{schemaName}'.");

        var pascal = char.ToUpperInvariant(propName[0]) + propName[1..];
        var property = type!.GetProperty(pascal, BindingFlags.Public | BindingFlags.Instance);
        Assert.True(property?.PropertyType == typeof(DateTimeOffset?), $"{where}: expected a DateTimeOffset? property '{pascal}'.");
        Assert.True(property!.CanWrite, $"{where}: '{pascal}' must stay assignable.");

        var clear = type.GetMethod($"Clear{pascal}", BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes);
        Assert.True(clear != null, $"{where}: expected a public Clear{pascal}() method.");
        var isCleared = type.GetProperty($"Is{pascal}Cleared", BindingFlags.Public | BindingFlags.Instance);
        Assert.True(isCleared?.PropertyType == typeof(bool), $"{where}: expected a bool Is{pascal}Cleared property.");

        var instance = Activator.CreateInstance(type)!;
        Assert.DoesNotContain($"\"{propName}\"", JsonSerializer.Serialize(instance, type, Options));

        property.SetValue(instance, Date);
        Assert.Contains(
            $"\"{propName}\":{JsonSerializer.Serialize(Date, Options)}",
            JsonSerializer.Serialize(instance, type, Options));

        clear!.Invoke(instance, null);
        Assert.True((bool)isCleared!.GetValue(instance)!, $"{where}: Clear{pascal}() must mark it cleared.");
        var clearedJson = JsonNode.Parse(JsonSerializer.Serialize(instance, type, Options))!;
        Assert.Equal("", clearedJson[propName]!.GetValue<string>());

        var read = JsonSerializer.Deserialize($"{{\"{propName}\":\"\"}}", type, Options)!;
        Assert.True((bool)isCleared.GetValue(read)!, $"{where}: \"\" must deserialize as a clear.");
    }

    private static JsonNode LoadBundledSpec()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++)
        {
            var candidate = Path.Combine(dir, "external-spec", "bundled", "rest-api.bundle.json");
            if (File.Exists(candidate))
                return JsonNode.Parse(File.ReadAllText(candidate))!;
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException("Could not locate external-spec/bundled/rest-api.bundle.json");
    }
}
