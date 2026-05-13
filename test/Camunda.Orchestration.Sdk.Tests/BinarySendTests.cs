using System.Net;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Tests for the binary response path (SendBinaryAsync).
/// Verifies that octet-stream endpoints return raw byte[] and
/// that non-2xx responses still throw HttpSdkException.
/// </summary>
public class BinarySendTests : IDisposable
{
    private readonly MockHttpMessageHandler _handler;

    public BinarySendTests()
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
    public async Task SendBinaryAsync_ReturnsExactBytes()
    {
        var expected = new byte[] { 0x00, 0x50, 0x4E, 0x47, 0xFF, 0xD8, 0x01, 0x02 };
        _handler.Enqueue(r => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(expected),
        });

        using var client = CreateClient();
        var docId = DocumentId.AssumeExists("doc-123");

        var result = await client.GetDocumentAsync(docId);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task SendBinaryAsync_ThrowsOnNon2xxResponse()
    {
        _handler.Enqueue(HttpStatusCode.NotFound,
            """{"title":"NOT_FOUND","detail":"Document not found"}""");

        using var client = CreateClient();
        var docId = DocumentId.AssumeExists("doc-123");

        var act = async () => await client.GetDocumentAsync(docId);
        var ex = await Assert.ThrowsAsync<HttpSdkException>(act);
        Assert.Equal(404, ex.Status);
    }

    [Fact]
    public async Task SendBinaryAsync_ReturnsEmptyArrayForEmptyBody()
    {
        _handler.Enqueue(r => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Array.Empty<byte>()),
        });

        using var client = CreateClient();
        var docId = DocumentId.AssumeExists("doc-456");

        var result = await client.GetDocumentAsync(docId);

        Assert.Empty(result);
    }

    public void Dispose()
    {
        _handler.Dispose();
        GC.SuppressFinalize(this);
    }
}
