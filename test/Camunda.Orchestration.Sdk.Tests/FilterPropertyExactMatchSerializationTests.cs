using System.Text.Json;
using Camunda.Orchestration.Sdk;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Behavioural guard (against the generated SDK) for advanced-filter <c>oneOf</c>
/// properties: the exact-match (bare scalar) branch and the advanced (operator
/// object) branch must both be expressible and must round-trip to the correct wire
/// shape. The bare form is what servers predating advanced filtering on a field
/// accept, so this is what keeps a newer SDK working against an older server
/// (see #267). Uses <c>ProcessInstanceKeyFilterProperty</c> as the representative.
/// </summary>
public class FilterPropertyExactMatchSerializationTests
{
    private const string Key = "2251799813685261";

    // Same key converters the client registers on its serializer options; the
    // generated filter converter delegates key (de)serialization to these.
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters =
        {
            new CamundaKeyJsonConverterFactory(),
            new CamundaLongKeyJsonConverterFactory(),
        },
    };

    [Fact]
    public void Exact_match_serializes_as_bare_scalar()
    {
        // Implicit conversion from the key — the original (pre-advanced-filter) call shape.
        ProcessInstanceKeyFilterProperty filter = ProcessInstanceKey.AssumeExists(Key);
        Assert.Equal($"\"{Key}\"", JsonSerializer.Serialize(filter, Options));
    }

    [Fact]
    public void Advanced_operator_serializes_as_object()
    {
        var filter = new ProcessInstanceKeyFilterProperty { Eq = ProcessInstanceKey.AssumeExists(Key) };
        Assert.Equal($"{{\"$eq\":\"{Key}\"}}", JsonSerializer.Serialize(filter, Options));
    }

    [Fact]
    public void Bare_scalar_deserializes_to_exact_match()
    {
        var filter = JsonSerializer.Deserialize<ProcessInstanceKeyFilterProperty>($"\"{Key}\"", Options);
        Assert.NotNull(filter);
        Assert.Equal(Key, filter!.ExactMatch?.Value);
        Assert.Null(filter.Eq);
    }

    [Fact]
    public void Object_deserializes_to_advanced_operator()
    {
        var filter = JsonSerializer.Deserialize<ProcessInstanceKeyFilterProperty>($"{{\"$eq\":\"{Key}\"}}", Options);
        Assert.NotNull(filter);
        Assert.Equal(Key, filter!.Eq?.Value);
        Assert.Null(filter.ExactMatch);
    }

    [Fact]
    public void Setting_both_branches_throws()
    {
        // oneOf semantics: exactly one branch. Populating both is ambiguous and must fail
        // loudly rather than silently dropping ExactMatch.
        var filter = new ProcessInstanceKeyFilterProperty
        {
            ExactMatch = ProcessInstanceKey.AssumeExists(Key),
            Eq = ProcessInstanceKey.AssumeExists(Key),
        };
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(filter, Options));
    }
}
