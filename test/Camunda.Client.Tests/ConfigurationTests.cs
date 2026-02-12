using Camunda.Client.Runtime;
using FluentAssertions;

namespace Camunda.Client.Tests;

/// <summary>
/// Tests for configuration parsing, mirroring the JS SDK's configuration.test.ts.
/// </summary>
public class ConfigurationTests
{
    [Fact]
    public void RejectsInvalidEnumForAuthStrategy()
    {
        var act = () => ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?> { ["CAMUNDA_AUTH_STRATEGY"] = "invalid" });

        act.Should().Throw<CamundaConfigurationException>()
            .Which.Errors.Should().Contain(e => e.Code == ConfigErrorCode.InvalidEnum);
    }

    [Fact]
    public void RejectsInvalidInteger()
    {
        var act = () => ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?> { ["CAMUNDA_OAUTH_TIMEOUT_MS"] = "5.0" });

        act.Should().Throw<CamundaConfigurationException>()
            .Which.Errors.Should().Contain(e => e.Code == ConfigErrorCode.InvalidInteger);
    }

    [Fact]
    public void ParsesValidInteger()
    {
        var config = ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?> { ["CAMUNDA_OAUTH_TIMEOUT_MS"] = "6000" });

        config.OAuth.TimeoutMs.Should().Be(6000);
    }

    [Fact]
    public void DefaultsToNoneAuthStrategy()
    {
        var config = ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?> { });

        config.Auth.Strategy.Should().Be(AuthStrategy.None);
    }

    [Fact]
    public void InfersOAuthStrategyFromCredentials()
    {
        var config = ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?>
            {
                ["CAMUNDA_OAUTH_URL"] = "https://auth.example.com/token",
                ["CAMUNDA_CLIENT_ID"] = "my-client",
                ["CAMUNDA_CLIENT_SECRET"] = "my-secret",
            });

        config.Auth.Strategy.Should().Be(AuthStrategy.OAuth);
    }

    [Fact]
    public void NormalizesRestAddressToV2()
    {
        var config = ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?>
            {
                ["CAMUNDA_REST_ADDRESS"] = "https://zeebe.example.com",
            });

        config.RestAddress.Should().EndWith("/v2");
    }

    [Fact]
    public void DoesNotDoubleAppendV2()
    {
        var config = ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?>
            {
                ["CAMUNDA_REST_ADDRESS"] = "https://zeebe.example.com/v2",
            });

        config.RestAddress.Should().Be("https://zeebe.example.com/v2");
    }

    [Fact]
    public void ParsesValidationModeSingleWord()
    {
        var config = ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?>
            {
                ["CAMUNDA_SDK_VALIDATION"] = "strict",
            });

        config.Validation.Request.Should().Be(ValidationMode.Strict);
        config.Validation.Response.Should().Be(ValidationMode.Strict);
    }

    [Fact]
    public void ParsesValidationModeScoped()
    {
        var config = ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?>
            {
                ["CAMUNDA_SDK_VALIDATION"] = "req:warn,res:strict",
            });

        config.Validation.Request.Should().Be(ValidationMode.Warn);
        config.Validation.Response.Should().Be(ValidationMode.Strict);
    }

    [Fact]
    public void OverridesTakePrecedenceOverEnv()
    {
        var config = ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?> { ["CAMUNDA_SDK_LOG_LEVEL"] = "error" },
            overrides: new Dictionary<string, string> { ["CAMUNDA_SDK_LOG_LEVEL"] = "debug" });

        config.LogLevel.Should().Be("debug");
    }

    [Fact]
    public void AcceptsZeebeRestAddressAlias()
    {
        var config = ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?>
            {
                ["ZEEBE_REST_ADDRESS"] = "https://zeebe.local",
            });

        config.RestAddress.Should().Contain("zeebe.local");
    }

    [Fact]
    public void RequiresMissingOAuthCredentials()
    {
        var act = () => ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?>
            {
                ["CAMUNDA_AUTH_STRATEGY"] = "OAUTH",
            });

        act.Should().Throw<CamundaConfigurationException>()
            .Which.Errors.Should().Contain(e => e.Code == ConfigErrorCode.MissingRequired);
    }

    [Fact]
    public void BackpressureProfileDefaults()
    {
        var config = ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?> { });

        config.Backpressure.Enabled.Should().BeTrue();
        config.Backpressure.Profile.Should().Be("BALANCED");
        config.Backpressure.ObserveOnly.Should().BeFalse();
    }

    [Fact]
    public void LegacyProfileDisablesBackpressure()
    {
        var config = ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?>
            {
                ["CAMUNDA_SDK_BACKPRESSURE_PROFILE"] = "LEGACY",
            });

        config.Backpressure.ObserveOnly.Should().BeTrue();
    }

    [Fact]
    public void RedactSecretMasksAllButLast4()
    {
        ConfigurationHydrator.RedactSecret("abcdefgh").Should().Be("****efgh");
        ConfigurationHydrator.RedactSecret("ab").Should().Be("**");
    }
}
