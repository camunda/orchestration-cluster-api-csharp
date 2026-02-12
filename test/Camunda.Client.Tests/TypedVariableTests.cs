using System.Text.Json;
using System.Text.Json.Serialization;
using Camunda.Client.Runtime;
using FluentAssertions;

namespace Camunda.Client.Tests;

/// <summary>
/// Tests for the typed variables extension — verifying the opt-in DTO mechanism
/// for Camunda variable and custom header payloads.
/// </summary>
public class TypedVariableTests
{
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

        result.Should().NotBeNull();
        result!.OrderId.Should().Be("ord-123");
        result.Amount.Should().Be(99.99m);
    }

    [Fact]
    public void DeserializeAs_FromJsonElement_MutableClass()
    {
        var json = """{"name":"widget","count":42}""";
        object payload = JsonSerializer.Deserialize<JsonElement>(json);

        var result = payload.DeserializeAs<MutableDto>();

        result.Should().NotBeNull();
        result!.Name.Should().Be("widget");
        result.Count.Should().Be(42);
    }

    [Fact]
    public void DeserializeAs_FromJsonElement_Dictionary()
    {
        var json = """{"key1":"value1","key2":"value2"}""";
        object payload = JsonSerializer.Deserialize<JsonElement>(json);

        var result = payload.DeserializeAs<Dictionary<string, string>>();

        result.Should().NotBeNull();
        result.Should().ContainKey("key1").WhoseValue.Should().Be("value1");
    }

    [Fact]
    public void DeserializeAs_FromNull_ReturnsDefault()
    {
        object? payload = null;

        var result = payload.DeserializeAs<OrderInput>();

        result.Should().BeNull();
    }

    [Fact]
    public void DeserializeAs_FromAlreadyTyped_ReturnsSameInstance()
    {
        var original = new OrderInput("ord-456", 10.0m);
        object payload = original;

        var result = payload.DeserializeAs<OrderInput>();

        result.Should().BeSameAs(original);
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

        result.Should().NotBeNull();
        result!.OrderId.Should().Be("ord-789");
    }

    // ---- Input: Assigning DTOs to Variables property ----

    [Fact]
    public void InputVariables_DtoSerializesCorrectly()
    {
        // Verify that assigning a DTO to an object? property
        // produces correct JSON when serialized
        var request = new Api.ProcessInstanceCreationInstructionById
        {
            ProcessDefinitionId = Api.ProcessDefinitionId.AssumeExists("test-process"),
            Variables = new OrderInput("ord-100", 50.0m),
        };

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        });

        // The variables should be serialized as the DTO's properties
        json.Should().Contain("\"orderId\":\"ord-100\"");
        json.Should().Contain("\"amount\":50");
    }

    [Fact]
    public void InputVariables_DictionaryAlsoWorks()
    {
        var request = new Api.ProcessInstanceCreationInstructionById
        {
            ProcessDefinitionId = Api.ProcessDefinitionId.AssumeExists("test-process"),
            Variables = new Dictionary<string, object> { ["myVar"] = "hello" },
        };

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        });

        json.Should().Contain("\"myVar\":\"hello\"");
    }

    // ---- Round-trip: Input DTO → serialize → deserialize → DeserializeAs<T> ----

    [Fact]
    public void RoundTrip_InputThenOutput()
    {
        // Simulate: user sends a DTO as variables, API echoes them back
        var input = new OrderInput("ord-rt", 77.77m);

        // Serialize as API would send
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        var serialized = JsonSerializer.Serialize(input, jsonOptions);

        // Deserialize as API would return (into an object property → JsonElement)
        var responseJson = $$"""{"variables":{{serialized}}}""";
        var response = JsonSerializer.Deserialize<FakeResponse>(responseJson, jsonOptions);

        // Extract typed variables
        var output = response!.Variables.DeserializeAs<OrderInput>(jsonOptions);

        output.Should().NotBeNull();
        output!.OrderId.Should().Be("ord-rt");
        output.Amount.Should().Be(77.77m);
    }

    [Fact]
    public void CustomHeaders_DeserializeAs()
    {
        var json = """{"customHeaders":{"region":"eu-west","priority":5}}""";
        var response = JsonSerializer.Deserialize<FakeJobResponse>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var headers = response!.CustomHeaders.DeserializeAs<JobHeaders>();

        headers.Should().NotBeNull();
        headers!.Region.Should().Be("eu-west");
        headers.Priority.Should().Be(5);
    }

    // Helper types to simulate API responses
    private class FakeResponse
    {
        public object? Variables { get; set; }
    }

    private class FakeJobResponse
    {
        public object? CustomHeaders { get; set; }
    }
}
