// Compilable usage examples for incident operations.
// Region tags are referenced by DocFX overwrite files.
#pragma warning disable CS8321 // Local function is declared but never used
#pragma warning disable CS8629 // Nullable value type may be null

using Camunda.Orchestration.Sdk;
using Camunda.Orchestration.Sdk.Api;

namespace Camunda.Orchestration.Sdk.Examples;

internal static class IncidentExamples
{
    // <GetIncident>
    static async Task GetIncidentExample()
    {
        using var client = Camunda.CreateClient();

        // Find an incident via search
        var incidents = await client.SearchIncidentsAsync(new IncidentSearchQuery());
        var incidentKey = incidents.Items![0].IncidentKey.Value;

        var incident = await client.GetIncidentAsync(incidentKey);

        Console.WriteLine($"Incident {incident.IncidentKey}: {incident.ErrorType}");
        Console.WriteLine($"Message: {incident.ErrorMessage}");
    }
    // </GetIncident>

    // <ResolveIncident>
    static async Task ResolveIncidentExample()
    {
        using var client = Camunda.CreateClient();

        // Find an incident via search
        var incidents = await client.SearchIncidentsAsync(new IncidentSearchQuery());
        var incidentKey = incidents.Items![0].IncidentKey.Value;

        await client.ResolveIncidentAsync(incidentKey, new IncidentResolutionRequest());
    }
    // </ResolveIncident>

    // <SearchIncidents>
    static async Task SearchIncidentsExample()
    {
        using var client = Camunda.CreateClient();

        var result = await client.SearchIncidentsAsync(new IncidentSearchQuery());

        foreach (var incident in result.Items!)
        {
            Console.WriteLine($"Incident {incident.IncidentKey}: {incident.ErrorType} — {incident.ErrorMessage}");
        }
    }
    // </SearchIncidents>
}
