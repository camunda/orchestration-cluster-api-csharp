using Camunda.Orchestration.Sdk.Runtime;
using FluentAssertions;
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

        config.RestAddress.Should().Contain("cluster.example.com");
        config.DefaultTenantId.Should().Be("my-tenant");
        config.LogLevel.Should().Be("debug");
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

        config.Auth.Strategy.Should().Be(AuthStrategy.Basic);
        config.Auth.Basic!.Username.Should().Be("alice");
        config.Auth.Basic.Password.Should().Be("secret");
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

        config.Auth.Strategy.Should().Be(AuthStrategy.OAuth);
        config.OAuth.OAuthUrl.Should().Be("https://auth.example.com/token");
        config.OAuth.ClientId.Should().Be("my-client");
        config.OAuth.TimeoutMs.Should().Be(8000);
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

        config.Backpressure.Profile.Should().Be("AGGRESSIVE");
        config.Backpressure.InitialMax.Should().Be(32);
        config.Backpressure.Floor.Should().Be(2);
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

        config.HttpRetry.MaxAttempts.Should().Be(5);
        config.HttpRetry.BaseDelayMs.Should().Be(200);
        config.HttpRetry.MaxDelayMs.Should().Be(5000);
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

        config.RestAddress.Should().Contain("from-override.example.com");
        // LogLevel was not overridden, so IConfiguration value wins
        config.LogLevel.Should().Be("info");
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

        config.RestAddress.Should().Contain("from-appsettings.example.com");
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

        extracted.Should().ContainKey("CAMUNDA_REST_ADDRESS");
        extracted["CAMUNDA_REST_ADDRESS"].Should().Be("https://lowercase.example.com");
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

        extracted.Should().NotContainKey("CAMUNDA_REST_ADDRESS");
        extracted.Should().ContainKey("CAMUNDA_SDK_LOG_LEVEL");
    }
}
