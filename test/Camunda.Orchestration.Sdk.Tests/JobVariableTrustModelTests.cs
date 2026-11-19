using System.Net;
using System.Text;
using System.Text.Json;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Pins the factual claims the job-variable trust model documentation makes about enforcement,
/// by driving the production paths a payload really takes rather than a stand-in parse:
/// <c>ActivateJobsAsync</c> over a stubbed HTTP transport for the activation response, and
/// <see cref="ActivatedJob.GetVariables{T}"/> for typed extraction.
///
/// <para>Claims covered: a payload nested beyond the <c>System.Text.Json</c> default
/// <c>MaxDepth</c> of 64 fails with a <see cref="JsonException"/> instead of recursing without
/// bound; that budget is shared with the activation envelope (response object, jobs array, job
/// object); options passed to typed extraction can tighten but never widen it; and a payload
/// that does not fit the target type surfaces as a <see cref="JsonException"/> rather than being
/// silently accepted when the DTO marks a member <c>required</c>. Both sides of every boundary are
/// asserted, because a test that only shows deep input failing would also pass if everything
/// failed.</para>
/// </summary>
public class JobVariableTrustModelTests
{
    // The envelope is three levels — response object, jobs array, job object — so 61 levels of
    // variables reach the limit of 64 exactly, and one more exceeds it, during activation, before
    // any handler runs.
    private const int MaxVariableDepthInActivation = 61;

    [Theory]
    [InlineData(MaxVariableDepthInActivation, false)]
    [InlineData(MaxVariableDepthInActivation + 1, true)]
    public async Task Activation_response_shares_one_depth_budget_with_the_job_envelope(
        int variableDepth, bool expectFailure)
    {
        var (client, handler) = NewClient();
        using var _ = client;
        handler.Enqueue(HttpStatusCode.OK, ActivationResponse(NestedObjectJson(variableDepth)));

        var activate = () => client.ActivateJobsAsync(ActivationRequest());

        if (expectFailure)
        {
            await Assert.ThrowsAnyAsync<JsonException>(activate);
        }
        else
        {
            var result = await activate();
            Assert.Single(result.Jobs);
            Assert.IsType<JsonElement>(result.Jobs[0].Variables);
        }
    }

    [Fact]
    public async Task Typed_extraction_accepts_the_deepest_payload_activation_admits()
    {
        var job = await ActivateJobAsync(NestedObjectJson(MaxVariableDepthInActivation));

        // Default options: the documented limit of 64 is not hit by a payload activation admitted.
        var holder = job.GetVariables<Holder>();

        Assert.NotNull(holder);
        Assert.NotNull(holder.A);
    }

    [Fact]
    public async Task Typed_extraction_options_can_tighten_the_depth_bound()
    {
        var job = await ActivateJobAsync(NestedObjectJson(MaxVariableDepthInActivation));
        var tighter = new JsonSerializerOptions(TypedVariables.DefaultOptions) { MaxDepth = 10 };

        Assert.IsAssignableFrom<JsonException>(
            Record.Exception(() => job.GetVariables<Holder>(tighter)));
    }

    [Fact]
    public void Typed_extraction_enforces_the_default_bound_on_an_already_parsed_element()
    {
        // A JsonElement parsed elsewhere with a higher limit still meets the default bound of 64
        // when it is converted to a DTO — the bound is not a property of how the element was made.
        using var document = JsonDocument.Parse(
            NestedObjectJson(65), new JsonDocumentOptions { MaxDepth = 128 });
        object payload = document.RootElement.Clone();

        Assert.IsAssignableFrom<JsonException>(
            Record.Exception(() => payload.DeserializeAs<Holder>()));

        using var withinBound = JsonDocument.Parse(NestedObjectJson(64));
        object accepted = withinBound.RootElement.Clone();

        Assert.NotNull(accepted.DeserializeAs<Holder>());
    }

    [Fact]
    public async Task Typed_extraction_reports_an_absent_required_member_as_a_json_exception()
    {
        var job = await ActivateJobAsync("""{"unrelated":1}""");

        // Without `required`, a mismatched payload is converted silently (properties ignored,
        // members left at their default) — the documented permissive default...
        var lenient = job.GetVariables<Holder>();
        Assert.NotNull(lenient);
        Assert.Null(lenient.A);

        // ...and with it, the same payload is rejected by the serializer, not by the SDK.
        Assert.IsAssignableFrom<JsonException>(
            Record.Exception(() => job.GetVariables<StrictHolder>()));
    }

    private sealed class Holder
    {
        public object? A { get; set; }
    }

    private sealed class StrictHolder
    {
        public required string Needed { get; set; }
    }

    private static async Task<ActivatedJob> ActivateJobAsync(string variablesJson)
    {
        var (client, handler) = NewClient();
        using var _ = client;
        handler.Enqueue(HttpStatusCode.OK, ActivationResponse(variablesJson));

        var result = await client.ActivateJobsAsync(ActivationRequest());

        return new ActivatedJob(result.Jobs.Single(), TimeProvider.System);
    }

    private static JobActivationRequest ActivationRequest() => new()
    {
        Type = "trust-model",
        MaxJobsToActivate = 1,
        Timeout = 1000,
    };

    private static string ActivationResponse(string variablesJson) => $$"""
        {
            "jobs": [
                {
                    "type": "trust-model",
                    "processDefinitionId": "p",
                    "processDefinitionVersion": 1,
                    "elementId": "task-1",
                    "customHeaders": {},
                    "worker": "w",
                    "retries": 3,
                    "deadline": 1700000000000,
                    "variables": {{variablesJson}},
                    "tenantId": "<default>",
                    "jobKey": "123456",
                    "processInstanceKey": "789012",
                    "processDefinitionKey": "345678",
                    "elementInstanceKey": "901234",
                    "kind": "BPMN_ELEMENT",
                    "listenerEventType": "UNSPECIFIED"
                }
            ]
        }
        """;

    private static (CamundaClient Client, MockHttpMessageHandler Handler) NewClient()
    {
        var handler = new MockHttpMessageHandler();
        var client = new CamundaClient(new CamundaOptions
        {
            Config = new Dictionary<string, string>
            {
                [ConfigKeys.RestAddress] = "https://mock.local",
                [ConfigKeys.AuthStrategy] = "NONE",
            },
            HttpMessageHandler = handler,
        });
        return (client, handler);
    }

    /// <summary>
    /// A payload of <paramref name="depth"/> nested objects: depth 1 is <c>{"a":1}</c>.
    /// </summary>
    private static string NestedObjectJson(int depth)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < depth; i++)
            builder.Append("{\"a\":");
        builder.Append('1');
        for (var i = 0; i < depth; i++)
            builder.Append('}');
        return builder.ToString();
    }
}
