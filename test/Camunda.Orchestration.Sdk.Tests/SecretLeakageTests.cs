using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Sentinel-secret regression test required by the shared SDK policy ("Secrets must not leak").
/// A recognisable sentinel is configured, the auth success and auth failure paths are driven with
/// logging fully enabled, and the sentinel must appear in none of the captured log output, the
/// exceptions surfaced to the caller, or the <c>ToString()</c> of anything in the configuration
/// graph.
///
/// <para>Class-of-defect scope: the audited paths are clean today, so these cases exist to keep
/// them that way. Rather than naming the individual log statements that exist now, the assertions
/// are over the <em>aggregate</em> of everything the SDK emitted during the run, and the
/// configuration case walks the whole config object graph by reflection and requires every node's
/// <c>ToString()</c> to be the default. That is what catches the realistic regression: turning one
/// config class into a <c>record</c> (whose generated <c>ToString()</c> prints every property,
/// secrets included) or adding a diagnostic that formats the config. Extend this file when a new
/// place a credential can flow appears.</para>
///
/// <para>Each case also asserts the credential really did travel the path under test, so a future
/// refactor that stops exercising auth altogether cannot leave these tests vacuously green.</para>
/// </summary>
public class SecretLeakageTests
{
    private const string ClientSecretSentinel = "sentinel-aaaaaaaaaaaa";
    private const string BasicPasswordSentinel = "sentinel-bbbbbbbbbbbb";
    private const string KeyPassphraseSentinel = "sentinel-cccccccccccc";

    // The issued access token is a bearer credential in its own right, so it carries a
    // sentinel too: logging the token, or the whole token response, must fail these tests.
    private const string AccessTokenSentinel = "sentinel-dddddddddddd";

    private static CamundaConfig OAuthConfigWithSentinel() => new()
    {
        RestAddress = "https://mock.local",
        TokenAudience = "test-audience",
        Auth = new AuthConfig { Strategy = AuthStrategy.OAuth },
        OAuth = new OAuthConfig
        {
            ClientId = "sentinel-client-id",
            ClientSecret = ClientSecretSentinel,
            OAuthUrl = "https://auth.mock/token",
            Retry = new OAuthRetryConfig { Max = 1, BaseDelayMs = 1 },
            TimeoutMs = 5000,
        },
    };

    [Fact]
    public async Task OAuth_success_path_never_leaks_the_client_secret()
    {
        var logs = new CapturingLoggerFactory();
        var handler = new MockHttpMessageHandler();
        string? tokenRequestBody = null;
        handler.Enqueue(async req =>
        {
            tokenRequestBody = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { access_token = AccessTokenSentinel, expires_in = 3600 }),
                    Encoding.UTF8,
                    "application/json"),
            };
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://auth.mock/") };

        using var oauth = new OAuthManager(
            OAuthConfigWithSentinel(),
            logs.CreateLogger("oauth"),
            TimeProvider.System,
            CamundaRandomSource.Live);

        var token = await oauth.GetTokenAsync(client);
        Assert.Equal(AccessTokenSentinel, token);

        // Not vacuous: the secret really was presented to the token endpoint.
        Assert.NotNull(tokenRequestBody);
        Assert.Contains(ClientSecretSentinel, tokenRequestBody!, StringComparison.Ordinal);

