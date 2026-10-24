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

    [Fact]
    public async Task SchemalessOptionalBody_Omitted_SendsEmptyJsonObject()
    {
        // Issue #469: cancelBatchOperation's request body is optional and
        // untyped in the spec. The JS SDK sends `{}` when the caller omits
        // it — the C# SDK must match on the wire. The request (and its
        // content) is disposed after sending, so capture inside the handler.
        string? capturedBody = null;
        var hadContent = false;
        _handler.Enqueue(async request =>
        {
            hadContent = request.Content != null;
            capturedBody = request.Content != null ? await request.Content.ReadAsStringAsync() : null;
            return new HttpResponseMessage(System.Net.HttpStatusCode.NoContent);
        });

        await _client.CancelBatchOperationAsync(BatchOperationKey.AssumeExists("123"));

        Assert.Single(_handler.Requests);
        Assert.True(hadContent, "Expected a request content body.");
        Assert.Equal("{}", capturedBody);
    }

    [Fact]
    public async Task SchemalessOptionalBody_Supplied_SendsSerialisedBody()
    {
        string? capturedBody = null;
        _handler.Enqueue(async request =>
        {
            capturedBody = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(System.Net.HttpStatusCode.NoContent);
        });

        await _client.CancelBatchOperationAsync(BatchOperationKey.AssumeExists("123"), new { reason = "test" });

        Assert.Single(_handler.Requests);
        Assert.Contains("\"reason\":\"test\"", capturedBody);
    }

    [Fact]
    public async Task SchemafulOptionalBody_Omitted_SendsNoContent()
    {
        // Issue #469: listSecrets' request body is optional and typed. The JS
        // SDK sends no body when the caller omits it — the C# SDK must match.
        var hadContent = false;
        _handler.Enqueue(request =>
        {
            hadContent = request.Content != null;
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"references\":[]}", System.Text.Encoding.UTF8, "application/json"),
            };
        });

        await _client.ListSecretsAsync();

        Assert.Single(_handler.Requests);
        Assert.False(hadContent, "Expected no request content body.");
    }

    [Fact]
    public async Task SchemafulOptionalBody_Supplied_SendsSerialisedBody()
    {
        string? capturedBody = null;
        _handler.Enqueue(async request =>
        {
            capturedBody = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"references\":[]}", System.Text.Encoding.UTF8, "application/json"),
            };
        });

        await _client.ListSecretsAsync(new SecretListRequest());

        Assert.Single(_handler.Requests);
        Assert.Equal("{}", capturedBody);
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
