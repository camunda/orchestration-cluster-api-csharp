using System.Net;
using System.Text.Json;
using Camunda.Orchestration.Sdk.Tests.Internals;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// An enum value the SDK does not know must fail decoding. It used to fall back to
/// <c>default(TEnum)</c>, which is the first declared member for 113 of the 117 enums, so a
/// response with a state this SDK version had never heard of silently read as a real one
/// (an unknown process-instance state became <c>ACTIVE</c>). Go, Python, Rust and Java reject
/// the response in that case; C# now does the same, with a <see cref="JsonException"/> that
/// names the enum and the value.
///
/// Class-of-defect: the sweep covers every public enum, so a new enum cannot reintroduce a
/// silent fallback without a test failing.
/// </summary>
public class UnknownEnumValueTests : IDisposable
{
    private static readonly JsonSerializerOptions s_options = SdkJsonOptions.Create();

    private readonly MockHttpMessageHandler _handler = new();
    private readonly CamundaClient _client;

    public UnknownEnumValueTests()
    {
        _client = new CamundaClient(new CamundaOptions
        {
            Config = new Dictionary<string, string>
            {
                ["CAMUNDA_REST_ADDRESS"] = "https://mock.local",
            },
            HttpMessageHandler = _handler,
        });
    }

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }

    public static IEnumerable<object[]> PublicEnumTypes() => EnumWireContractTests.PublicEnumTypes();

    [Theory]
    [MemberData(nameof(PublicEnumTypes))]
    public void AnUnknownValue_IsRejected_ForEveryEnum(Type enumType)
    {
        var ex = Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize("\"__NOT_A_KNOWN_VALUE__\"", enumType, s_options));

        Assert.Contains("__NOT_A_KNOWN_VALUE__", ex.Message, StringComparison.Ordinal);
        Assert.Contains(enumType.Name, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownValue_IsRejected_InsideAnObject_WithItsPath()
    {
        var ex = Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<ActivatedJobResult>(
                "{\"kind\":\"SOMETHING_NEW\"}", s_options));

        Assert.Equal("$.kind", ex.Path);
    }

    [Fact]
    public void ANullableEnum_StillReadsNull_AndStillRejectsAnUnknownValue()
    {
        Assert.Null(JsonSerializer.Deserialize<ProcessInstanceStateEnum?>("null", s_options));
        Assert.Equal(
            ProcessInstanceStateEnum.COMPLETED,
            JsonSerializer.Deserialize<ProcessInstanceStateEnum?>("\"COMPLETED\"", s_options));
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<ProcessInstanceStateEnum?>("\"SOMETHING_NEW\"", s_options));
    }

    // The converter exists to bridge how the API spells values (UPPER_SNAKE_CASE) and how the
    // generator names members; rejecting the unknown must not make it stricter than that.
    [Theory]
    [InlineData("\"BPMN_ELEMENT\"", JobKindEnum.BPMNELEMENT)]
    [InlineData("\"bpmn_element\"", JobKindEnum.BPMNELEMENT)]
    [InlineData("\"BPMNELEMENT\"", JobKindEnum.BPMNELEMENT)]
    public void TheExistingSpellingTolerance_IsKept(string json, JobKindEnum expected)
    {
        Assert.Equal(expected, JsonSerializer.Deserialize<JobKindEnum>(json, s_options));
    }

    // End to end through the client: the observed failure. This body is a process instance whose
    // state the SDK does not know; it must not come back as a process instance in state ACTIVE.
    [Fact]
    public async Task GetProcessInstance_WithAnUnknownState_ThrowsInsteadOfReportingActive()
    {
        _handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ProcessInstanceJson("SOMETHING_NEW"), System.Text.Encoding.UTF8, "application/json"),
        });

        var ex = await Assert.ThrowsAsync<JsonException>(
            () => _client.GetProcessInstanceAsync(ProcessInstanceKey.AssumeExists("1")));

        Assert.Contains("SOMETHING_NEW", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetProcessInstance_WithAKnownState_StillDecodes()
    {
        _handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ProcessInstanceJson("COMPLETED"), System.Text.Encoding.UTF8, "application/json"),
        });

        var result = await _client.GetProcessInstanceAsync(ProcessInstanceKey.AssumeExists("1"));

        Assert.Equal(ProcessInstanceStateEnum.COMPLETED, result.State);
    }

    private static string ProcessInstanceJson(string state) =>
        "{\"businessId\":null,\"endDate\":null,\"hasIncident\":false,\"parentElementInstanceKey\":null,"
        + "\"parentProcessInstanceKey\":null,\"processDefinitionId\":\"p\",\"processDefinitionKey\":\"2\","
        + "\"processDefinitionName\":\"n\",\"processDefinitionVersion\":1,\"processDefinitionVersionTag\":null,"
        + "\"processInstanceKey\":\"1\",\"rootProcessInstanceKey\":\"1\",\"startDate\":\"2026-01-01T00:00:00Z\","
        + "\"state\":\"" + state + "\",\"storageOrdinal\":null,\"suspendedDate\":null,\"tags\":[],\"tenantId\":\"<default>\"}";
}
