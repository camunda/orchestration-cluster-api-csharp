// Compilable usage examples for incident operations.
// Region tags are referenced by DocFX overwrite files.
#pragma warning disable CS8321 // Local function is declared but never used
#pragma warning disable CS8629 // Nullable value type may be null

using Camunda.Orchestration.Sdk;

namespace Camunda.Orchestration.Sdk.Examples;

internal static class IncidentExamples
{
    // <GetIncident>
    private static async Task GetIncidentExample()
    {
        using var client = CamundaClient.Create();

        // Find an incident via search
        var incidents = await client.SearchIncidentsAsync(new IncidentSearchQuery());
        var incidentKey = incidents.Items![0].IncidentKey;

        var incident = await client.GetIncidentAsync(incidentKey);

        Console.WriteLine($"Incident {incident.IncidentKey}: {incident.ErrorType}");
        Console.WriteLine($"Message: {incident.ErrorMessage}");
    }
    // </GetIncident>

    // <ResolveIncident>
    private static async Task ResolveIncidentExample()
    {
        using var client = CamundaClient.Create();

        // Find an incident via search
        var incidents = await client.SearchIncidentsAsync(new IncidentSearchQuery());
        var incidentKey = incidents.Items![0].IncidentKey;

        await client.ResolveIncidentAsync(incidentKey, new IncidentResolutionRequest());
    }
    // </ResolveIncident>

    // <SearchIncidents>
    private static async Task SearchIncidentsExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchIncidentsAsync(new IncidentSearchQuery());

        foreach (var incident in result.Items!)
        {
            Console.WriteLine($"Incident {incident.IncidentKey}: {incident.ErrorType} — {incident.ErrorMessage}");
        }
    }
    // </SearchIncidents>
}
