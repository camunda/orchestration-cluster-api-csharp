using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Camunda.Client.Runtime;

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

    public AuthHandler(CamundaConfig config, HttpMessageHandler? inner, ILogger? logger)
        : base(inner ?? new HttpClientHandler())
    {
        _config = config;
        _logger = logger ?? NullLogger.Instance;

        switch (config.Auth.Strategy)
        {
            case AuthStrategy.OAuth:
                _oauth = new OAuthManager(config, _logger);
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
            // Use a separate HttpClient for token fetches to avoid recursion
            using var tokenClient = new HttpClient();
            var token = await _oauth.GetTokenAsync(tokenClient, ct);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        else if (_basicHeader != null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", _basicHeader);
        }

        return await base.SendAsync(request, ct);
    }

    // Expose for tests
    internal OAuthManager? OAuthManagerInternal => _oauth;
}
