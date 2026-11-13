using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Camunda.Orchestration.Sdk;

/// <summary>
/// Auth handler that injects authentication headers into HTTP requests.
/// Installed as a DelegatingHandler in the HttpClient pipeline.
/// </summary>
internal sealed class AuthHandler : DelegatingHandler
{
    private readonly CamundaConfig _config;
    private readonly OAuthManager? _oauth;
    private readonly string? _basicHeader;
    private readonly ILogger _logger;
    private readonly object _tokenClientGate = new();
    private HttpClient? _tokenClient;

    public AuthHandler(CamundaConfig config, HttpMessageHandler? inner, ILogger? logger, TimeProvider timeProvider, IRandomSource randomSource)
        : base(inner ?? new HttpClientHandler())
    {
        _config = config;
        _logger = logger ?? NullLogger.Instance;

        switch (config.Auth.Strategy)
        {
            case AuthStrategy.OAuth:
                _oauth = new OAuthManager(config, _logger, timeProvider, randomSource);
                break;
            case AuthStrategy.Basic:
                if (config.Auth.Basic is { Username: not null, Password: not null })
                {
                    var encoded = Convert.ToBase64String(
                        Encoding.UTF8.GetBytes($"{config.Auth.Basic.Username}:{config.Auth.Basic.Password}"));
                    _basicHeader = $"Basic {encoded}";
                }
                break;
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (_oauth != null)
        {
            var token = await _oauth.GetTokenAsync(GetTokenClient(), ct);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        else if (_basicHeader != null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", _basicHeader);
        }

        return await base.SendAsync(request, ct);
    }

    /// <summary>
    /// The HttpClient used for token-endpoint calls, created on first use and reused for the
    /// lifetime of this handler.
    ///
    /// <para>It is separate from the pipeline's own client so that a token fetch does not recurse
    /// back through this handler, and it carries the TLS handler so token requests present the
    /// same custom certificates as everything else.</para>
    ///
    /// <para>One per handler, not one per request: an HttpClient owns a connection pool, so
    /// building and disposing one on every outbound call left sockets in TIME_WAIT (port
    /// exhaustion under sustained load), gave the token endpoint a cold TLS handshake every
    /// refresh, and — with mTLS configured — re-read and re-parsed the certificate and key from
    /// disk on the hot path of every single request.</para>
    /// </summary>
    private HttpClient GetTokenClient()
    {
        lock (_tokenClientGate)
        {
            // Left unset when BuildHandler throws (a missing certificate file, say), so the next
            // send retries the build rather than caching the failure for the handler's lifetime.
            if (_tokenClient == null)
            {
                var tlsHandler = TlsHelper.BuildHandler(_config.Tls);
                _tokenClient = tlsHandler != null ? new HttpClient(tlsHandler) : new HttpClient();
            }

            return _tokenClient;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _oauth?.Dispose();

            lock (_tokenClientGate)
            {
                _tokenClient?.Dispose();
                _tokenClient = null;
            }
        }

        base.Dispose(disposing);
    }

    // Expose for tests
    internal OAuthManager? OAuthManagerInternal => _oauth;
}
