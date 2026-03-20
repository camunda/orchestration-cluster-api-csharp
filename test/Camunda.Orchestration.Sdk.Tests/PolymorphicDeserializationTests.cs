using System.Text.Json;
using System.Text.Json.Serialization;
using Camunda.Orchestration.Sdk.Api;
using FluentAssertions;

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
            new Runtime.TolerantEnumConverterFactory(),
            new Runtime.CamundaKeyJsonConverterFactory(),
            new Runtime.CamundaLongKeyJsonConverterFactory(),
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

        result.Should().BeOfType<JobResultUserTask>();
        var userTask = (JobResultUserTask)result!;
        userTask.Denied.Should().BeTrue();
        userTask.DeniedReason.Should().Be("Missing approval");
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

        result.Should().BeOfType<JobResultAdHocSubProcess>();
        var adHoc = (JobResultAdHocSubProcess)result!;
        adHoc.IsCompletionConditionFulfilled.Should().BeTrue();
        adHoc.IsCancelRemainingInstances.Should().BeFalse();
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

        deserialized.Should().BeOfType<JobResultUserTask>();
        var roundTripped = (JobResultUserTask)deserialized!;
        roundTripped.Denied.Should().BeFalse();
        roundTripped.DeniedReason.Should().Be("Looks good");
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

        request.Should().NotBeNull();
        request!.Result.Should().BeOfType<JobResultUserTask>();
        var userTask = (JobResultUserTask)request.Result!;
        userTask.Denied.Should().BeTrue();
        userTask.DeniedReason.Should().Be("Rejected by compliance");
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

        result.Should().BeOfType<SourceElementIdInstruction>();
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

        result.Should().BeOfType<SourceElementInstanceKeyInstruction>();
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

        result.Should().BeOfType<DirectAncestorKeyInstruction>();
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

        result.Should().BeOfType<InferredAncestorKeyInstruction>();
    }
}
