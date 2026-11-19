using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Every request the SDK sends (REST operations and OAuth token fetches) must carry
/// <c>camunda-orchestration-sdk-csharp/&lt;version&gt;</c> as the first User-Agent product,
/// so gateway usage reports can attribute traffic to this SDK.
/// </summary>
public class UserAgentTests
{
    private static string ExpectedVersion =>
        typeof(CamundaClient).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion
            .Split('+')[0];

    private static string ExpectedSdkProduct => $"camunda-orchestration-sdk-csharp/{ExpectedVersion}";

    private static string UserAgentOf(HttpRequestMessage r) => r.Headers.UserAgent.ToString();

    private static string TokenJson() => JsonSerializer.Serialize(new { access_token = "tok", expires_in = 3600 });

    private static Dictionary<string, string> BaseConfig(string? userAgent = null)
    {
        var d = new Dictionary<string, string> { ["CAMUNDA_REST_ADDRESS"] = "https://mock.local" };
        if (userAgent != null)
            d["CAMUNDA_USER_AGENT"] = userAgent;
        return d;
    }

    [Fact]
    public void SdkUserAgent_UsesRealVersion()
    {
        Assert.Equal(ExpectedSdkProduct, SdkUserAgent.Product);
        Assert.DoesNotContain("+", SdkUserAgent.Product);
    }

    [Fact]
    public async Task RestRequest_SendsSdkUserAgentFirst()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "{}");
        using var client = new CamundaClient(new CamundaOptions { Config = BaseConfig(), HttpMessageHandler = handler });

        await client.GetTopologyAsync();

        Assert.Equal(ExpectedSdkProduct, UserAgentOf(Assert.Single(handler.Requests)));
    }

    [Fact]
    public async Task RestRequest_AppendsApplicationUserAgentAfterSdk()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "{}");
        using var client = new CamundaClient(new CamundaOptions
        {
            Config = BaseConfig("my-app/1.2"),
            HttpMessageHandler = handler,
        });

        await client.GetTopologyAsync();

        Assert.Equal($"{ExpectedSdkProduct} my-app/1.2", UserAgentOf(Assert.Single(handler.Requests)));
    }

    [Fact]
    public async Task RestRequest_CallerSuppliedHttpClient_SdkFirstThenCallerDefault()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "{}");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://mock.local/v2/") };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("custom-client/9");
        using var client = new CamundaClient(new CamundaOptions { Config = BaseConfig(), HttpClient = http });

        await client.GetTopologyAsync();

        Assert.Equal($"{ExpectedSdkProduct} custom-client/9", UserAgentOf(Assert.Single(handler.Requests)));
    }

    [Fact]
    public async Task TokenRequest_SendsSdkUserAgentFirst()
    {
        var tokenHandler = new MockHttpMessageHandler();
        tokenHandler.Enqueue(HttpStatusCode.OK, TokenJson());
        using var tokenClient = new HttpClient(tokenHandler);
        using var oauth = new OAuthManager(new CamundaConfig
        {
            OAuth = new OAuthConfig
            {
                ClientId = "id",
                ClientSecret = "secret",
                OAuthUrl = "https://auth.mock/token",
                Retry = new OAuthRetryConfig { Max = 1, BaseDelayMs = 1 },
            },
            UserAgent = "my-app/1.2",
        }, NullLogger.Instance, TimeProvider.System, CamundaRandomSource.Live);

        await oauth.GetTokenAsync(tokenClient);

        Assert.Equal($"{ExpectedSdkProduct} my-app/1.2", UserAgentOf(Assert.Single(tokenHandler.Requests)));
    }

    [Theory]
    // CR/LF: a header-injection attempt must never be added without validation.
    [InlineData("my-app/1.2\r\nX-Injected: yes")]
    [InlineData("my-app\nX-Injected: yes")]
    [InlineData("my-app\rX-Injected: yes")]
    // Other values the User-Agent grammar rejects are dropped too, not force-added.
    [InlineData("my app/1.2")]
    [InlineData("my-app/1.2;")]
    public void Apply_DropsUnparseableApplicationUserAgent(string appUserAgent)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://mock.local");

        SdkUserAgent.Apply(request, appUserAgent);

        Assert.Equal(ExpectedSdkProduct, UserAgentOf(request));
    }

    [Fact]
    public async Task RestRequest_DropsMalformedApplicationUserAgent()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "{}");
        using var client = new CamundaClient(new CamundaOptions
        {
            Config = BaseConfig("my-app/1.2\r\nX-Injected: yes"),
            HttpMessageHandler = handler,
        });

        await client.GetTopologyAsync();

        Assert.Equal(ExpectedSdkProduct, UserAgentOf(Assert.Single(handler.Requests)));
    }

    [Fact]
    public async Task TokenRequest_DropsMalformedApplicationUserAgent()
    {
        var tokenHandler = new MockHttpMessageHandler();
        tokenHandler.Enqueue(HttpStatusCode.OK, TokenJson());
        using var tokenClient = new HttpClient(tokenHandler);
        using var oauth = new OAuthManager(new CamundaConfig
        {
            OAuth = new OAuthConfig
            {
                ClientId = "id",
                ClientSecret = "secret",
                OAuthUrl = "https://auth.mock/token",
                Retry = new OAuthRetryConfig { Max = 1, BaseDelayMs = 1 },
            },
            UserAgent = "my-app/1.2\r\nX-Injected: yes",
        }, NullLogger.Instance, TimeProvider.System, CamundaRandomSource.Live);

        await oauth.GetTokenAsync(tokenClient);

        Assert.Equal(ExpectedSdkProduct, UserAgentOf(Assert.Single(tokenHandler.Requests)));
    }
}
