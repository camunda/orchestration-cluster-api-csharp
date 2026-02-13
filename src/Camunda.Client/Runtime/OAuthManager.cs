using Microsoft.Extensions.Logging;

namespace Camunda.Client.Runtime;

/// <summary>
/// OAuth token management with singleflight refresh, caching, and retry.
/// Mirrors the JS SDK's OAuthManager.
/// </summary>
internal sealed class OAuthManager : IDisposable
{
    private readonly CamundaConfig _config;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private OAuthToken? _token;

    internal sealed class OAuthToken
    {
        public required string AccessToken { get; init; }
        public required long ExpiresAtEpochMs { get; init; }
        public required long ObtainedAtEpochMs { get; init; }
    }

    public OAuthManager(CamundaConfig config, ILogger logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<string> GetTokenAsync(HttpClient httpClient, CancellationToken ct = default)
    {
        if (_token != null && !ShouldRefresh(_token))
            return _token.AccessToken;

        await _refreshLock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock
            if (_token != null && !ShouldRefresh(_token))
                return _token.AccessToken;

            return await FetchAndStoreAsync(httpClient, ct);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public async Task<string> ForceRefreshAsync(HttpClient httpClient, CancellationToken ct = default)
    {
        await _refreshLock.WaitAsync(ct);
        try
        {
            return await FetchAndStoreAsync(httpClient, ct);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void ClearCache()
    {
        _token = null;
    }

    public void Dispose()
    {
        _refreshLock.Dispose();
    }

    private static bool ShouldRefresh(OAuthToken token)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        const int refreshLeadMs = 5000;
        return now >= token.ExpiresAtEpochMs - refreshLeadMs;
    }

    private async Task<string> FetchAndStoreAsync(HttpClient httpClient, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_config.OAuth.ClientId) || string.IsNullOrEmpty(_config.OAuth.ClientSecret))
            throw new CamundaAuthException(CamundaAuthErrorCode.OAuthConfigMissing, "Missing OAuth client credentials");

        var max = _config.OAuth.Retry.Max;
        var baseDelay = _config.OAuth.Retry.BaseDelayMs;
        Exception? lastError = null;

        for (var attempt = 0; attempt < max; attempt++)
        {
            try
            {
                _logger.LogDebug("OAuth token attempt {Attempt}/{Max}", attempt + 1, max);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(_config.OAuth.TimeoutMs);

                var body = new FormUrlEncodedContent(BuildTokenRequestBody());

                var response = await httpClient.PostAsync(_config.OAuth.OAuthUrl, body, cts.Token);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadFromJsonAsync<TokenResponse>(cts.Token)
                           ?? throw new CamundaAuthException(CamundaAuthErrorCode.TokenParseFailed, "Empty token response");

                if (string.IsNullOrEmpty(json.AccessToken) || json.ExpiresIn <= 0)
                    throw new CamundaAuthException(CamundaAuthErrorCode.TokenParseFailed, "Missing access_token or expires_in in response");

                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var lifetimeMs = json.ExpiresIn * 1000L;
                var skewBuffer = Math.Max(30000, (long)(lifetimeMs * 0.05));

                _token = new OAuthToken
                {
                    AccessToken = json.AccessToken,
                    ObtainedAtEpochMs = now,
                    ExpiresAtEpochMs = now + lifetimeMs - skewBuffer,
                };

                _logger.LogInformation("Token fetched; effective expiry (s)={Expiry}",
                    Math.Round((_token.ExpiresAtEpochMs - now) / 1000.0));

                return _token.AccessToken;
            }
            catch (Exception ex) when (ex is not CamundaAuthException)
            {
                lastError = ex;
                if (attempt + 1 >= max)
                    break;

                var delay = baseDelay * (int)Math.Pow(2, attempt);
                var jitter = (int)(delay * 0.2 * (Random.Shared.NextDouble() - 0.5));
                await Task.Delay(delay + jitter, ct);
            }
        }

        throw new CamundaAuthException(
            CamundaAuthErrorCode.TokenFetchFailed,
            $"Failed to fetch token after {max} attempts: {lastError?.Message ?? "unknown"}",
            lastError);
    }

    private List<KeyValuePair<string, string>> BuildTokenRequestBody()
    {
        var body = new List<KeyValuePair<string, string>>
        {
            new("grant_type", _config.OAuth.GrantType),
            new("client_id", _config.OAuth.ClientId!),
            new("client_secret", _config.OAuth.ClientSecret!),
            new("audience", _config.TokenAudience),
        };
        if (!string.IsNullOrEmpty(_config.OAuth.Scope))
            body.Add(new("scope", _config.OAuth.Scope));
        return body;
    }

    // Internal test helper
    internal void Debug_SetTokenExpiry(long epochMs)
    {
        if (_token != null)
            _token = new OAuthToken
            {
                AccessToken = _token.AccessToken,
                ObtainedAtEpochMs = _token.ObtainedAtEpochMs,
                ExpiresAtEpochMs = epochMs,
            };
    }

    private sealed class TokenResponse
    {
        public string? AccessToken { get; init; }
        public int ExpiresIn { get; init; }
        public string? TokenType { get; init; }
        public string? Scope { get; init; }
    }
}

/// <summary>
/// Auth error codes matching the JS SDK.
/// </summary>
public enum CamundaAuthErrorCode
{
    TokenFetchFailed,
    TokenParseFailed,
    TokenExpired,
    OAuthConfigMissing,
    BasicCredentialsMissing,
}

/// <summary>
/// Authentication-specific exception.
/// </summary>
public sealed class CamundaAuthException : Exception
{
    public CamundaAuthErrorCode Code { get; }

    public CamundaAuthException(CamundaAuthErrorCode code, string message, Exception? inner = null)
        : base($"{code}: {message}", inner)
    {
        Code = code;
    }
}

// JSON deserialization helper using snake_case for OAuth responses
internal static class HttpContentExtensions
{
    private static readonly System.Text.Json.JsonSerializerOptions s_snakeCaseOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
    };

    public static async Task<T?> ReadFromJsonAsync<T>(this HttpContent content, CancellationToken ct = default)
    {
        var stream = await content.ReadAsStreamAsync(ct);
        return await System.Text.Json.JsonSerializer.DeserializeAsync<T>(stream, s_snakeCaseOptions, ct);
    }
}
