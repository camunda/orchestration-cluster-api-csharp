using System.Text;
using System.Text.Json;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Pins the one factual claim the job-variable trust model documentation makes about enforcement:
/// a variable payload nested beyond the <c>System.Text.Json</c> default <c>MaxDepth</c> of 64
/// fails with a <see cref="JsonException"/> instead of recursing without bound.
///
/// <para>Class-of-defect scope: the bound is asserted as behaviour rather than by reading
/// <c>MaxDepth</c> back off the options, and it is asserted on <em>every</em> options object a
/// variable payload passes through — the client options that parse an activation response and the
/// <see cref="TypedVariables"/> options that a handler's <c>GetVariables&lt;T&gt;()</c> uses — so
/// one of them drifting away from the documented limit fails here. Both sides of the boundary are
/// checked, because a test that only shows deep input failing would also pass if everything
/// failed.</para>
/// </summary>
public class JobVariableTrustModelTests
{
    [Theory]
    [InlineData(64, false)]
    [InlineData(65, true)]
    public void Variable_payloads_are_bounded_at_the_documented_depth(int depth, bool expectFailure)
    {
        using var client = NewClient();

        var payload = NestedObjectJson(depth);

        // Every options object a variable payload is deserialized with, in the order a payload
        // meets them: the client options that read it off the activation response, then the
        // TypedVariables options a handler's GetVariables<T>() falls back to.
        JsonSerializerOptions[] optionsOnTheVariablePath =
        [
            client._jsonOptions,
            TypedVariables.DefaultOptions,
        ];

        foreach (var options in optionsOnTheVariablePath)
        {
            var exception = Record.Exception(
                () => JsonSerializer.Deserialize<JsonElement>(payload, options));

            if (expectFailure)
                Assert.IsAssignableFrom<JsonException>(exception);
            else
                Assert.Null(exception);
        }
    }

    [Theory]
    // The envelope is three levels — response object, jobs array, job object — so 61 levels of
    // variables reach the limit exactly...
    [InlineData(61, false)]
    // ...and one more exceeds it, during activation, before any handler runs.
    [InlineData(62, true)]
    public void The_activation_envelope_counts_towards_the_same_depth_budget(int variableDepth, bool expectFailure)
    {
        using var client = NewClient();

        var response = $"{{\"jobs\":[{{\"variables\":{NestedObjectJson(variableDepth)}}}]}}";

        var exception = Record.Exception(
            () => JsonSerializer.Deserialize<JsonElement>(response, client._jsonOptions));

        if (expectFailure)
            Assert.IsAssignableFrom<JsonException>(exception);
        else
            Assert.Null(exception);
    }

    private static CamundaClient NewClient() => new(new CamundaOptions
    {
        Config = new Dictionary<string, string>
        {
            [ConfigKeys.RestAddress] = "https://mock.local",
            [ConfigKeys.AuthStrategy] = "NONE",
        },
        HttpMessageHandler = new MockHttpMessageHandler(),
    });

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
