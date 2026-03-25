using System.Net;
using System.Text.Json;
using FluentAssertions;
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
        token1.Should().Be("first-token");

        // Second call should return cached token without another HTTP call
        var token2 = await _oauth.GetTokenAsync(_tokenClient);
        token2.Should().Be("first-token");
        _tokenHandler.Requests.Should().HaveCount(1, "token should be cached");
    }

    [Fact]
    public async Task GetToken_RefreshesExpiredToken()
    {
        _tokenHandler.Enqueue(HttpStatusCode.OK, TokenJson("initial-token"));
        _tokenHandler.Enqueue(HttpStatusCode.OK, TokenJson("refreshed-token"));

        var token1 = await _oauth.GetTokenAsync(_tokenClient);
        token1.Should().Be("initial-token");

        // Force expiry
        _oauth.Debug_SetTokenExpiry(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 10000);

        var token2 = await _oauth.GetTokenAsync(_tokenClient);
        token2.Should().Be("refreshed-token");
        _tokenHandler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task ForceRefresh_AlwaysFetchesNewToken()
    {
        _tokenHandler.Enqueue(HttpStatusCode.OK, TokenJson("token-1"));
        _tokenHandler.Enqueue(HttpStatusCode.OK, TokenJson("token-2"));

        var token1 = await _oauth.GetTokenAsync(_tokenClient);
        token1.Should().Be("token-1");

        var token2 = await _oauth.ForceRefreshAsync(_tokenClient);
        token2.Should().Be("token-2");
        _tokenHandler.Requests.Should().HaveCount(2);
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

        results.Should().AllBe("shared-token");
        _tokenHandler.Requests.Should().HaveCount(1, "singleflight should coalesce concurrent fetches");
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
        var ex = (await act.Should().ThrowAsync<CamundaAuthException>()).Which;
        ex.Code.Should().Be(CamundaAuthErrorCode.OAuthConfigMissing);
    }

    [Fact]
    public async Task GetToken_ThrowsOnEmptyTokenResponse()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "{}");
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://auth.mock/") };

        using var oauth = new OAuthManager(CreateConfig(), NullLogger.Instance);

        var act = async () => await oauth.GetTokenAsync(client);
        var ex = (await act.Should().ThrowAsync<CamundaAuthException>()).Which;
        ex.Code.Should().Be(CamundaAuthErrorCode.TokenParseFailed);
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
        var ex = (await act.Should().ThrowAsync<CamundaAuthException>()).Which;
        ex.Code.Should().Be(CamundaAuthErrorCode.TokenParseFailed);
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
        token.Should().Be("recovered-token");
        handler.Requests.Should().HaveCount(2);
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
        var ex = (await act.Should().ThrowAsync<CamundaAuthException>()).Which;
        ex.Code.Should().Be(CamundaAuthErrorCode.TokenFetchFailed);
    }

    [Fact]
    public async Task ClearCache_ForcesNextCallToFetch()
    {
        _tokenHandler.Enqueue(HttpStatusCode.OK, TokenJson("first"));
        _tokenHandler.Enqueue(HttpStatusCode.OK, TokenJson("second"));

        await _oauth.GetTokenAsync(_tokenClient);
        _oauth.ClearCache();
        var token = await _oauth.GetTokenAsync(_tokenClient);

        token.Should().Be("second");
        _tokenHandler.Requests.Should().HaveCount(2);
    }
}
