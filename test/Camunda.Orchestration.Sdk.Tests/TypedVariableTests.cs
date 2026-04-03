using System.Text.Json;
using System.Text.Json.Serialization;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Tests for the typed variables extension — verifying the opt-in DTO mechanism
/// for Camunda variable and custom header payloads.
/// </summary>
public class TypedVariableTests
{
    private static readonly JsonSerializerOptions s_camelCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions s_camelCaseOnlyOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly JsonSerializerOptions s_caseInsensitiveOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ---- Sample DTOs ----

    public record OrderInput(string OrderId, decimal Amount);

    public record OrderOutput(bool Processed, string InvoiceNumber);

    public record JobHeaders(string Region, int Priority);

    public class MutableDto
    {
        public string? Name { get; set; }
        public int Count { get; set; }
    }

    // ---- DeserializeAs<T> from JsonElement ----

    [Fact]
    public void DeserializeAs_FromJsonElement_Record()
    {
        // Simulate what the API returns: a JsonElement inside an object property
        var json = """{"orderId":"ord-123","amount":99.99}""";
        object payload = JsonSerializer.Deserialize<JsonElement>(json);

        var result = payload.DeserializeAs<OrderInput>();

        Assert.NotNull(result);
        Assert.Equal("ord-123", result!.OrderId);
        Assert.Equal(99.99m, result.Amount);
    }

    [Fact]
    public void DeserializeAs_FromJsonElement_MutableClass()
    {
        var json = """{"name":"widget","count":42}""";
        object payload = JsonSerializer.Deserialize<JsonElement>(json);

        var result = payload.DeserializeAs<MutableDto>();

        Assert.NotNull(result);
        Assert.Equal("widget", result!.Name);
        Assert.Equal(42, result.Count);
    }

    [Fact]
    public void DeserializeAs_FromJsonElement_Dictionary()
    {
        var json = """{"key1":"value1","key2":"value2"}""";
        object payload = JsonSerializer.Deserialize<JsonElement>(json);

        var result = payload.DeserializeAs<Dictionary<string, string>>();

        Assert.NotNull(result);
        Assert.True(result.ContainsKey("key1"));
        Assert.Equal("value1", result["key1"]);
    }

    [Fact]
    public void DeserializeAs_FromNull_ReturnsDefault()
    {
        object? payload = null;

        var result = payload.DeserializeAs<OrderInput>();

        Assert.Null(result);
    }

    [Fact]
    public void DeserializeAs_FromAlreadyTyped_ReturnsSameInstance()
    {
        var original = new OrderInput("ord-456", 10.0m);
        object payload = original;

        var result = payload.DeserializeAs<OrderInput>();

        Assert.Same(original, result);
    }

    [Fact]
    public void DeserializeAs_WithCustomOptions()
    {
        // Use snake_case naming
        var json = """{"order_id":"ord-789","amount":1.5}""";
        object payload = JsonSerializer.Deserialize<JsonElement>(json);

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
        };

        var result = payload.DeserializeAs<OrderInput>(options);

        Assert.NotNull(result);
        Assert.Equal("ord-789", result!.OrderId);
    }

    // ---- Input: Assigning DTOs to Variables property ----

    [Fact]
    public void InputVariables_DtoSerializesCorrectly()
    {
        // Verify that assigning a DTO to an object? property
        // produces correct JSON when serialized
        var request = new ProcessInstanceCreationInstructionById
        {
            ProcessDefinitionId = ProcessDefinitionId.AssumeExists("test-process"),
            Variables = new OrderInput("ord-100", 50.0m),
        };

        var json = JsonSerializer.Serialize(request, s_camelCaseOptions);

        // The variables should be serialized as the DTO's properties
        Assert.Contains("\"orderId\":\"ord-100\"", json);
        Assert.Contains("\"amount\":50", json);
    }

    [Fact]
    public void InputVariables_DictionaryAlsoWorks()
    {
        var request = new ProcessInstanceCreationInstructionById
        {
            ProcessDefinitionId = ProcessDefinitionId.AssumeExists("test-process"),
            Variables = new Dictionary<string, object> { ["myVar"] = "hello" },
        };

        var json = JsonSerializer.Serialize(request, s_camelCaseOptions);

        Assert.Contains("\"myVar\":\"hello\"", json);
    }

    // ---- Round-trip: Input DTO → serialize → deserialize → DeserializeAs<T> ----

    [Fact]
    public void RoundTrip_InputThenOutput()
    {
        // Simulate: user sends a DTO as variables, API echoes them back
        var input = new OrderInput("ord-rt", 77.77m);

        // Serialize as API would send
        var serialized = JsonSerializer.Serialize(input, s_camelCaseOnlyOptions);

        // Deserialize as API would return (into an object property → JsonElement)
        var responseJson = $$"""{"variables":{{serialized}}}""";
        var response = JsonSerializer.Deserialize<FakeResponse>(responseJson, s_camelCaseOnlyOptions);

        // Extract typed variables
        var output = response!.Variables.DeserializeAs<OrderInput>(s_camelCaseOnlyOptions);

        Assert.NotNull(output);
        Assert.Equal("ord-rt", output!.OrderId);
        Assert.Equal(77.77m, output.Amount);
    }

    [Fact]
    public void CustomHeaders_DeserializeAs()
    {
        var json = """{"customHeaders":{"region":"eu-west","priority":5}}""";
        var response = JsonSerializer.Deserialize<FakeJobResponse>(json, s_caseInsensitiveOptions);

        var headers = response!.CustomHeaders.DeserializeAs<JobHeaders>();

        Assert.NotNull(headers);
        Assert.Equal("eu-west", headers!.Region);
        Assert.Equal(5, headers.Priority);
    }

    // Helper types to simulate API responses
    private sealed class FakeResponse
    {
        public object? Variables { get; set; }
    }

    private sealed class FakeJobResponse
    {
        public object? CustomHeaders { get; set; }
    }
}
