// Compilable usage examples for decision evaluation operations.
// Region tags are referenced by DocFX overwrite files.
#pragma warning disable CS8321 // Local function is declared but never used
#pragma warning disable CS8629 // Nullable value type may be null

using Camunda.Orchestration.Sdk;
using Camunda.Orchestration.Sdk.Api;

namespace Camunda.Orchestration.Sdk.Examples;

internal static class DecisionExamples
{
    // <EvaluateDecision>
    static async Task EvaluateDecisionExample()
    {
        using var client = Camunda.CreateClient();

        // Find the decision definition via search
        var definitions = await client.SearchDecisionDefinitionsAsync(new DecisionDefinitionSearchQuery());
        var decisionDefinitionId = definitions.Items![0].DecisionDefinitionId.Value;

        var result = await client.EvaluateDecisionAsync(new DecisionEvaluationById
        {
            DecisionDefinitionId = decisionDefinitionId,
            Variables = new Dictionary<string, object>
            {
                ["weight"] = 5.2,
                ["destination"] = "DE"
            }
        });

        Console.WriteLine($"Decision: {result.DecisionDefinitionId}");
    }
    // </EvaluateDecision>

    // <SearchDecisionDefinitions>
    static async Task SearchDecisionDefinitionsExample()
    {
        using var client = Camunda.CreateClient();

        var result = await client.SearchDecisionDefinitionsAsync(new DecisionDefinitionSearchQuery());

        foreach (var def in result.Items!)
        {
            Console.WriteLine($"{def.DecisionDefinitionId} v{def.Version}");
        }
    }
    // </SearchDecisionDefinitions>
}