        AssertNoSentinel(logs.AllOutput);
    }

    [Fact]
    public async Task OAuth_failure_path_never_leaks_the_client_secret()
    {
        var logs = new CapturingLoggerFactory();
        var handler = new MockHttpMessageHandler();

        // A hostile-but-plausible token endpoint: the 401 body echoes the request it received,
        // sentinel included. Nothing the SDK surfaces may carry that body forward.
        handler.Enqueue(HttpStatusCode.Unauthorized,
            JsonSerializer.Serialize(new
            {
                error = "invalid_client",
                echo = $"client_secret={ClientSecretSentinel}",
            }));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://auth.mock/") };

        using var oauth = new OAuthManager(
            OAuthConfigWithSentinel(),
            logs.CreateLogger("oauth"),
            TimeProvider.System,
            CamundaRandomSource.Live);

        var ex = await Assert.ThrowsAsync<CamundaAuthException>(() => oauth.GetTokenAsync(client));
        Assert.Equal(CamundaAuthErrorCode.TokenFetchFailed, ex.Code);

        // ToString() covers the message, the inner exception chain and the stack trace — the whole
        // of what a caller is likely to log or ship to an error tracker.
        AssertNoSentinel(ex.ToString());
        AssertNoSentinel(logs.AllOutput);
    }

    [Fact]
    public async Task Basic_auth_never_leaks_the_password_on_success_or_failure()
    {
        var logs = new CapturingLoggerFactory();
        var handler = new MockHttpMessageHandler();
        var authorizationHeaders = new List<string?>();

        handler.Enqueue(req =>
        {
            authorizationHeaders.Add(req.Headers.TryGetValues("Authorization", out var v) ? string.Join(',', v) : null);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        handler.Enqueue(req =>
        {
            authorizationHeaders.Add(req.Headers.TryGetValues("Authorization", out var v) ? string.Join(',', v) : null);
            return new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                // Deliberately generic: what a server puts in its own error body is the server's
                // business, and the SDK surfaces it by design. What is asserted here is that the
                // SDK adds no credential of its own to the message it builds.
                Content = new StringContent("{\"title\":\"Unauthorized\"}", Encoding.UTF8, "application/json"),
            };
        });

        using var camunda = new CamundaClient(new CamundaOptions
        {
            Config = new Dictionary<string, string>
            {
                [ConfigKeys.RestAddress] = "https://mock.local",
                [ConfigKeys.AuthStrategy] = "BASIC",
                [ConfigKeys.BasicAuthUsername] = "sentinel-user",
                [ConfigKeys.BasicAuthPassword] = BasicPasswordSentinel,
                [ConfigKeys.HttpRetryMaxAttempts] = "1",
            },
            HttpMessageHandler = handler,
            LoggerFactory = logs,
        });

        await camunda.CancelBatchOperationAsync(BatchOperationKey.AssumeExists("123"));
        var failure = await Record.ExceptionAsync(
            () => camunda.CancelBatchOperationAsync(BatchOperationKey.AssumeExists("123")));

        Assert.NotNull(failure);

        // Not vacuous: both requests really did carry the basic credential.
        var expectedHeader = "Basic " + Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"sentinel-user:{BasicPasswordSentinel}"));
        Assert.Equal(2, authorizationHeaders.Count);
        Assert.All(authorizationHeaders, h => Assert.Equal(expectedHeader, h));

        // The encoded form is checked too: base64 is not redaction.
        var encodedCredential = expectedHeader["Basic ".Length..];
        AssertNoSentinel(logs.AllOutput);
        AssertNoSentinel(failure!.ToString());
        Assert.DoesNotContain(encodedCredential, logs.AllOutput, StringComparison.Ordinal);
        Assert.DoesNotContain(encodedCredential, failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OAuth_client_never_leaks_secrets_on_resource_success_or_failure()
    {
        // The two OAuth cases above drive OAuthManager directly. This one goes through the
        // client, so the OAuth branch of AuthHandler (token fetch plus Bearer injection) and
        // the client-level diagnostics sit inside the guardrail too.
        using var tokenEndpoint = new LoopbackTokenEndpoint(_ =>
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { access_token = AccessTokenSentinel, expires_in = 3600 })));
        var logs = new CapturingLoggerFactory();
        var resourceHandler = new MockHttpMessageHandler();
        var authorizationHeaders = new List<string?>();

        resourceHandler.Enqueue(req =>
        {
            authorizationHeaders.Add(req.Headers.TryGetValues("Authorization", out var v) ? string.Join(',', v) : null);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        resourceHandler.Enqueue(req =>
        {
            authorizationHeaders.Add(req.Headers.TryGetValues("Authorization", out var v) ? string.Join(',', v) : null);
            return new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                // Generic on purpose, as in the basic-auth case: only what the SDK adds is asserted.
                Content = new StringContent("{\"title\":\"Unauthorized\"}", Encoding.UTF8, "application/json"),
            };
        });

        using var camunda = OAuthClient(tokenEndpoint.Url, resourceHandler, logs);

        await camunda.CancelBatchOperationAsync(BatchOperationKey.AssumeExists("123"));
        var failure = await Record.ExceptionAsync(
            () => camunda.CancelBatchOperationAsync(BatchOperationKey.AssumeExists("123")));

        Assert.NotNull(failure);

        // Not vacuous: the client secret really went to the token endpoint, and both resource
        // requests really carried the issued token as a Bearer credential.
        var tokenRequest = Assert.Single(tokenEndpoint.RequestBodies);
        Assert.Contains(ClientSecretSentinel, tokenRequest, StringComparison.Ordinal);
        Assert.Equal(2, authorizationHeaders.Count);
        Assert.All(authorizationHeaders, h => Assert.Equal("Bearer " + AccessTokenSentinel, h));

        AssertNoSentinel(logs.AllOutput);
        AssertNoSentinel(failure!.ToString());
    }

    [Fact]
    public async Task OAuth_client_never_leaks_secrets_when_the_token_endpoint_rejects_the_credentials()
    {
        // The token endpoint's 401 body echoes the request it received, sentinel included.
        using var tokenEndpoint = new LoopbackTokenEndpoint(requestBody =>
            (HttpStatusCode.Unauthorized, JsonSerializer.Serialize(new { error = "invalid_client", echo = requestBody })));
        var logs = new CapturingLoggerFactory();
        var resourceHandler = new MockHttpMessageHandler();

        using var camunda = OAuthClient(tokenEndpoint.Url, resourceHandler, logs);

        var failure = await Record.ExceptionAsync(
            () => camunda.CancelBatchOperationAsync(BatchOperationKey.AssumeExists("123")));

        Assert.NotNull(failure);

        // Not vacuous: the secret reached the token endpoint, the failure is the auth failure,
        // and no resource request was sent without a token.
        var tokenRequest = Assert.Single(tokenEndpoint.RequestBodies);
        Assert.Contains(ClientSecretSentinel, tokenRequest, StringComparison.Ordinal);
        Assert.Contains(AuthExceptionsIn(failure!), e => e.Code == CamundaAuthErrorCode.TokenFetchFailed);
        Assert.Empty(resourceHandler.Requests);

        AssertNoSentinel(logs.AllOutput);
        AssertNoSentinel(failure!.ToString());
    }

    [Fact]
    public void No_configuration_object_prints_its_values()
    {
        var config = new CamundaConfig
        {
            RestAddress = "https://mock.local",
            Auth = new AuthConfig
            {
                Strategy = AuthStrategy.Basic,
                Basic = new BasicAuthConfig { Username = "sentinel-user", Password = BasicPasswordSentinel },
            },
            OAuth = new OAuthConfig { ClientId = "sentinel-client-id", ClientSecret = ClientSecretSentinel },
            Tls = new TlsConfig { Key = "sentinel-pem-key", KeyPassphrase = KeyPassphraseSentinel },
        };

        var nodes = ConfigGraph(config).ToList();

        // Sanity: the walk really reached the secret-bearing leaves.
        Assert.Contains(nodes, n => n is BasicAuthConfig);
        Assert.Contains(nodes, n => n is OAuthConfig);
        Assert.Contains(nodes, n => n is TlsConfig);

        foreach (var node in nodes)
        {
            // The default ToString() is the type name and nothing else. Anything else — an
            // override, or the compiler-generated printer a record gets — is a potential leak of
            // every property it prints, including ones added after this test was written.
            Assert.Equal(node.GetType().FullName, node.ToString());
        }
    }

    [Fact]
    public void Client_ToString_does_not_print_configuration()
    {
        using var camunda = new CamundaClient(new CamundaOptions
        {
            Config = new Dictionary<string, string>
            {
                [ConfigKeys.RestAddress] = "https://mock.local",
                [ConfigKeys.AuthStrategy] = "BASIC",
                [ConfigKeys.BasicAuthUsername] = "sentinel-user",
                [ConfigKeys.BasicAuthPassword] = BasicPasswordSentinel,
            },
            HttpMessageHandler = new MockHttpMessageHandler(),
        });

        Assert.Equal(typeof(CamundaClient).FullName, camunda.ToString());
        AssertNoSentinel(camunda.ToString()!);
    }

    private static void AssertNoSentinel(string haystack)
    {
        Assert.DoesNotContain(ClientSecretSentinel, haystack, StringComparison.Ordinal);
        Assert.DoesNotContain(BasicPasswordSentinel, haystack, StringComparison.Ordinal);
        Assert.DoesNotContain(KeyPassphraseSentinel, haystack, StringComparison.Ordinal);
        Assert.DoesNotContain(AccessTokenSentinel, haystack, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every object reachable from <paramref name="root"/> through properties whose declared type
    /// is an SDK class, plus the root itself.
    /// </summary>
    private static IEnumerable<object> ConfigGraph(object root)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<object>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (!seen.Add(node))
                continue;

            yield return node;

            foreach (var property in node.GetType().GetProperties())
            {
                if (!IsSdkClass(property.PropertyType))
                    continue;
                if (property.GetValue(node) is { } child)
                    pending.Push(child);
            }
        }
    }

    private static bool IsSdkClass(Type type) =>
        type.IsClass
        && type.Assembly == typeof(CamundaClient).Assembly
        && type.Namespace == typeof(CamundaClient).Namespace;

    private static CamundaClient OAuthClient(string tokenUrl, MockHttpMessageHandler resourceHandler, CapturingLoggerFactory logs) =>
        new(new CamundaOptions
        {
            Config = new Dictionary<string, string>
            {
                [ConfigKeys.RestAddress] = "https://mock.local",
                [ConfigKeys.AuthStrategy] = "OAUTH",
                [ConfigKeys.ClientId] = "sentinel-client-id",
                [ConfigKeys.ClientSecret] = ClientSecretSentinel,
                [ConfigKeys.OAuthUrl] = tokenUrl,
                [ConfigKeys.TokenAudience] = "test-audience",
                [ConfigKeys.OAuthRetryMax] = "1",
                [ConfigKeys.OAuthRetryBaseDelayMs] = "1",
                [ConfigKeys.OAuthTimeoutMs] = "5000",
                [ConfigKeys.HttpRetryMaxAttempts] = "1",
            },
            HttpMessageHandler = resourceHandler,
            LoggerFactory = logs,
        });

    private static IEnumerable<CamundaAuthException> AuthExceptionsIn(Exception root)
    {
        for (Exception? e = root; e != null; e = e.InnerException)
        {
            if (e is CamundaAuthException auth)
                yield return auth;
        }
    }

    /// <summary>
    /// A real token endpoint on a loopback port. <c>AuthHandler</c> fetches tokens through its own
    /// <c>HttpClient</c>, which a test cannot hand a stub handler, so the endpoint has to speak
    /// HTTP. The listener owns its port (bound to 0), so there is no race for a free one.
    /// Requests are served one at a time, which is all these tests need.
    /// </summary>
    private sealed class LoopbackTokenEndpoint : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Func<string, (HttpStatusCode Status, string Body)> _respond;
        private readonly List<string> _requestBodies = new();
        private readonly object _gate = new();
        private readonly Task _loop;

        public LoopbackTokenEndpoint(Func<string, (HttpStatusCode Status, string Body)> respond)
        {
            _respond = respond;
            _listener.Start();
            _loop = Task.Run(AcceptLoopAsync);
        }

        public string Url => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/token";

        public IReadOnlyList<string> RequestBodies
        {
            get { lock (_gate) return _requestBodies.ToList(); }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
            _loop.GetAwaiter().GetResult();
            _stop.Dispose();
        }

        private async Task AcceptLoopAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    using var connection = await _listener.AcceptTcpClientAsync(_stop.Token);
                    await ServeAsync(connection, _stop.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException)
                {
                    // A listener that was stopped, or a client that went away mid-request.
                    if (_stop.IsCancellationRequested)
                        return;
                }
                catch (IOException)
                {
                    // A client that went away mid-request; keep serving.
                }
            }
        }

        private static readonly byte[] HeaderSeparator = "\r\n\r\n"u8.ToArray();

        private static int IndexOfHeaderEnd(MemoryStream received) =>
            received.GetBuffer().AsSpan(0, (int)received.Length).IndexOf(HeaderSeparator);

        private async Task ServeAsync(TcpClient connection, CancellationToken ct)
        {
            var stream = connection.GetStream();
            var received = new MemoryStream();
            var chunk = new byte[4096];

            int headerEnd;
            while ((headerEnd = IndexOfHeaderEnd(received)) < 0)
            {
                var read = await stream.ReadAsync(chunk, ct);
                if (read == 0)
                    return;
                received.Write(chunk, 0, read);
            }

            var headers = Encoding.ASCII.GetString(received.GetBuffer(), 0, headerEnd);
            var contentLength = 0;
            foreach (var line in headers.Split("\r\n"))
            {
                const string name = "content-length:";
                if (line.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                    contentLength = int.Parse(line[name.Length..].Trim(), CultureInfo.InvariantCulture);
            }

            var bodyStart = headerEnd + HeaderSeparator.Length;
            while (received.Length - bodyStart < contentLength)
            {
                var read = await stream.ReadAsync(chunk, ct);
                if (read == 0)
                    return;
                received.Write(chunk, 0, read);
            }

            var requestBody = Encoding.UTF8.GetString(received.GetBuffer(), bodyStart, contentLength);
            lock (_gate)
                _requestBodies.Add(requestBody);

            var (status, body) = _respond(requestBody);
            var payload = Encoding.UTF8.GetBytes(body);
            var head = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {(int)status} {status}\r\nContent-Type: application/json\r\n" +
                $"Content-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(head, ct);
            await stream.WriteAsync(payload, ct);
            await stream.FlushAsync(ct);
        }
    }

    /// <summary>
    /// Records everything the SDK logs, at every level, as one searchable blob. <c>IsEnabled</c>
    /// always returns true so that every level-guarded statement in the SDK actually runs — the
    /// equivalent of configuring the SDK logger at trace.
    /// </summary>
    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
        private readonly StringBuilder _output = new();
        private readonly object _gate = new();

        public string AllOutput
        {
            get { lock (_gate) return _output.ToString(); }
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, this);

        public void AddProvider(ILoggerProvider provider) { }

        public void Dispose() { }

        private void Append(string category, LogLevel level, string message, Exception? exception)
        {
            lock (_gate)
            {
                _output.Append(category).Append(' ').Append(level).Append(' ').AppendLine(message);
                if (exception != null)
                    _output.AppendLine(exception.ToString());
            }
        }

        private sealed class CapturingLogger(string category, CapturingLoggerFactory owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
                => owner.Append(category, logLevel, formatter(state, exception), exception);
        }
    }
}
