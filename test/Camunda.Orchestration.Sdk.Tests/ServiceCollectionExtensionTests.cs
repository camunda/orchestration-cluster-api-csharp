using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Camunda.Orchestration.Sdk.Tests;

public class ServiceCollectionExtensionTests
{
    [Fact]
    public void AddCamundaClient_ZeroConfig_RegistersSingleton()
    {
        var services = new ServiceCollection();
        services.AddCamundaClient();

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<CamundaClient>();

        Assert.NotNull(client);
        Assert.Equal(AuthStrategy.None, client.Config.Auth.Strategy);
    }

    [Fact]
    public void AddCamundaClient_WithConfigurationSection_BindsValues()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Camunda:RestAddress"] = "https://cluster.example.com",
                ["Camunda:Backpressure:Profile"] = "CONSERVATIVE",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddCamundaClient(configuration.GetSection("Camunda"));

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<CamundaClient>();

        Assert.Contains("cluster.example.com", client.Config.RestAddress);
        Assert.Equal("CONSERVATIVE", client.Config.Backpressure.Profile);
    }

    [Fact]
    public void AddCamundaClient_WithCallback_AppliesOptions()
    {
        var services = new ServiceCollection();
        services.AddCamundaClient(options =>
        {
            options.Config = new Dictionary<string, string>
            {
                ["CAMUNDA_REST_ADDRESS"] = "https://callback.example.com",
            };
        });

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<CamundaClient>();

        Assert.Contains("callback.example.com", client.Config.RestAddress);
    }

    [Fact]
    public void AddCamundaClient_ResolvesSameInstance()
    {
        var services = new ServiceCollection();
        services.AddCamundaClient();

        var provider = services.BuildServiceProvider();
        var a = provider.GetRequiredService<CamundaClient>();
        var b = provider.GetRequiredService<CamundaClient>();

        Assert.Same(a, b);
    }

    [Fact]
    public void AddCamundaClient_PicksUpILoggerFactory()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug));
        services.AddCamundaClient();

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<CamundaClient>();

        // If ILoggerFactory was injected, the client should construct without error
        Assert.NotNull(client);
    }
}
