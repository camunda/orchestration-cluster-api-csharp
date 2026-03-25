// Compilable usage examples for decision operations.
// These examples are type-checked during build to guard against API regressions.
using Camunda.Orchestration.Sdk;

public static class DecisionExamples
{
    #region EvaluateDecisionById
    public static async Task EvaluateDecisionByIdExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.EvaluateDecisionAsync(new DecisionEvaluationInstruction
        {
            DecisionDefinitionId = "my-decision",
        });

        Console.WriteLine($"Decision output: {result.DecisionOutput}");
    }
    #endregion EvaluateDecisionById

    #region EvaluateDecisionByKey
    public static async Task EvaluateDecisionByKeyExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.EvaluateDecisionAsync(new DecisionEvaluationInstruction
        {
            DecisionDefinitionKey = "123456",
        });

        Console.WriteLine($"Decision output: {result.DecisionOutput}");
    }
    #endregion EvaluateDecisionByKey

    #region GetDecisionDefinition
    public static async Task GetDecisionDefinitionExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetDecisionDefinitionAsync(
            new DecisionDefinitionKey("123456"));

        Console.WriteLine($"Decision definition: {result.Name}");
    }
    #endregion GetDecisionDefinition

    #region GetDecisionDefinitionXml
    public static async Task GetDecisionDefinitionXmlExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetDecisionDefinitionXmlAsync(
            new DecisionDefinitionKey("123456"));

        Console.WriteLine($"XML: {result}");
    }
    #endregion GetDecisionDefinitionXml

    #region SearchDecisionDefinitions
    public static async Task SearchDecisionDefinitionsExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchDecisionDefinitionsAsync(
            new DecisionDefinitionSearchQuery());

        foreach (var dd in result.Items)
        {
            Console.WriteLine($"Decision definition: {dd.Name}");
        }
    }
    #endregion SearchDecisionDefinitions

    #region GetDecisionInstance
    public static async Task GetDecisionInstanceExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetDecisionInstanceAsync(
            new DecisionEvaluationInstanceKey("123456"));

        Console.WriteLine($"Decision instance: {result.DecisionDefinitionId}");
    }
    #endregion GetDecisionInstance

    #region SearchDecisionInstances
    public static async Task SearchDecisionInstancesExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchDecisionInstancesAsync(
            new DecisionInstanceSearchQuery());

        foreach (var di in result.Items)
        {
            Console.WriteLine($"Decision instance: {di.DecisionDefinitionId}");
        }
    }
    #endregion SearchDecisionInstances

    #region DeleteDecisionInstance
    public static async Task DeleteDecisionInstanceExample()
    {
        using var client = CamundaClient.Create();

        await client.DeleteDecisionInstanceAsync(
            new DecisionEvaluationKey("123456"),
            new DeleteDecisionInstanceRequest());
    }
    #endregion DeleteDecisionInstance

    #region GetDecisionRequirements
    public static async Task GetDecisionRequirementsExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetDecisionRequirementsAsync(
            new DecisionRequirementsKey("123456"));

        Console.WriteLine($"DRD: {result.Name}");
    }
    #endregion GetDecisionRequirements

    #region GetDecisionRequirementsXml
    public static async Task GetDecisionRequirementsXmlExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetDecisionRequirementsXmlAsync(
            new DecisionRequirementsKey("123456"));

        Console.WriteLine($"XML: {result}");
    }
    #endregion GetDecisionRequirementsXml

    #region SearchDecisionRequirements
    public static async Task SearchDecisionRequirementsExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchDecisionRequirementsAsync(
            new DecisionRequirementsSearchQuery());

        foreach (var drd in result.Items)
        {
            Console.WriteLine($"DRD: {drd.Name}");
        }
    }
    #endregion SearchDecisionRequirements
}
