using Microsoft.Extensions.Logging.Abstractions;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Tests that runtime components properly implement IAsyncDisposable without errors.
/// </summary>
public class DisposalTests
{
    [Fact]
    public void BackpressureManager_Dispose_DoesNotThrow()
    {
        var bp = new BackpressureManager(new BackpressureConfig(), NullLogger.Instance);
        bp.Dispose();
    }

    [Fact]
    public async Task BackpressureManager_DisposeAsync_DoesNotThrow()
    {
        var bp = new BackpressureManager(new BackpressureConfig(), NullLogger.Instance);
        await bp.DisposeAsync();
    }

    [Fact]
    public void OAuthManager_Dispose_DoesNotThrow()
    {
        var config = new CamundaConfig
        {
            OAuth = new OAuthConfig { ClientId = "x", ClientSecret = "x" },
        };
        var oauth = new OAuthManager(config, NullLogger.Instance);
        oauth.Dispose();
    }

    [Fact]
    public async Task OAuthManager_DisposeAsync_DoesNotThrow()
    {
        var config = new CamundaConfig
        {
            OAuth = new OAuthConfig { ClientId = "x", ClientSecret = "x" },
        };
        var oauth = new OAuthManager(config, NullLogger.Instance);
        await oauth.DisposeAsync();
    }

    [Fact]
    public async Task CamundaClient_DisposeAsync_DoesNotThrow()
    {
        var client = new CamundaClient(new CamundaOptions
        {
            Config = new Dictionary<string, string>
            {
                ["CAMUNDA_REST_ADDRESS"] = "https://mock.local",
            },
            HttpMessageHandler = new MockHttpMessageHandler(),
        });

        await client.DisposeAsync();
    }

    [Fact]
    public async Task CamundaClient_Dispose_ThenDisposeAsync_DoesNotThrow()
    {
        var client = new CamundaClient(new CamundaOptions
        {
            Config = new Dictionary<string, string>
            {
                ["CAMUNDA_REST_ADDRESS"] = "https://mock.local",
            },
            HttpMessageHandler = new MockHttpMessageHandler(),
        });

        // Sync dispose then async dispose should not throw
        client.Dispose();
        await client.DisposeAsync();
    }

    [Fact]
    public async Task BackpressureManager_CanBeUsedAfterDispose_ThrowsObjectDisposed()
    {
        var bp = new BackpressureManager(new BackpressureConfig(), NullLogger.Instance);
        bp.Dispose();

        // SemaphoreSlim.WaitAsync throws ObjectDisposedException after disposal
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await bp.AcquireAsync());
    }
}
