using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Tests for OAuthManager: singleflight refresh, token caching, retry, error handling.
/// </summary>
public class OAuthManagerTests : IDisposable
{
    private readonly OAuthManager _oauth;
    private readonly MockHttpMessageHandler _tokenHandler;
    private readonly HttpClient _tokenClient;

    private static CamundaConfig CreateConfig(int retryMax = 1, int timeoutMs = 5000) => new()
    {
        OAuth = new OAuthConfig
        {
            ClientId = "test-id",
            ClientSecret = "test-secret",
            OAuthUrl = "https://auth.mock/token",
            Retry = new OAuthRetryConfig { Max = retryMax, BaseDelayMs = 10 },
            TimeoutMs = timeoutMs,
        },
        TokenAudience = "test-audience",
    };

    public OAuthManagerTests()
    {
        _tokenHandler = new MockHttpMessageHandler();
        _tokenClient = new HttpClient(_tokenHandler)
        {
            BaseAddress = new Uri("https://auth.mock/"),
        };
        _oauth = new OAuthManager(CreateConfig(), NullLogger.Instance);
    }

    public void Dispose()
    {
        _oauth.Dispose();
        _tokenClient.Dispose();
        GC.SuppressFinalize(this);
    }

    private static string TokenJson(string accessToken = "tok-123", int expiresIn = 3600)
        => JsonSerializer.Serialize(new { access_token = accessToken, expires_in = expiresIn });

    [Fact]
    public async Task GetToken_FetchesAndCachesToken()
    {
        _tokenHandler.Enqueue(HttpStatusCode.OK, TokenJson("first-token"));

        var token1 = await _oauth.GetTokenAsync(_tokenClient);
        Assert.Equal("first-token", token1);

        // Second call should return cached token without another HTTP call
        var token2 = await _oauth.GetTokenAsync(_tokenClient);
        Assert.Equal("first-token", token2);
        Assert.Single(_tokenHandler.Requests);
    }

    [Fact]
    public async Task GetToken_RefreshesExpiredToken()
    {
        _tokenHandler.Enqueue(HttpStatusCode.OK, TokenJson("initial-token"));
        _tokenHandler.Enqueue(HttpStatusCode.OK, TokenJson("refreshed-token"));

        var token1 = await _oauth.GetTokenAsync(_tokenClient);
        Assert.Equal("initial-token", token1);

        // Force expiry
        _oauth.Debug_SetTokenExpiry(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 10000);

        var token2 = await _oauth.GetTokenAsync(_tokenClient);
        Assert.Equal("refreshed-token", token2);
        Assert.Equal(2, _tokenHandler.Requests.Count);
    }

    [Fact]
    public async Task ForceRefresh_AlwaysFetchesNewToken()
    {
        _tokenHandler.Enqueue(HttpStatusCode.OK, TokenJson("token-1"));
        _tokenHandler.Enqueue(HttpStatusCode.OK, TokenJson("token-2"));

        var token1 = await _oauth.GetTokenAsync(_tokenClient);
        Assert.Equal("token-1", token1);

        var token2 = await _oauth.ForceRefreshAsync(_tokenClient);
        Assert.Equal("token-2", token2);
        Assert.Equal(2, _tokenHandler.Requests.Count);
    }

    [Fact]
    public async Task ConcurrentGetToken_OnlyFetchesOnce()
    {
        // Simulate slow token endpoint
        _tokenHandler.Enqueue(async _ =>
        {
            await Task.Delay(50);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(TokenJson("shared-token"),
                    System.Text.Encoding.UTF8, "application/json"),
            };
        });

        // Fire multiple concurrent requests
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => _oauth.GetTokenAsync(_tokenClient))
            .ToList();

        var results = await Task.WhenAll(tasks);

        Assert.All(results, item => Assert.Equal("shared-token", item));
        Assert.Single(_tokenHandler.Requests);
    }

    [Fact]
    public async Task GetToken_ThrowsOnMissingCredentials()
    {
        var config = new CamundaConfig
        {
            OAuth = new OAuthConfig
            {
                ClientId = null,
                ClientSecret = null,
                OAuthUrl = "https://auth.mock/token",
            },
            TokenAudience = "test",
        };

        using var oauth = new OAuthManager(config, NullLogger.Instance);

        var act = async () => await oauth.GetTokenAsync(_tokenClient);
        var ex = await Assert.ThrowsAsync<CamundaAuthException>(act);
        Assert.Equal(CamundaAuthErrorCode.OAuthConfigMissing, ex.Code);
    }

    [Fact]
    public async Task GetToken_ThrowsOnEmptyTokenResponse()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "{}");
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://auth.mock/") };

        using var oauth = new OAuthManager(CreateConfig(), NullLogger.Instance);

        var act = async () => await oauth.GetTokenAsync(client);
        var ex = await Assert.ThrowsAsync<CamundaAuthException>(act);
        Assert.Equal(CamundaAuthErrorCode.TokenParseFailed, ex.Code);
    }

    [Fact]
    public async Task GetToken_ThrowsOnMalformedAccessToken()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            JsonSerializer.Serialize(new { access_token = "", expires_in = 3600 }));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://auth.mock/") };

        using var oauth = new OAuthManager(CreateConfig(), NullLogger.Instance);

        var act = async () => await oauth.GetTokenAsync(client);
        var ex = await Assert.ThrowsAsync<CamundaAuthException>(act);
        Assert.Equal(CamundaAuthErrorCode.TokenParseFailed, ex.Code);
    }

    [Fact]
    public async Task GetToken_RetriesOnTransientFailureThenSucceeds()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.ServiceUnavailable, "server error");
        handler.Enqueue(HttpStatusCode.OK, TokenJson("recovered-token"));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://auth.mock/") };

        using var oauth = new OAuthManager(CreateConfig(retryMax: 3), NullLogger.Instance);

        var token = await oauth.GetTokenAsync(client);
        Assert.Equal("recovered-token", token);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetToken_ThrowsAfterAllRetriesExhausted()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.InternalServerError, "fail 1");
        handler.Enqueue(HttpStatusCode.InternalServerError, "fail 2");
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://auth.mock/") };

        using var oauth = new OAuthManager(CreateConfig(retryMax: 2), NullLogger.Instance);

        var act = async () => await oauth.GetTokenAsync(client);
        var ex = await Assert.ThrowsAsync<CamundaAuthException>(act);
        Assert.Equal(CamundaAuthErrorCode.TokenFetchFailed, ex.Code);
    }

    [Fact]
    public async Task ClearCache_ForcesNextCallToFetch()
    {
        _tokenHandler.Enqueue(HttpStatusCode.OK, TokenJson("first"));
        _tokenHandler.Enqueue(HttpStatusCode.OK, TokenJson("second"));

        await _oauth.GetTokenAsync(_tokenClient);
        _oauth.ClearCache();
        var token = await _oauth.GetTokenAsync(_tokenClient);

        Assert.Equal("second", token);
        Assert.Equal(2, _tokenHandler.Requests.Count);
    }
}
