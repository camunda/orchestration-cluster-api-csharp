using System.Text.Json;
using System.Text.Json.Serialization;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Unit tests for the pure (I/O-free) core of the DTO-driven typed variable map feature:
/// DTO field extraction, the incremental collector, value parsing, and <see cref="VariableMap{T}"/>.
/// </summary>
public class TypedVariableSearchTests
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly string[] s_orderIdOnly = ["orderId"];

    // ---- Sample DTOs ----

    public record OrderVars(string OrderId, decimal? Amount);

    public record AliasedVars([property: JsonPropertyName("order_id")] string OrderId);

    public record AllOptional(string? Note, int? Count);

    public record RequiredValueType(int Quantity, string? Label);

    // ---- ExtractFields: naming ----

    [Fact]
    public void ExtractFields_AppliesCamelCaseNamingPolicy()
    {
        var fields = TypedVariableSearch.ExtractFields(typeof(OrderVars), s_options);

        Assert.Equal(
            ["amount", "orderId"],
            fields.Select(f => f.VariableName).OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void ExtractFields_HonorsJsonPropertyName()
    {
        var fields = TypedVariableSearch.ExtractFields(typeof(AliasedVars), s_options);

        Assert.Equal("order_id", Assert.Single(fields).VariableName);
    }

    // ---- ExtractFields: required detection (nullability-driven) ----

    [Fact]
    public void ExtractFields_NonNullableReferenceIsRequired()
    {
        var fields = TypedVariableSearch.ExtractFields(typeof(OrderVars), s_options);

        var orderId = fields.Single(f => f.VariableName == "orderId");
        Assert.True(orderId.Required);
    }

    [Fact]
    public void ExtractFields_NullableMemberIsOptional()
    {
        var fields = TypedVariableSearch.ExtractFields(typeof(OrderVars), s_options);

        var amount = fields.Single(f => f.VariableName == "amount");
        Assert.False(amount.Required);
    }

    [Fact]
    public void ExtractFields_NonNullableValueTypeIsRequired()
    {
        var fields = TypedVariableSearch.ExtractFields(typeof(RequiredValueType), s_options);

        Assert.True(fields.Single(f => f.VariableName == "quantity").Required);
        Assert.False(fields.Single(f => f.VariableName == "label").Required);
    }

    [Fact]
    public void ExtractFields_AllOptionalDtoHasNoRequiredFields()
    {
        var fields = TypedVariableSearch.ExtractFields(typeof(AllOptional), s_options);

        Assert.All(fields, f => Assert.False(f.Required));
    }

    // ---- VariableCollector: incremental folding ----

    [Fact]
    public void Collector_RetainsOnlyDeclaredNames()
    {
        var collector = new TypedVariableSearch.VariableCollector(s_orderIdOnly);
        collector.Ingest(new[]
        {
            Result("orderId", "\"ord-1\"", "100"),
            Result("undeclared", "\"junk\"", "100"),
        });

        var raw = collector.Build();

        Assert.True(raw.ContainsKey("orderId"));
        Assert.False(raw.ContainsKey("undeclared"));
    }

    [Fact]
    public void Collector_FirstValuePerNameWins_AcrossPages()
    {
        var collector = new TypedVariableSearch.VariableCollector(s_orderIdOnly);
        collector.Ingest(new[] { Result("orderId", "\"first\"", "100") });
        collector.Ingest(new[] { Result("orderId", "\"second\"", "100") });

        var raw = collector.Build();

        Assert.Equal("first", raw["orderId"].GetString());
    }

    [Fact]
    public void Collector_SameNameAtMultipleScopes_Throws()
    {
        var collector = new TypedVariableSearch.VariableCollector(s_orderIdOnly);
        collector.Ingest(new[]
        {
            Result("orderId", "\"a\"", "100"),
            Result("orderId", "\"b\"", "200"),
        });

        var ex = Assert.Throws<VariableScopeCollisionException>(() => collector.Build());
        Assert.Equal("orderId", ex.VariableName);
        Assert.Equal(["100", "200"], ex.ScopeKeys.ToArray());
    }

    [Fact]
    public void Collector_MalformedJsonValue_Throws()
    {
        var collector = new TypedVariableSearch.VariableCollector(s_orderIdOnly);
        collector.Ingest(new[] { Result("orderId", "not-json", "100") });

        var ex = Assert.Throws<VariableDeserializationException>(() => collector.Build());
        Assert.Equal("orderId", ex.VariableName);
    }

    [Fact]
    public void Collector_NullValue_ThrowsVariableDeserialization()
    {
        var collector = new TypedVariableSearch.VariableCollector(s_orderIdOnly);
        collector.Ingest(new[] { Result("orderId", null!, "100") });

        var ex = Assert.Throws<VariableDeserializationException>(() => collector.Build());
        Assert.Equal("orderId", ex.VariableName);
    }

    [Fact]
    public void Collector_SameNameSameScopeAcrossPages_IsNotACollision()
    {
        var collector = new TypedVariableSearch.VariableCollector(s_orderIdOnly);
        collector.Ingest(new[] { Result("orderId", "\"a\"", "100") });
        collector.Ingest(new[] { Result("orderId", "\"a\"", "100") });

        var raw = collector.Build();

        Assert.Equal("a", raw["orderId"].GetString());
    }

    // ---- VariableMap: lenient + strict access ----

    [Fact]
    public void VariableMap_Get_ReturnsNullForAbsentVariable()
    {
        var map = MapOf(("orderId", "\"ord-1\""));

        Assert.Null(map.Get("missing"));
        Assert.NotNull(map.Get("orderId"));
    }

    [Fact]
    public void VariableMap_GetTyped_DeserializesPresentValue()
    {
        var map = MapOf(("amount", "9.99"));

        Assert.Equal(9.99m, map.Get<decimal>("amount"));
        Assert.Equal(default, map.Get<decimal>("missing"));
    }

    [Fact]
    public void VariableMap_Validate_ConstructsDtoWhenRequiredPresent()
    {
        var map = MapOf(("orderId", "\"ord-1\""), ("amount", "9.99"));

        var dto = map.Validate();

        Assert.Equal("ord-1", dto.OrderId);
        Assert.Equal(9.99m, dto.Amount);
    }

    [Fact]
    public void VariableMap_Validate_OptionalAbsentIsAllowed()
    {
        var map = MapOf(("orderId", "\"ord-1\""));

        var dto = map.Validate();

        Assert.Equal("ord-1", dto.OrderId);
        Assert.Null(dto.Amount);
    }

    [Fact]
    public void VariableMap_Validate_MissingRequiredThrows()
    {
        var map = MapOf(("amount", "9.99"));

        var ex = Assert.Throws<VariableValidationException>(() => map.Validate());
        Assert.Equal(typeof(OrderVars), ex.DtoType);
        Assert.Contains("orderId", ex.MissingVariableNames);
    }

    // ---- helpers ----

    private static VariableMap<OrderVars> MapOf(params (string Name, string Json)[] entries)
    {
        var raw = entries.ToDictionary(
            e => e.Name,
            e => CloneJson(e.Json),
            StringComparer.Ordinal);
        return new VariableMap<OrderVars>(raw, s_options);
    }

    private static JsonElement CloneJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static VariableSearchResult Result(string name, string value, string scopeKey) => new()
    {
        Name = name,
        Value = value,
        ScopeKey = ScopeKey.AssumeExists(scopeKey),
        VariableKey = VariableKey.AssumeExists("1"),
        ProcessInstanceKey = ProcessInstanceKey.AssumeExists("999"),
        TenantId = TenantId.AssumeExists("<default>"),
    };
}
