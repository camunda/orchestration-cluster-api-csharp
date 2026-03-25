using FluentAssertions;
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
        var act = () => bp.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public async Task BackpressureManager_DisposeAsync_DoesNotThrow()
    {
        var bp = new BackpressureManager(new BackpressureConfig(), NullLogger.Instance);
        var act = async () => await bp.DisposeAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void OAuthManager_Dispose_DoesNotThrow()
    {
        var config = new CamundaConfig
        {
            OAuth = new OAuthConfig { ClientId = "x", ClientSecret = "x" },
        };
        var oauth = new OAuthManager(config, NullLogger.Instance);
        var act = () => oauth.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public async Task OAuthManager_DisposeAsync_DoesNotThrow()
    {
        var config = new CamundaConfig
        {
            OAuth = new OAuthConfig { ClientId = "x", ClientSecret = "x" },
        };
        var oauth = new OAuthManager(config, NullLogger.Instance);
        var act = async () => await oauth.DisposeAsync();
        await act.Should().NotThrowAsync();
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

        var act = async () => await client.DisposeAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void CamundaClient_Dispose_ThenDisposeAsync_DoesNotThrow()
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
        var act = async () => await client.DisposeAsync();
        act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task BackpressureManager_CanBeUsedAfterDispose_ThrowsObjectDisposed()
    {
        var bp = new BackpressureManager(new BackpressureConfig(), NullLogger.Instance);
        bp.Dispose();

        // SemaphoreSlim.WaitAsync throws ObjectDisposedException after disposal
        var act = async () => await bp.AcquireAsync();
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }
}
