using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging.Abstractions;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Pins that the token-endpoint transport is built once per <c>AuthHandler</c> rather than once
/// per outbound request.
///
/// <para>Class-of-defect scope: the defect is "the token transport is constructed on the request
/// path", not any single consequence of it, so the test asserts construction itself happens once
/// — everything else (sockets left in TIME_WAIT, a cold TLS handshake per refresh, certificate
/// files re-read on every call) follows from that one fact.</para>
///
/// <para>Construction is observed through the certificate file, because that is the one
/// externally visible side effect of building the transport and it needs no test hook in
/// production code: with a CA bundle configured, building the transport reads that file. The
/// file is removed after the first send, so a handler that rebuilds per request reads a file that
/// is no longer there and fails with <see cref="FileNotFoundException"/>, while a handler that
/// built it once never touches the disk again. The token fetch itself fails in both cases —
/// nothing is listening on the configured endpoint — which is deliberate: this test is about the
/// transport's lifetime, not about fetching a token, and both sends are required to fail the same
/// way.</para>
/// </summary>
public class AuthHandlerTokenClientTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly string _caCertPath;

    public AuthHandlerTokenClientTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), $"auth-handler-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tmpDir);

        using var caKey = RSA.Create(2048);
        var caReq = new CertificateRequest("CN=token-client-test-ca", caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        caReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        var notBefore = DateTimeOffset.UtcNow;
        using var caCert = caReq.CreateSelfSigned(notBefore, notBefore.AddYears(10));

        _caCertPath = Path.Combine(_tmpDir, "ca.crt");
        File.WriteAllText(_caCertPath, caCert.ExportCertificatePem());
    }

    public void Dispose()
    {
        try
        { Directory.Delete(_tmpDir, true); }
        catch { /* ignore */ }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Token_transport_is_built_once_per_handler_not_once_per_request()
    {
        using var handler = new AuthHandler(
            OAuthConfig(_caCertPath), new MockHttpMessageHandler(), NullLogger.Instance, TimeProvider.System, CamundaRandomSource.Live);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://mock.local/") };

        var first = await Record.ExceptionAsync(() => client.GetAsync("/topology"));
        Assert.True(HasInChain<CamundaAuthException>(first), $"first send: {first}");
        Assert.False(HasInChain<FileNotFoundException>(first), $"first send: {first}");

        File.Delete(_caCertPath);

        var second = await Record.ExceptionAsync(() => client.GetAsync("/topology"));

        // The transport was not rebuilt, so the missing file is never noticed...
        Assert.False(HasInChain<FileNotFoundException>(second), $"second send: {second}");

        // ...and the send still got as far as the token fetch, so this is not green merely because
        // the request stopped reaching the auth path.
        Assert.True(HasInChain<CamundaAuthException>(second), $"second send: {second}");
    }

    [Fact]
    public async Task Token_transport_is_not_rebuilt_after_the_handler_is_disposed()
    {
        // The other half of a single owned transport: a send still in flight when the handler is
        // disposed must not quietly build a replacement that nothing will ever dispose.
        //
        // Observing that needs the same trick as the test above. Without the disposed guard the
        // send would build a replacement and then fail anyway, because OAuthManager's own lock is
        // already disposed, so "the send fails" cannot tell the two apart. Building the transport
        // with a CA bundle configured reads the file, though, so with the bundle gone a rebuild
        // surfaces as FileNotFoundException before anything else can throw, while the guard
        // refuses the send before the build is even attempted.
        var handler = new AuthHandler(
            OAuthConfig(_caCertPath), new MockHttpMessageHandler(), NullLogger.Instance, TimeProvider.System, CamundaRandomSource.Live);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://mock.local/") };

        var first = await Record.ExceptionAsync(() => client.GetAsync("/topology"));
        Assert.True(HasInChain<CamundaAuthException>(first), $"first send: {first}");
        Assert.False(HasInChain<FileNotFoundException>(first), $"first send: {first}");

        handler.Dispose();
        File.Delete(_caCertPath);

        var afterDispose = await Record.ExceptionAsync(() => client.GetAsync("/topology"));

        // No rebuild was attempted...
        Assert.False(HasInChain<FileNotFoundException>(afterDispose), $"after dispose: {afterDispose}");

        // ...and the refusal comes from the handler itself, not from a disposed dependency
        // further down the send.
        Assert.True(
            HasInChain<ObjectDisposedException>(afterDispose, e => e.ObjectName == typeof(AuthHandler).FullName),
            $"after dispose: {afterDispose}");
    }

    private static CamundaConfig OAuthConfig(string? caPath) => new()
    {
        RestAddress = "https://mock.local",
        TokenAudience = "test-audience",
        Auth = new AuthConfig { Strategy = AuthStrategy.OAuth },
        OAuth = new OAuthConfig
        {
            ClientId = "test-id",
            ClientSecret = "test-secret",
            // Loopback, refused immediately, and bypassed by any configured proxy: the fetch
            // fails fast and deterministically without reaching the network.
            OAuthUrl = "https://127.0.0.1:1/token",
            Retry = new OAuthRetryConfig { Max = 1, BaseDelayMs = 1 },
            TimeoutMs = 2000,
        },
        Tls = caPath == null ? null : new TlsConfig { CaPath = caPath },
    };

    private static bool HasInChain<T>(Exception? exception, Func<T, bool>? predicate = null) where T : Exception
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is T match && (predicate == null || predicate(match)))
                return true;
            if (current is AggregateException aggregate && aggregate.InnerExceptions.Any(e => HasInChain(e, predicate)))
                return true;
        }

        return false;
    }
}
