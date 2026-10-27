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

    [Fact]
    public async Task SendMultipartAsync_RewindsSeekableStream_WhenSerializationThrows()
    {
        // If the upload copy throws after consuming a prefix of the caller's stream, the
        // rewind must still run (finally) — otherwise the next attempt wraps the advanced
        // position as its origin and re-sends only the remaining bytes (a truncated file).
        var payload = new byte[1024];
        new Random(3).NextBytes(payload);
        var stream = new MemoryStream(payload);
        var streamContent = new StreamContent(stream);
        var content = new MultipartFormDataContent();
        content.Add(streamContent, "resources", "big.bpmn");

        // First attempt: the handler reads only a prefix of the body, then fails the
        // request. The partial read advances the caller's stream before the 503 is seen.
        _handler.Enqueue(async request =>
        {
            var body = await request.Content!.ReadAsByteArrayAsync();
            Assert.True(body.Length > 0);
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent(
                    """{"title":"RESOURCE_EXHAUSTED","status":503,"detail":"broker overloaded"}""",
                    System.Text.Encoding.UTF8, "application/json"),
            };
        });
        _handler.Enqueue(HttpStatusCode.OK, """{"ok":true}""");

        // CreateDeploymentAsync wraps the multipart send in InvokeWithRetryAsync, so the
        // 503 triggers a second attempt against the same caller-owned content.
        await _client.CreateDeploymentAsync(content);

        // Whatever happened mid-attempt, the caller's stream is rewound for reuse.
        Assert.Equal(0, stream.Position);
        Assert.Equal(2, _handler.Requests.Count);
    }

    [Fact]
    public async Task SendMultipartAsync_ForwardsCancellation_ToSeekableStreamRead()
    {
        // The replayable wrapper must forward the request's cancellation token into the
        // source stream read. A source read that is still pending when the token is
        // cancelled must observe it (OperationCanceledException) rather than hang.
        var readStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stream = new CancellableReadStream(readStarted);
        var streamContent = new StreamContent(stream);
        var content = new MultipartFormDataContent();
        content.Add(streamContent, "resources", "big.bpmn");

        // Enqueue a handler that reads the request content (serializing the multipart
        // body) with the request's cancellation token before responding — that read is
        // what blocks on the source stream. A plain Enqueue(status) returns without
        // reading the body, so the source stream would never be read and the test could
        // not observe the cancellation.
        _handler.Enqueue(async (request, ct) =>
        {
            await request.Content!.ReadAsByteArrayAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"ok":true}""", System.Text.Encoding.UTF8, "application/json"),
            };
        });

        using var cts = new CancellationTokenSource();
        var send = _client.SendMultipartAsync<object>("deployments", content, ct: cts.Token);

        // Cancel once the upload is actually reading from the (blocked) source stream.
        // Bound the wait so a serialization path that never reads the source fails fast
        // instead of hanging the suite.
        var readObserved = await Task.WhenAny(readStarted.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.True(ReferenceEquals(readObserved, readStarted.Task),
            "the upload never read from the source stream");
        cts.Cancel();

        // The cancelled read must fault the send with OperationCanceledException. Bound
        // the wait: if the wrapper drops the token, the source read never completes and
        // this times out (a deterministic failure) instead of hanging the suite.
        var completed = await Task.WhenAny(send, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.True(ReferenceEquals(completed, send),
            "cancelling the request did not reach the pending source-stream read");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await send);
    }

    /// <summary>
    /// A seekable stream whose read blocks until cancelled, to prove the request's
    /// cancellation token reaches the source read. Reports a fixed length and seeks
    /// freely so it is treated as replayable; the read only completes via cancellation.
    /// </summary>
    private sealed class CancellableReadStream : Stream
    {
        private readonly TaskCompletionSource _readStarted;

        public CancellableReadStream(TaskCompletionSource readStarted)
        {
            _readStarted = readStarted;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => 1024;
        public override long Position { get; set; }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            _readStarted.TrySetResult();
            // Block until the request's token is cancelled; a missing token hangs forever.
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _readStarted.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) { Position = offset; return offset; }
        public override void SetLength(long value) { }
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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
