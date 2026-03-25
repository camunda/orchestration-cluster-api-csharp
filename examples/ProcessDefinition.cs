// Compilable usage examples for process definition operations.
// These examples are type-checked during build to guard against API regressions.
using Camunda.Orchestration.Sdk;

public static class ProcessDefinitionExamples
{
    #region GetProcessDefinition
    public static async Task GetProcessDefinitionExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetProcessDefinitionAsync(
            new ProcessDefinitionKey("123456"));

        Console.WriteLine($"Process definition: {result.Name}");
    }
    #endregion GetProcessDefinition

    #region GetProcessDefinitionXml
    public static async Task GetProcessDefinitionXmlExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetProcessDefinitionXmlAsync(
            new ProcessDefinitionKey("123456"));

        Console.WriteLine($"XML: {result}");
    }
    #endregion GetProcessDefinitionXml

    #region SearchProcessDefinitions
    public static async Task SearchProcessDefinitionsExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchProcessDefinitionsAsync(
            new ProcessDefinitionSearchQuery());

        foreach (var pd in result.Items)
        {
            Console.WriteLine($"Process definition: {pd.Name}");
        }
    }
    #endregion SearchProcessDefinitions

    #region GetProcessDefinitionStatistics
    public static async Task GetProcessDefinitionStatisticsExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetProcessDefinitionStatisticsAsync(
            new ProcessDefinitionKey("123456"),
            new ProcessDefinitionElementStatisticsQuery());

        foreach (var stat in result.Items)
        {
            Console.WriteLine($"Element: {stat.ElementId}");
        }
    }
    #endregion GetProcessDefinitionStatistics

    #region GetProcessDefinitionInstanceStatistics
    public static async Task GetProcessDefinitionInstanceStatisticsExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetProcessDefinitionInstanceStatisticsAsync(
            new ProcessDefinitionInstanceStatisticsQuery());

        foreach (var stat in result.Items)
        {
            Console.WriteLine($"Definition: {stat.ProcessDefinitionKey}");
        }
    }
    #endregion GetProcessDefinitionInstanceStatistics

    #region GetProcessDefinitionInstanceVersionStatistics
    public static async Task GetProcessDefinitionInstanceVersionStatisticsExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetProcessDefinitionInstanceVersionStatisticsAsync(
            new ProcessDefinitionInstanceVersionStatisticsQuery
            {
                ProcessDefinitionKey = "123456",
            });

        foreach (var stat in result.Items)
        {
            Console.WriteLine($"Version: {stat.Version}");
        }
    }
    #endregion GetProcessDefinitionInstanceVersionStatistics

    #region GetProcessDefinitionMessageSubscriptionStatistics
    public static async Task GetProcessDefinitionMessageSubscriptionStatisticsExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetProcessDefinitionMessageSubscriptionStatisticsAsync(
            new ProcessDefinitionMessageSubscriptionStatisticsQuery());

        foreach (var stat in result.Items)
        {
            Console.WriteLine($"Message name: {stat.MessageName}");
        }
    }
    #endregion GetProcessDefinitionMessageSubscriptionStatistics

    #region GetStartProcessForm
    public static async Task GetStartProcessFormExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetStartProcessFormAsync(
            new ProcessDefinitionKey("123456"));

        Console.WriteLine($"Form: {result.FormKey}");
    }
    #endregion GetStartProcessForm
}
