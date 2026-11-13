using System.Net;
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
                    JsonSerializer.Serialize(new { access_token = "tok-123", expires_in = 3600 }),
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
        Assert.Equal("tok-123", token);

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
