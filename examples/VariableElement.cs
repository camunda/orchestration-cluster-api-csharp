// Compilable usage examples for variable and element instance operations.
// These examples are type-checked during build to guard against API regressions.
using Camunda.Orchestration.Sdk;

public static class VariableElementExamples
{
    #region GetVariable
    // <GetVariable>
    public static async Task GetVariableExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetVariableAsync(VariableKey.AssumeExists("123456"));
        Console.WriteLine($"Variable: {result.Name} = {result.Value}");
    }
    // </GetVariable>
    #endregion GetVariable

    #region SearchVariables

    // <SearchVariables>
    public static async Task SearchVariablesExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchVariablesAsync(new SearchVariablesRequest());

        foreach (var variable in result.Items)
        {
            Console.WriteLine($"Variable: {variable.Name}");
        }
    }
    // </SearchVariables>
    #endregion SearchVariables

    #region GetElementInstance

    // <GetElementInstance>
    public static async Task GetElementInstanceExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetElementInstanceAsync(
            ElementInstanceKey.AssumeExists("123456"));

        Console.WriteLine($"Element: {result.ElementId}");
    }
    // </GetElementInstance>
    #endregion GetElementInstance

    #region SearchElementInstances

    // <SearchElementInstances>
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
    // </SearchElementInstances>
    #endregion SearchElementInstances

    #region SearchElementInstanceIncidents

    // <SearchElementInstanceIncidents>
    public static async Task SearchElementInstanceIncidentsExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchElementInstanceIncidentsAsync(
            ElementInstanceKey.AssumeExists("123456"),
            new IncidentSearchQuery());

        foreach (var incident in result.Items)
        {
            Console.WriteLine($"Incident: {incident.IncidentKey}");
        }
    }
    // </SearchElementInstanceIncidents>
    #endregion SearchElementInstanceIncidents

    #region CreateElementInstanceVariables

    // <CreateElementInstanceVariables>
    public static async Task CreateElementInstanceVariablesExample()
    {
        using var client = CamundaClient.Create();

        await client.CreateElementInstanceVariablesAsync(
            ElementInstanceKey.AssumeExists("123456"),
            new SetVariableRequest());
    }
    // </CreateElementInstanceVariables>
    #endregion CreateElementInstanceVariables

    #region ActivateAdHocSubProcessActivities

    // <ActivateAdHocSubProcessActivities>
    public static async Task ActivateAdHocSubProcessActivitiesExample()
    {
        using var client = CamundaClient.Create();

        await client.ActivateAdHocSubProcessActivitiesAsync(
            ElementInstanceKey.AssumeExists("123456"),
            new AdHocSubProcessActivateActivitiesInstruction());
    }
    // </ActivateAdHocSubProcessActivities>
    #endregion ActivateAdHocSubProcessActivities
}
