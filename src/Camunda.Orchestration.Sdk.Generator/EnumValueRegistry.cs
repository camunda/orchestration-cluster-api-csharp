using System.Text.Json;

namespace Camunda.Orchestration.Sdk.Generator;

/// <summary>
/// Assigns <b>stable</b> integer values to generated enum members.
///
/// Generated enums serialize by name (<c>JsonStringEnumConverter</c> +
/// <c>JsonPropertyName</c>), but their implicit numeric values are baked into
/// consumer IL. When the upstream spec inserts a new member mid-list, every
/// later member's implicit value shifts, silently breaking any consumer that
/// cast/persisted the numeric value. To prevent that, member→int assignments
/// are persisted to a JSON manifest next to the generated output and reloaded
/// on each run:
/// <list type="bullet">
///   <item>existing members keep their assigned value (order-independent),</item>
///   <item>new members get the next free int (max + 1),</item>
///   <item>removed members are retained as tombstones so their int is never reused.</item>
/// </list>
/// The registry is process-global but reset per <see cref="LoadFrom"/> call so
/// that independent generation runs (e.g. unit tests against temp dirs) stay
/// isolated.
/// </summary>
internal static class EnumValueRegistry
{
    private static readonly Dictionary<string, Dictionary<string, int>> _map =
        new(StringComparer.Ordinal);
    private static string? _path;

    private static readonly JsonSerializerOptions _writeOptions = new() { WriteIndented = true };

    /// <summary>Reset in-memory state and load the manifest at <paramref name="path"/> if present.</summary>
    public static void LoadFrom(string path)
    {
        _map.Clear();
        _path = path;

        if (!File.Exists(path))
            return;

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, int>>>(
                File.ReadAllText(path));
            if (parsed != null)
            {
                foreach (var (enumType, members) in parsed)
                    _map[enumType] = new Dictionary<string, int>(members, StringComparer.Ordinal);
            }
        }
        catch (JsonException)
        {
            // Corrupt manifest → reseed from scratch (deterministic by call order).
            _map.Clear();
        }
    }

    /// <summary>
    /// Return the stable integer for <paramref name="wireValue"/> in
    /// <paramref name="enumType"/>, assigning the next free value if unseen.
    /// </summary>
    public static int Assign(string enumType, string wireValue)
    {
        if (!_map.TryGetValue(enumType, out var members))
        {
            members = new Dictionary<string, int>(StringComparer.Ordinal);
            _map[enumType] = members;
        }

        if (members.TryGetValue(wireValue, out var existing))
            return existing;

        var next = members.Count == 0 ? 0 : members.Values.Max() + 1;
        members[wireValue] = next;
        return next;
    }

    /// <summary>Persist the manifest (stable key order) to the path given to <see cref="LoadFrom"/>.</summary>
    public static void Save()
    {
        if (_path == null)
            return;

        var ordered = _map
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .ToDictionary(
                kv => kv.Key,
                kv => kv.Value
                    .OrderBy(m => m.Value)
                    .ToDictionary(m => m.Key, m => m.Value, StringComparer.Ordinal),
                StringComparer.Ordinal);

        var json = JsonSerializer.Serialize(ordered, _writeOptions);
        File.WriteAllText(_path, json + "\n");
    }
}
