using System.Net;
using FluentAssertions;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Tests for multipart (deployment) send error paths.
/// </summary>
public class MultipartSendTests : IDisposable
{
    private readonly MockHttpMessageHandler _handler;
    private readonly CamundaClient _client;

    public MultipartSendTests()
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
    public async Task SendMultipartAsync_ThrowsOnNon2xxResponse()
    {
        _handler.Enqueue(HttpStatusCode.BadRequest,
            """{"title":"INVALID_ARGUMENT","detail":"Missing resource"}""");

        var content = new MultipartFormDataContent();
        content.Add(new StringContent("test"), "resources", "test.bpmn");

        var act = async () => await _client.SendMultipartAsync<object>("deployments", content);
        var ex = (await act.Should().ThrowAsync<HttpSdkException>()).Which;

        ex.Status.Should().Be(400);
        ex.Title.Should().Be("INVALID_ARGUMENT");
        ex.Detail.Should().Be("Missing resource");
    }

    [Fact]
    public async Task SendMultipartAsync_ThrowsOn413PayloadTooLarge()
    {
        _handler.Enqueue(HttpStatusCode.RequestEntityTooLarge,
            """{"title":"REQUEST_TOO_LARGE","detail":"Max deployment size exceeded"}""");

        var content = new MultipartFormDataContent();
        content.Add(new StringContent("huge payload"), "resources", "big.bpmn");

        var act = async () => await _client.SendMultipartAsync<object>("deployments", content);
        var ex = (await act.Should().ThrowAsync<HttpSdkException>()).Which;

        ex.Status.Should().Be(413);
    }

    [Fact]
    public async Task SendMultipartAsync_HandlesNonJsonErrorBody()
    {
        _handler.Enqueue(HttpStatusCode.BadGateway, "Bad Gateway", "text/plain");

        var content = new MultipartFormDataContent();
        content.Add(new StringContent("test"), "resources", "test.bpmn");

        var act = async () => await _client.SendMultipartAsync<object>("deployments", content);
        var ex = (await act.Should().ThrowAsync<HttpSdkException>()).Which;

        ex.Status.Should().Be(502);
        ex.Title.Should().BeNull();
    }

    [Fact]
    public async Task SendMultipartAsync_IdentifiesBackpressureResponse()
    {
        _handler.Enqueue(HttpStatusCode.TooManyRequests,
            """{"title":"Too Many Requests","detail":"Rate limited"}""");

        var content = new MultipartFormDataContent();
        content.Add(new StringContent("test"), "resources", "test.bpmn");

        var act = async () => await _client.SendMultipartAsync<object>("deployments", content);
        var ex = (await act.Should().ThrowAsync<HttpSdkException>()).Which;

        ex.Status.Should().Be(429);
        ex.IsBackpressure.Should().BeTrue();
    }

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }
}
