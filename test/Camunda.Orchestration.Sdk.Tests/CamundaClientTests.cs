using System.Net;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Tests for CamundaClient construction and basic operations, mirroring the JS SDK's
/// usage-sdk-flow.test.ts and class-instance.test.ts.
/// </summary>
public class CamundaClientTests : IDisposable
{
    private readonly CamundaClient _client;
    private readonly MockHttpMessageHandler _handler;

    public CamundaClientTests()
    {
        _handler = new MockHttpMessageHandler();
        _client = new CamundaClient(new CamundaOptions
        {
            Config = new Dictionary<string, string>
            {
                ["CAMUNDA_REST_ADDRESS"] = "https://mock.local",
            },
            HttpMessageHandler = _handler,
        });
    }

    [Fact]
    public void ConfigIsHydrated()
    {
        Assert.NotNull(_client.Config);
        Assert.Contains("mock.local", _client.Config.RestAddress);
        Assert.EndsWith("/v2", _client.Config.RestAddress);
    }

    [Fact]
    public void BackpressureStateIsHealthyInitially()
    {
        var state = _client.GetBackpressureState();
        Assert.Equal("healthy", state.Severity);
        Assert.Equal(0, state.Consecutive);
    }

    [Fact]
    public void DisposesOwnedHttpClient()
    {
        var handler = new MockHttpMessageHandler();
        var client = new CamundaClient(new CamundaOptions
        {
            Config = new Dictionary<string, string>
            {
                ["CAMUNDA_REST_ADDRESS"] = "https://test.local",
            },
            HttpMessageHandler = handler,
        });

        client.Dispose();
    }

    [Fact]
    public void CanBeCreatedWithMinimalConfig()
    {
        using var client = new CamundaClient(new CamundaOptions
        {
            Config = new Dictionary<string, string>(),
        });

        Assert.Equal(AuthStrategy.None, client.Config.Auth.Strategy);
    }

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Test-support HttpMessageHandler that records calls and returns mock responses.
/// </summary>
internal sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, Task<HttpResponseMessage>>> _responses = new();
    private readonly List<HttpRequestMessage> _requests = new();

    public IReadOnlyList<HttpRequestMessage> Requests => _requests;

    public void Enqueue(HttpStatusCode status, string content = "{}", string contentType = "application/json")
    {
        _responses.Enqueue(_ => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(content, System.Text.Encoding.UTF8, contentType),
        }));
    }

    public void Enqueue(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        _responses.Enqueue(r => Task.FromResult(handler(r)));
    }

    public void Enqueue(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
    {
        _responses.Enqueue(handler);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        _requests.Add(request);

        if (_responses.Count > 0)
        {
            var handler = _responses.Dequeue();
            return await handler(request);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("{\"title\":\"Not Found\"}", System.Text.Encoding.UTF8, "application/json"),
        };
    }
}
