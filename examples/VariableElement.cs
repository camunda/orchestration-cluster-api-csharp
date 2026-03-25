// Compilable usage examples for variable and element instance operations.
// These examples are type-checked during build to guard against API regressions.
using Camunda.Orchestration.Sdk;

public static class VariableElementExamples
{
    #region GetVariable
    public static async Task GetVariableExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetVariableAsync(new VariableKey("123456"));
        Console.WriteLine($"Variable: {result.Name} = {result.Value}");
    }
    #endregion GetVariable

    #region SearchVariables
    public static async Task SearchVariablesExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchVariablesAsync(new SearchVariablesRequest());

        foreach (var variable in result.Items)
        {
            Console.WriteLine($"Variable: {variable.Name}");
        }
    }
    #endregion SearchVariables

    #region GetElementInstance
    public static async Task GetElementInstanceExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetElementInstanceAsync(
            new ElementInstanceKey("123456"));

        Console.WriteLine($"Element: {result.ElementId}");
    }
    #endregion GetElementInstance

    #region SearchElementInstances
    public static async Task SearchElementInstancesExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchElementInstancesAsync(
            new ElementInstanceSearchQuery());

        foreach (var ei in result.Items)
        {
            Console.WriteLine($"Element instance: {ei.ElementInstanceKey}");
        }
    }
    #endregion SearchElementInstances

    #region SearchElementInstanceIncidents
    public static async Task SearchElementInstanceIncidentsExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchElementInstanceIncidentsAsync(
            new ElementInstanceKey("123456"),
            new IncidentSearchQuery());

        foreach (var incident in result.Items)
        {
            Console.WriteLine($"Incident: {incident.IncidentKey}");
        }
    }
    #endregion SearchElementInstanceIncidents

    #region CreateElementInstanceVariables
    public static async Task CreateElementInstanceVariablesExample()
    {
        using var client = CamundaClient.Create();

        await client.CreateElementInstanceVariablesAsync(
            new ElementInstanceKey("123456"),
            new SetVariableRequest());
    }
    #endregion CreateElementInstanceVariables

    #region ActivateAdHocSubProcessActivities
    public static async Task ActivateAdHocSubProcessActivitiesExample()
    {
        using var client = CamundaClient.Create();

        await client.ActivateAdHocSubProcessActivitiesAsync(
            new ElementInstanceKey("123456"),
            new AdHocSubProcessActivateActivitiesInstruction());
    }
    #endregion ActivateAdHocSubProcessActivities
}
