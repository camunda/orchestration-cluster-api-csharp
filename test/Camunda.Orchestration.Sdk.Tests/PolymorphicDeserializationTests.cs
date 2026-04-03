using System.Text.Json;
using System.Text.Json.Serialization;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Tests for polymorphic (discriminated oneOf) model deserialization.
/// Verifies that System.Text.Json can round-trip discriminated types
/// using the generated [JsonDerivedType] / [JsonPolymorphic] attributes.
/// </summary>
public class PolymorphicDeserializationTests
{
    /// <summary>
    /// JsonSerializerOptions matching CamundaClient._jsonOptions.
    /// </summary>
    private static readonly JsonSerializerOptions s_options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new TolerantEnumConverterFactory(),
            new CamundaKeyJsonConverterFactory(),
            new CamundaLongKeyJsonConverterFactory(),
        },
    };

    // ── JobResult: discriminator property "type" ──

    [Fact]
    public void Deserialize_JobResultUserTask_PreservesConcreteType()
    {
        var json = """
        {
            "type": "userTask",
            "denied": true,
            "deniedReason": "Missing approval"
        }
        """;

        var result = JsonSerializer.Deserialize<JobResult>(json, s_options);

        var userTask = Assert.IsType<JobResultUserTask>(result);
        Assert.True(userTask.Denied);
        Assert.Equal("Missing approval", userTask.DeniedReason);
        // Note: STJ consumes the discriminator property ("type") as metadata;
        // it is NOT populated on the deserialized object.
    }

    [Fact]
    public void Deserialize_JobResultAdHocSubProcess_PreservesConcreteType()
    {
        var json = """
        {
            "type": "adHocSubProcess",
            "isCompletionConditionFulfilled": true,
            "isCancelRemainingInstances": false
        }
        """;

        var result = JsonSerializer.Deserialize<JobResult>(json, s_options);

        var adHoc = Assert.IsType<JobResultAdHocSubProcess>(result);
        Assert.True(adHoc.IsCompletionConditionFulfilled);
        Assert.False(adHoc.IsCancelRemainingInstances);
    }

    [Fact]
    public void RoundTrip_JobResultUserTask_SerializesThenDeserializes()
    {
        // Do NOT set the discriminator property (Type); STJ writes it
        // automatically from the [JsonDerivedType] registration.
        var original = new JobResultUserTask
        {
            Denied = false,
            DeniedReason = "Looks good",
        };

        // Serialize as the base type (as the SDK would in JobCompletionRequest.Result)
        var json = JsonSerializer.Serialize<JobResult>(original, s_options);
        var deserialized = JsonSerializer.Deserialize<JobResult>(json, s_options);

        var roundTripped = Assert.IsType<JobResultUserTask>(deserialized);
        Assert.False(roundTripped.Denied);
        Assert.Equal("Looks good", roundTripped.DeniedReason);
    }

    [Fact]
    public void Deserialize_JobCompletionRequest_WithUserTaskResult()
    {
        // Simulates what the server would see when completing a user-task listener job
        var json = """
        {
            "variables": {},
            "result": {
                "type": "userTask",
                "denied": true,
                "deniedReason": "Rejected by compliance"
            }
        }
        """;

        var request = JsonSerializer.Deserialize<JobCompletionRequest>(json, s_options);

        Assert.NotNull(request);
        var userTask = Assert.IsType<JobResultUserTask>(request!.Result);
        Assert.True(userTask.Denied);
        Assert.Equal("Rejected by compliance", userTask.DeniedReason);
    }

    // ── SourceElementInstruction: discriminator property "sourceType" ──

    [Fact]
    public void Deserialize_SourceElementIdInstruction_PreservesConcreteType()
    {
        var json = """
        {
            "sourceType": "byId",
            "elementId": "task_1"
        }
        """;

        var result = JsonSerializer.Deserialize<SourceElementInstruction>(json, s_options);

        Assert.IsType<SourceElementIdInstruction>(result);
    }

    [Fact]
    public void Deserialize_SourceElementInstanceKeyInstruction_PreservesConcreteType()
    {
        var json = """
        {
            "sourceType": "byKey",
            "elementInstanceKey": 12345
        }
        """;

        var result = JsonSerializer.Deserialize<SourceElementInstruction>(json, s_options);

        Assert.IsType<SourceElementInstanceKeyInstruction>(result);
    }

    // ── AncestorScopeInstruction: discriminator property "ancestorScopeType" ──

    [Fact]
    public void Deserialize_DirectAncestorKeyInstruction_PreservesConcreteType()
    {
        var json = """
        {
            "ancestorScopeType": "direct",
            "ancestorElementInstanceKey": 99999
        }
        """;

        var result = JsonSerializer.Deserialize<AncestorScopeInstruction>(json, s_options);

        Assert.IsType<DirectAncestorKeyInstruction>(result);
    }

    [Fact]
    public void Deserialize_InferredAncestorKeyInstruction_PreservesConcreteType()
    {
        var json = """
        {
            "ancestorScopeType": "inferred"
        }
        """;

        var result = JsonSerializer.Deserialize<AncestorScopeInstruction>(json, s_options);

        Assert.IsType<InferredAncestorKeyInstruction>(result);
    }
}
