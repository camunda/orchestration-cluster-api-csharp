using FluentAssertions;

namespace Camunda.Orchestration.Sdk.IntegrationTests;

/// <summary>
/// Integration tests for <see cref="CamundaClient.SearchVariablesAsDtoAsync{T}"/> against a live
/// cluster.
///
/// <para>
/// Mirrors the Python and TypeScript SDK coverage: a freshly-created process instance's variables
/// are not immediately visible to the variable index (eventual consistency), so the search is
/// polled until every declared variable becomes visible. The test process waits at an
/// unactivated service task, keeping the instance — and its variables — alive for the duration of
/// the search.
/// </para>
/// </summary>
[Collection("Camunda")]
[Trait("Category", "Integration")]
public class SearchVariablesAsDtoTests(CamundaFixture fixture)
{
    // Variable names resolve through the client's camelCase serializer options, so the DTO member
    // `OrderId` maps to the variable `orderId`, and `Amount` to `amount`.
    public record OrderVars(string OrderId, decimal? Amount);

    // A DTO that declares a required variable which is never set on the instance, so strict
    // validation must fail while lenient access tolerates the absence.
    public record StrictOrderVars(string OrderId, string CustomerId);

    [Fact]
    public async Task SearchVariablesAsDto_FindsDeclaredVariables()
    {
        await fixture.DeployResourceAsync("test-process.bpmn");

        var createResult = await fixture.CreateProcessInstanceAsync(
            "integration-test-process",
            new Dictionary<string, object>
            {
                ["orderId"] = "ord-1",
                ["amount"] = 42.5m,
                // An extra variable that is not declared on the DTO and must be ignored.
                ["internalSecret"] = "do-not-leak",
            });

        var processInstanceKey = createResult.ProcessInstanceKey;

        try
        {
            // Variables are eventually consistent on a freshly-created instance, so poll the
            // declared-variable search until both declared variables are visible.
            var map = await PollUntilAsync<OrderVars>(
                processInstanceKey,
                result => result.Contains("orderId") && result.Contains("amount"));

            map.Should().NotBeNull("both declared variables should become visible within the deadline");

            map!.Contains("orderId").Should().BeTrue();
            map.Contains("amount").Should().BeTrue();
            map.Get<string>("orderId").Should().Be("ord-1");
            map.Get<decimal>("amount").Should().Be(42.5m);

            // Only the declared variables are queried — the extra one is never fetched.
            map.Contains("internalSecret").Should().BeFalse();
            map.Raw.Keys.OrderBy(k => k, StringComparer.Ordinal)
                .Should().Equal("amount", "orderId");

            // Strict access returns a fully-typed, validated DTO.
            var order = map.Validate();
            order.OrderId.Should().Be("ord-1");
            order.Amount.Should().Be(42.5m);
        }
        finally
        {
            await fixture.CancelProcessInstanceAsync(processInstanceKey);
        }
    }

    [Fact]
    public async Task SearchVariablesAsDto_ValidateThrowsOnMissingRequired()
    {
        await fixture.DeployResourceAsync("test-process.bpmn");

        var createResult = await fixture.CreateProcessInstanceAsync(
            "integration-test-process",
            new Dictionary<string, object>
            {
                ["orderId"] = "ord-1",
                // `customerId` is declared on StrictOrderVars but never set on the instance.
            });

        var processInstanceKey = createResult.ProcessInstanceKey;

        try
        {
            // Wait until the one variable that does exist becomes visible.
            var map = await PollUntilAsync<StrictOrderVars>(
                processInstanceKey,
                result => result.Contains("orderId"));

            map.Should().NotBeNull("the set variable should become visible within the deadline");

            // The declared-but-absent variable is simply missing — lenient access never throws.
            map!.Contains("orderId").Should().BeTrue();
            map.Contains("customerId").Should().BeFalse();

            // Strict validation fails because a required variable is missing.
            map.Invoking(m => m.Validate())
                .Should().Throw<VariableValidationException>();
        }
        finally
        {
            await fixture.CancelProcessInstanceAsync(processInstanceKey);
        }
    }

    /// <summary>
    /// Polls <see cref="CamundaClient.SearchVariablesAsDtoAsync{T}"/> until <paramref name="isReady"/>
    /// is satisfied or the deadline elapses, returning the last result (or <c>null</c> if the
    /// predicate was never satisfied).
    /// </summary>
    private async Task<VariableMap<T>?> PollUntilAsync<T>(
        ProcessInstanceKey processInstanceKey,
        Func<VariableMap<T>, bool> isReady)
        where T : class
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        VariableMap<T>? last = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            last = await fixture.Client.SearchVariablesAsDtoAsync<T>(processInstanceKey);
            if (isReady(last))
                return last;

            await Task.Delay(500);
        }

        return isReady(last!) ? last : null;
    }
}
