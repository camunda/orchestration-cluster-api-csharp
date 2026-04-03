using Microsoft.Extensions.Configuration;

namespace Camunda.Orchestration.Sdk.Tests;

public class AppSettingsConfigurationTests
{
    [Fact]
    public void BindsTopLevelKeysFromIConfiguration()
    {
        var configSection = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Camunda:RestAddress"] = "https://cluster.example.com",
                ["Camunda:DefaultTenantId"] = "my-tenant",
                ["Camunda:LogLevel"] = "debug",
            })
            .Build()
            .GetSection("Camunda");

        var config = ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?>(),
            configuration: configSection);

        Assert.Contains("cluster.example.com", config.RestAddress);
        Assert.Equal("my-tenant", config.DefaultTenantId);
        Assert.Equal("debug", config.LogLevel);
    }

    [Fact]
    public void BindsAuthSectionFromIConfiguration()
    {
        var configSection = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Camunda:Auth:Strategy"] = "BASIC",
                ["Camunda:Auth:BasicUsername"] = "alice",
                ["Camunda:Auth:BasicPassword"] = "secret",
            })
            .Build()
            .GetSection("Camunda");

        var config = ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?>(),
            configuration: configSection);

        Assert.Equal(AuthStrategy.Basic, config.Auth.Strategy);
        Assert.Equal("alice", config.Auth.Basic!.Username);
        Assert.Equal("secret", config.Auth.Basic.Password);
    }

    [Fact]
    public void BindsOAuthSectionFromIConfiguration()
    {
        var configSection = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Camunda:OAuth:Url"] = "https://auth.example.com/token",
                ["Camunda:OAuth:ClientId"] = "my-client",
                ["Camunda:OAuth:ClientSecret"] = "my-secret",
                ["Camunda:OAuth:TimeoutMs"] = "8000",
            })
            .Build()
            .GetSection("Camunda");

        var config = ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?>(),
            configuration: configSection);

        Assert.Equal(AuthStrategy.OAuth, config.Auth.Strategy);
        Assert.Equal("https://auth.example.com/token", config.OAuth.OAuthUrl);
        Assert.Equal("my-client", config.OAuth.ClientId);
        Assert.Equal(8000, config.OAuth.TimeoutMs);
    }

    [Fact]
    public void BindsBackpressureSectionFromIConfiguration()
    {
        var configSection = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Camunda:Backpressure:Profile"] = "AGGRESSIVE",
                ["Camunda:Backpressure:InitialMax"] = "32",
                ["Camunda:Backpressure:Floor"] = "2",
            })
            .Build()
            .GetSection("Camunda");

        var config = ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?>(),
            configuration: configSection);

        Assert.Equal("AGGRESSIVE", config.Backpressure.Profile);
        Assert.Equal(32, config.Backpressure.InitialMax);
        Assert.Equal(2, config.Backpressure.Floor);
    }

    [Fact]
    public void BindsHttpRetrySectionFromIConfiguration()
    {
        var configSection = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Camunda:HttpRetry:MaxAttempts"] = "5",
                ["Camunda:HttpRetry:BaseDelayMs"] = "200",
                ["Camunda:HttpRetry:MaxDelayMs"] = "5000",
            })
            .Build()
            .GetSection("Camunda");

        var config = ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?>(),
            configuration: configSection);

        Assert.Equal(5, config.HttpRetry.MaxAttempts);
        Assert.Equal(200, config.HttpRetry.BaseDelayMs);
        Assert.Equal(5000, config.HttpRetry.MaxDelayMs);
    }

    [Fact]
    public void ConfigOverridesTakesPrecedenceOverIConfiguration()
    {
        var configSection = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Camunda:RestAddress"] = "https://from-appsettings.example.com",
                ["Camunda:LogLevel"] = "info",
            })
            .Build()
            .GetSection("Camunda");

        var config = ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?>(),
            overrides: new Dictionary<string, string>
            {
                ["CAMUNDA_REST_ADDRESS"] = "https://from-override.example.com",
            },
            configuration: configSection);

        Assert.Contains("from-override.example.com", config.RestAddress);
        // LogLevel was not overridden, so IConfiguration value wins
        Assert.Equal("info", config.LogLevel);
    }

    [Fact]
    public void IConfigurationOverridesEnvironmentVariables()
    {
        var configSection = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Camunda:RestAddress"] = "https://from-appsettings.example.com",
            })
            .Build()
            .GetSection("Camunda");

        var config = ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?>
            {
                ["CAMUNDA_REST_ADDRESS"] = "https://from-env.example.com",
            },
            configuration: configSection);

        Assert.Contains("from-appsettings.example.com", config.RestAddress);
    }

    [Fact]
    public void ExtractFromConfigurationIsCaseInsensitive()
    {
        var configSection = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Camunda:restaddress"] = "https://lowercase.example.com",
            })
            .Build()
            .GetSection("Camunda");

        var extracted = ConfigurationHydrator.ExtractFromConfiguration(configSection);

        Assert.True(extracted.ContainsKey("CAMUNDA_REST_ADDRESS"));
        Assert.Equal("https://lowercase.example.com", extracted["CAMUNDA_REST_ADDRESS"]);
    }

    [Fact]
    public void IgnoresEmptyValuesInIConfiguration()
    {
        var configSection = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Camunda:RestAddress"] = "",
                ["Camunda:LogLevel"] = "warn",
            })
            .Build()
            .GetSection("Camunda");

        var extracted = ConfigurationHydrator.ExtractFromConfiguration(configSection);

        Assert.False(extracted.ContainsKey("CAMUNDA_REST_ADDRESS"));
        Assert.True(extracted.ContainsKey("CAMUNDA_SDK_LOG_LEVEL"));
    }
}
