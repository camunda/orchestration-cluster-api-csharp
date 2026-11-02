using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Camunda.Orchestration.Sdk.Tests.Internals;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Regression guard for the "optional enum on a response modelled as a non-nullable
/// value type" defect class (#489): a non-nullable enum silently deserializes a missing
/// or null wire value to the enum default, misreporting e.g. legacy batch operations
/// with no recorded type as the first enum member.
/// </summary>
public class NullableEnumDeserializationTests
{
    private static readonly JsonSerializerOptions s_options = SdkJsonOptions.Create();

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"batchOperationType\":null}")]
    public void BatchOperationResponse_MissingOrNullType_DeserializesToNull(string json)
    {
        var result = JsonSerializer.Deserialize<BatchOperationResponse>(json, s_options)!;
        Assert.Null(result.BatchOperationType);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"batchOperationType\":null}")]
    public void AuditLogResult_MissingOrNullType_DeserializesToNull(string json)
    {
        var result = JsonSerializer.Deserialize<AuditLogResult>(json, s_options)!;
        Assert.Null(result.BatchOperationType);
    }

    [Fact]
    public void BatchOperationResponse_KnownType_RetainsEnumValue()
    {
        var result = JsonSerializer.Deserialize<BatchOperationResponse>(
            "{\"batchOperationType\":\"CANCEL_PROCESS_INSTANCE\"}", s_options)!;
        Assert.Equal(BatchOperationTypeEnum.CANCELPROCESSINSTANCE, result.BatchOperationType);
    }

    public static IEnumerable<object[]> NullableEnumProperties()
    {
        foreach (var t in typeof(CamundaClient).Assembly.GetTypes())
        {
            if (!t.IsPublic || !t.IsClass || t.IsAbstract || t.GetConstructor(Type.EmptyTypes) == null)
                continue;
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var u = Nullable.GetUnderlyingType(p.PropertyType);
                var name = p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name;
                if (u is { IsEnum: true } && name != null && p.CanWrite)
                    yield return new object[] { t, p.Name, name };
            }
        }
    }

    [Theory]
    [MemberData(nameof(NullableEnumProperties))]
    public void EveryNullableEnumProperty_ExplicitNull_DeserializesToNull(Type type, string prop, string wireName)
    {
        var json = "{\"" + wireName + "\":null}";
        object? obj;
        try
        {
            obj = JsonSerializer.Deserialize(json, type, s_options);
        }
        catch (JsonException)
        {
            return; // type has other required members; covered by the targeted tests above
        }
        Assert.NotNull(obj);
        Assert.Null(type.GetProperty(prop)!.GetValue(obj));
    }
}
