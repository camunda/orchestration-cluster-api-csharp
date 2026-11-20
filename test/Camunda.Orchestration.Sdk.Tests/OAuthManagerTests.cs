using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

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
        _oauth = new OAuthManager(CreateConfig(), NullLogger.Instance, TimeProvider.System, CamundaRandomSource.Live);
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

        using var oauth = new OAuthManager(config, NullLogger.Instance, TimeProvider.System, CamundaRandomSource.Live);

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

        using var oauth = new OAuthManager(CreateConfig(), NullLogger.Instance, TimeProvider.System, CamundaRandomSource.Live);

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

        using var oauth = new OAuthManager(CreateConfig(), NullLogger.Instance, TimeProvider.System, CamundaRandomSource.Live);

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

        using var oauth = new OAuthManager(CreateConfig(retryMax: 3), NullLogger.Instance, TimeProvider.System, CamundaRandomSource.Live);

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

        using var oauth = new OAuthManager(CreateConfig(retryMax: 2), NullLogger.Instance, TimeProvider.System, CamundaRandomSource.Live);

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

    // ---- Expiry boundaries ----
    //
    // The two boundary computations in OAuthManager are the proactive-refresh lead window and
    // the skew buffer subtracted from the server-reported lifetime. Both are pinned to the exact
    // millisecond through the injected clock: a margin-based assertion passes whether the lead
    // window is 5000 ms, 0 ms or a minute, which is precisely the regression class these cases
    // exist to catch. The defect class is "a boundary that silently moves", so both sides of each
    // boundary are asserted, and the skew case covers both branches of max(30 s, 5 % of lifetime)
    // rather than one example lifetime.

    [Fact]
    public async Task GetToken_ReusesTheTokenJustOutsideTheRefreshLeadWindow()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, TokenJson("lead-window-token"));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://auth.mock/") };
        using var oauth = new OAuthManager(CreateConfig(), NullLogger.Instance, clock, CamundaRandomSource.Live);

        await oauth.GetTokenAsync(client);

        // One millisecond earlier than the lead window opens: the token is still presentable.
        oauth.Debug_SetTokenExpiry(clock.GetUtcNow().ToUnixTimeMilliseconds() + 5001);

        Assert.Equal("lead-window-token", await oauth.GetTokenAsync(client));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetToken_RefreshesAtTheRefreshLeadWindowEdge()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, TokenJson("stale-token"));
        handler.Enqueue(HttpStatusCode.OK, TokenJson("lead-window-refresh"));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://auth.mock/") };
        using var oauth = new OAuthManager(CreateConfig(), NullLogger.Instance, clock, CamundaRandomSource.Live);

        await oauth.GetTokenAsync(client);

        // Exactly at the lead window: still 5 s of nominal validity left, and the token must
        // already be replaced. Dropping the lead window would leave workers presenting a token
        // the server has expired whenever their clock runs slightly ahead of the issuer's.
        oauth.Debug_SetTokenExpiry(clock.GetUtcNow().ToUnixTimeMilliseconds() + 5000);

        Assert.Equal("lead-window-refresh", await oauth.GetTokenAsync(client));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    // expires_in 60 s: 5 % is 3 s, so the 30 s floor applies -> expiry at +30 s.
    [InlineData(60, 30_000)]
    // expires_in 3600 s: 5 % is 180 s, above the floor -> expiry at +3 420 s.
    [InlineData(3600, 3_420_000)]
    public async Task GetToken_StoresTheExpiryWithTheSkewBufferApplied(int expiresInSeconds, long expectedLifetimeMs)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, TokenJson("skew-token", expiresInSeconds));
        handler.Enqueue(HttpStatusCode.OK, TokenJson("skew-refresh", expiresInSeconds));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://auth.mock/") };
        using var oauth = new OAuthManager(CreateConfig(), NullLogger.Instance, clock, CamundaRandomSource.Live);

        Assert.Equal("skew-token", await oauth.GetTokenAsync(client));

        // The stored expiry is private, so it is observed through the boundary it drives: the
        // refresh must happen at (stored expiry - 5000 ms) to the millisecond. A mis-signed or
        // dropped skew buffer moves this edge and fails here.
        clock.Advance(TimeSpan.FromMilliseconds(expectedLifetimeMs - 5001));
        Assert.Equal("skew-token", await oauth.GetTokenAsync(client));
        Assert.Single(handler.Requests);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal("skew-refresh", await oauth.GetTokenAsync(client));
        Assert.Equal(2, handler.Requests.Count);
    }
}
