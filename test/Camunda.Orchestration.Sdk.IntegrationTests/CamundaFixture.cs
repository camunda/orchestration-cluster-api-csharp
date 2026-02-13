using System.Text;
using System.Text.Json;
using Camunda.Orchestration.Sdk.Api;

namespace Camunda.Orchestration.Sdk.IntegrationTests;

/// <summary>
/// Shared fixture that provides a <see cref="CamundaClient"/> connected to the
/// local Docker Camunda instance and common test helpers.
/// Instantiated once per xUnit test collection and reused across all tests.
/// </summary>
public sealed class CamundaFixture : IAsyncLifetime
{
    private static readonly string RestAddress =
        Environment.GetEnvironmentVariable("CAMUNDA_REST_ADDRESS")
        ?? "http://localhost:8080/v2";

    public CamundaClient Client { get; private set; } = null!;

    /// <summary>
    /// Raw <see cref="HttpClient"/> pointing at the same base address, for
    /// operations whose generated types don't yet fully support all patterns.
    /// </summary>
    public HttpClient Http { get; private set; } = null!;

    public JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new Runtime.CamundaKeyJsonConverterFactory(),
            new Runtime.CamundaLongKeyJsonConverterFactory(),
        },
    };

    public async Task InitializeAsync()
    {
        Client = Camunda.CreateClient(new Runtime.CamundaOptions
        {
            Config = new Dictionary<string, string>
            {
                ["CAMUNDA_REST_ADDRESS"] = RestAddress,
                ["CAMUNDA_AUTH_STRATEGY"] = "NONE",
            },
        });

        Http = new HttpClient
        {
            BaseAddress = new Uri(RestAddress.TrimEnd('/') + "/"),
        };

        // Wait for the engine to be ready (topology endpoint)
        await WaitForReadyAsync();
    }

    public Task DisposeAsync()
    {
        Http.Dispose();
        Client.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Polls topology until server is ready (up to 60 s).
    /// </summary>
    private async Task WaitForReadyAsync()
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var topology = await Client.GetTopologyAsync();
                if (topology?.ClusterSize > 0)
                    return;
            }
            catch
            {
                // not ready yet
            }
            await Task.Delay(1000);
        }

        throw new TimeoutException("Camunda engine did not become ready within 60 s");
    }

    /// <summary>
    /// Deploy a BPMN resource file from the Fixtures directory.
    /// </summary>
    public async Task<CreateDeploymentResponse> DeployResourceAsync(string fixtureFileName)
    {
        var filePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixtureFileName);
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Fixture not found: {filePath}");

        using var content = new MultipartFormDataContent();
        var fileBytes = await File.ReadAllBytesAsync(filePath);
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, "resources", fixtureFileName);

        return await Client.CreateDeploymentAsync(content);
    }

    /// <summary>
    /// Create a process instance by BPMN process id using the typed SDK API.
    /// </summary>
    public async Task<CreateProcessInstanceResult> CreateProcessInstanceAsync(
        string processDefinitionId,
        Dictionary<string, object>? variables = null)
    {
        return await Client.CreateProcessInstanceAsync(new ProcessInstanceCreationInstructionById
        {
            ProcessDefinitionId = ProcessDefinitionId.AssumeExists(processDefinitionId),
            Variables = variables,
        });
    }

    /// <summary>
    /// Cancel a process instance by its key.
    /// Swallows 404 (already completed/cancelled).
    /// </summary>
    public async Task CancelProcessInstanceAsync(ProcessInstanceKey processInstanceKey)
    {
        try
        {
            await Client.CancelProcessInstanceAsync(processInstanceKey, new CancelProcessInstanceRequest());
        }
        catch (Runtime.HttpSdkException ex) when (ex.Status == 404)
        {
            // Already completed or cancelled
        }
    }
}

/// <summary>
/// xUnit collection definition — all integration tests share a single <see cref="CamundaFixture"/>.
/// </summary>
[CollectionDefinition("Camunda")]
public class CamundaTests : ICollectionFixture<CamundaFixture>;
