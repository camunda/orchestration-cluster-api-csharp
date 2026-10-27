using System.Net;

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
        var ex = await Assert.ThrowsAsync<HttpSdkException>(act);

        Assert.Equal(400, ex.Status);
        Assert.Equal("INVALID_ARGUMENT", ex.Title);
        Assert.Equal("Missing resource", ex.Detail);
    }

    [Fact]
    public async Task SendMultipartAsync_ThrowsOn413PayloadTooLarge()
    {
        _handler.Enqueue(HttpStatusCode.RequestEntityTooLarge,
            """{"title":"REQUEST_TOO_LARGE","detail":"Max deployment size exceeded"}""");

        var content = new MultipartFormDataContent();
        content.Add(new StringContent("huge payload"), "resources", "big.bpmn");

        var act = async () => await _client.SendMultipartAsync<object>("deployments", content);
        var ex = await Assert.ThrowsAsync<HttpSdkException>(act);

        Assert.Equal(413, ex.Status);
    }

    [Fact]
    public async Task SendMultipartAsync_HandlesNonJsonErrorBody()
    {
        _handler.Enqueue(HttpStatusCode.BadGateway, "Bad Gateway", "text/plain");

        var content = new MultipartFormDataContent();
        content.Add(new StringContent("test"), "resources", "test.bpmn");

        var act = async () => await _client.SendMultipartAsync<object>("deployments", content);
        var ex = await Assert.ThrowsAsync<HttpSdkException>(act);

        Assert.Equal(502, ex.Status);
        Assert.Null(ex.Title);
    }

    [Fact]
    public async Task SendMultipartAsync_IdentifiesBackpressureResponse()
    {
        _handler.Enqueue(HttpStatusCode.TooManyRequests,
            """{"title":"Too Many Requests","detail":"Rate limited"}""");

        var content = new MultipartFormDataContent();
        content.Add(new StringContent("test"), "resources", "test.bpmn");

        var act = async () => await _client.SendMultipartAsync<object>("deployments", content);
        var ex = await Assert.ThrowsAsync<HttpSdkException>(act);

        Assert.Equal(429, ex.Status);
        Assert.True(ex.IsBackpressure);
    }

    // ---- Multipart cloning must not exhaust memory (#479 review) ----
    // Cloning every part with ReadAsByteArrayAsync buffers the whole payload before
    // sending — even when the first attempt succeeds — so a large StreamContent upload
    // (or several concurrent ones) can exhaust memory. A replayable (seekable) stream
    // must be streamed from its current position on each attempt and left open for the
    // caller; only a non-seekable stream needs a one-time bounded buffer.

    [Fact]
    public async Task SendMultipartAsync_StreamsSeekableContent_WithoutBufferingOrDisposing()
    {
        _handler.Enqueue(HttpStatusCode.OK, """{"ok":true}""");

        // A seekable stream stands in for a large upload. If the clone buffered it, the
        // stream would be fully read and left at the end; if it disposed it, the caller
        // could not reuse it. Either is the regression.
        var payload = new byte[1024];
        new Random(1).NextBytes(payload);
        var stream = new MemoryStream(payload);
        var streamContent = new StreamContent(stream);
        var content = new MultipartFormDataContent();
        content.Add(streamContent, "resources", "big.bpmn");

        await _client.SendMultipartAsync<object>("deployments", content);

        // The caller's stream must survive the send (ownership preserved) and be left
        // rewound so a subsequent retry attempt re-reads it from the start.
        Assert.True(stream.CanRead);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public async Task SendMultipartAsync_BuffersNonSeekableContent_ForReplay()
    {
        _handler.Enqueue(HttpStatusCode.OK, """{"ok":true}""");

        // A non-seekable stream cannot be rewound for a retry, so it must be buffered
        // once. The send must still succeed and serialize the payload.
        var payload = new byte[512];
        new Random(2).NextBytes(payload);
        var stream = new NonSeekableReadStream(payload);
        var streamContent = new StreamContent(stream);
        var content = new MultipartFormDataContent();
        content.Add(streamContent, "resources", "stream.bpmn");

        await _client.SendMultipartAsync<object>("deployments", content);

        Assert.Single(_handler.Requests);
    }

    /// <summary>A read-only, forward-only stream that reports <see cref="Stream.CanSeek"/> false.</summary>
    private sealed class NonSeekableReadStream : MemoryStream
    {
        public NonSeekableReadStream(byte[] buffer)
            : base(buffer)
        {
        }

        public override bool CanSeek => false;
    }

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }
}
