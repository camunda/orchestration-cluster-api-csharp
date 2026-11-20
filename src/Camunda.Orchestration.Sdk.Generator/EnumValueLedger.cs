using System.Text.Json;

namespace Camunda.Orchestration.Sdk.Generator;

/// <summary>
/// Append-only ledger of numeric values for generated enum members.
/// </summary>
/// <remarks>
/// The upstream spec lists enum values alphabetically, so emitting members
/// positionally would shift the numeric value of every member after a
/// mid-list insert — a binary-breaking change for code compiled against the
/// old values or storing them as <c>int</c>. The ledger pins each
/// (enum type, wire value) pair to the number it was first assigned; new
/// members get <c>max + 1</c>. Entries are never removed, so a value is never
/// reused even if a member disappears from the spec.
/// </remarks>
internal sealed class EnumValueLedger
{
    private readonly SortedDictionary<string, SortedDictionary<string, int>> _values;

    private EnumValueLedger(SortedDictionary<string, SortedDictionary<string, int>> values)
    {
        _values = values;
    }

    private static readonly JsonSerializerOptions s_writeOptions = new() { WriteIndented = true };

    public bool Changed { get; private set; }

    public static EnumValueLedger Load(string path)
    {
        var values = new SortedDictionary<string, SortedDictionary<string, int>>(StringComparer.Ordinal);
        if (File.Exists(path))
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, int>>>(File.ReadAllText(path))
                ?? [];
            foreach (var (enumName, members) in parsed)
            {
                values[enumName] = new SortedDictionary<string, int>(members, StringComparer.Ordinal);
            }
        }
        return new EnumValueLedger(values);
    }

    /// <summary>
    /// Returns the stable numeric value for each wire value of <paramref name="enumTypeName"/>,
    /// in the given order, assigning new values (max + 1) to members not yet in the ledger.
    /// </summary>
    public IReadOnlyList<int> Assign(string enumTypeName, IReadOnlyList<string> wireValues)
    {
        if (!_values.TryGetValue(enumTypeName, out var members))
        {
            members = new SortedDictionary<string, int>(StringComparer.Ordinal);
            _values[enumTypeName] = members;
        }

        var next = members.Count == 0 ? 0 : members.Values.Max() + 1;
        var result = new List<int>(wireValues.Count);
        foreach (var wire in wireValues)
        {
            if (!members.TryGetValue(wire, out var value))
            {
                value = next++;
                members[wire] = value;
                Changed = true;
            }
            result.Add(value);
        }
        return result;
    }

    public void Save(string path)
    {
        var json = JsonSerializer.Serialize(_values, s_writeOptions);
        File.WriteAllText(path, json.ReplaceLineEndings("\n") + "\n");
    }
}
