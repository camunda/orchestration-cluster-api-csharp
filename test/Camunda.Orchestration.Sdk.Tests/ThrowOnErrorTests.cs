using System.Net;
using FluentAssertions;

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
        await act.Should().ThrowAsync<HttpSdkException>()
            .Where(e => e.Status == 400);
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
        await act.Should().ThrowAsync<HttpSdkException>()
            .Where(e => e.Status == 404);
    }

    [Fact]
    public async Task HttpSdkException_CarriesProblemDetails()
    {
        _handler.Enqueue(HttpStatusCode.BadRequest,
            """{"type":"about:blank","title":"INVALID_ARGUMENT","detail":"Process definition not found","instance":"/v2/process-instances"}""");

        using var client = CreateClient();

        var act = async () => await client.GetTopologyAsync();
        var ex = (await act.Should().ThrowAsync<HttpSdkException>()).Which;

        ex.Status.Should().Be(400);
        ex.Title.Should().Be("INVALID_ARGUMENT");
        ex.Detail.Should().Be("Process definition not found");
        ex.Type.Should().Be("about:blank");
        ex.Instance.Should().Be("/v2/process-instances");
    }

    [Fact]
    public async Task HttpSdkException_HandlesNonJsonBody()
    {
        _handler.Enqueue(HttpStatusCode.InternalServerError, "Gateway Timeout", "text/plain");

        using var client = CreateClient();

        var act = async () => await client.GetTopologyAsync();
        var ex = (await act.Should().ThrowAsync<HttpSdkException>()).Which;

        ex.Status.Should().Be(500);
        ex.Title.Should().BeNull();
        ex.Detail.Should().BeNull();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
