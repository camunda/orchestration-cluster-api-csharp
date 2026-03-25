// Compilable usage examples for incident operations.
// These examples are type-checked during build to guard against API regressions.
using Camunda.Orchestration.Sdk;

public static class IncidentExamples
{
    #region GetIncident
    public static async Task GetIncidentExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetIncidentAsync(new IncidentKey("123456"));
        Console.WriteLine($"Incident: {result.IncidentKey}");
    }
    #endregion GetIncident

    #region ResolveIncident
    public static async Task ResolveIncidentExample()
    {
        using var client = CamundaClient.Create();

        await client.ResolveIncidentAsync(
            new IncidentKey("123456"),
            new IncidentResolutionRequest());
    }
    #endregion ResolveIncident

    #region SearchIncidents
    public static async Task SearchIncidentsExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchIncidentsAsync(new IncidentSearchQuery());

        foreach (var incident in result.Items)
        {
            Console.WriteLine($"Incident: {incident.IncidentKey}");
        }
    }
    #endregion SearchIncidents
}
