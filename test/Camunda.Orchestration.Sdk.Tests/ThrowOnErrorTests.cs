using System.Net;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Tests that non-2xx HTTP responses always throw HttpSdkException.
/// Previously ThrowOnError=false existed but was broken: SendAsync swallowed
/// the error and tried to deserialize the error body as the success type.
/// The option was removed — all send methods now uniformly throw.
/// </summary>
public class ThrowOnErrorTests : IDisposable
{
    private readonly MockHttpMessageHandler _handler;

    public ThrowOnErrorTests()
    {
        _handler = new MockHttpMessageHandler();
    }

    private CamundaClient CreateClient()
    {
        return new CamundaClient(new CamundaOptions
        {
            Config = new Dictionary<string, string>
            {
                ["CAMUNDA_REST_ADDRESS"] = "https://mock.local",
            },
            HttpMessageHandler = _handler,
        });
    }

    [Fact]
    public async Task SendAsync_ThrowsOnNon2xxResponse()
    {
        _handler.Enqueue(HttpStatusCode.BadRequest,
            """{"title":"INVALID_ARGUMENT","detail":"Bad input"}""");

        using var client = CreateClient();

        var act = async () => await client.GetTopologyAsync();
        var ex = await Assert.ThrowsAsync<HttpSdkException>(act);
        Assert.Equal(400, ex.Status);
    }

    [Fact]
    public async Task SendVoidAsync_ThrowsOnNon2xxResponse()
    {
        _handler.Enqueue(HttpStatusCode.NotFound,
            """{"title":"NOT_FOUND","detail":"Job not found"}""");

        using var client = CreateClient();

        var jobKey = JobKey.AssumeExists("12345");
        var act = async () => await client.FailJobAsync(jobKey, new JobFailRequest
        {
            Retries = 0,
            ErrorMessage = "test",
        });
        var ex = await Assert.ThrowsAsync<HttpSdkException>(act);
        Assert.Equal(404, ex.Status);
    }

    [Fact]
    public async Task HttpSdkException_CarriesProblemDetails()
    {
        _handler.Enqueue(HttpStatusCode.BadRequest,
            """{"type":"about:blank","title":"INVALID_ARGUMENT","detail":"Process definition not found","instance":"/v2/process-instances"}""");

        using var client = CreateClient();

        var act = async () => await client.GetTopologyAsync();
        var ex = await Assert.ThrowsAsync<HttpSdkException>(act);

        Assert.Equal(400, ex.Status);
        Assert.Equal("INVALID_ARGUMENT", ex.Title);
        Assert.Equal("Process definition not found", ex.Detail);
        Assert.Equal("about:blank", ex.Type);
        Assert.Equal("/v2/process-instances", ex.Instance);
    }

    [Fact]
    public async Task HttpSdkException_HandlesNonJsonBody()
    {
        // 500 is retried (#479): exhaust the retry budget with the same response so
        // the assertion still observes the original status.
        _handler.Enqueue(HttpStatusCode.InternalServerError, "Gateway Timeout", "text/plain");
        _handler.Enqueue(HttpStatusCode.InternalServerError, "Gateway Timeout", "text/plain");
        _handler.Enqueue(HttpStatusCode.InternalServerError, "Gateway Timeout", "text/plain");

        using var client = new CamundaClient(new CamundaOptions
        {
            Config = new Dictionary<string, string>
            {
                ["CAMUNDA_REST_ADDRESS"] = "https://mock.local",
                ["CAMUNDA_SDK_HTTP_RETRY_BASE_DELAY_MS"] = "1",
                ["CAMUNDA_SDK_HTTP_RETRY_MAX_DELAY_MS"] = "5",
            },
            HttpMessageHandler = _handler,
        });

        var act = async () => await client.GetTopologyAsync();
        var ex = await Assert.ThrowsAsync<HttpSdkException>(act);

        Assert.Equal(500, ex.Status);
        Assert.Null(ex.Title);
        Assert.Null(ex.Detail);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
