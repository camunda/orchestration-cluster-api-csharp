namespace Camunda.Orchestration.Sdk.Tests;

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

        var ex = Assert.Throws<CamundaConfigurationException>(act);
        Assert.Contains(ex.Errors, e => e.Code == ConfigErrorCode.InvalidEnum);
    }

    [Fact]
    public void RejectsInvalidInteger()
    {
        var act = () => ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?> { ["CAMUNDA_OAUTH_TIMEOUT_MS"] = "5.0" });

        var ex = Assert.Throws<CamundaConfigurationException>(act);
        Assert.Contains(ex.Errors, e => e.Code == ConfigErrorCode.InvalidInteger);
    }

    [Fact]
    public void ParsesValidInteger()
    {
        var config = ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?> { ["CAMUNDA_OAUTH_TIMEOUT_MS"] = "6000" });

        Assert.Equal(6000, config.OAuth.TimeoutMs);
    }

    [Fact]
    public void DefaultsToNoneAuthStrategy()
    {
        var config = ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?> { });

        Assert.Equal(AuthStrategy.None, config.Auth.Strategy);
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

        Assert.Equal(AuthStrategy.OAuth, config.Auth.Strategy);
    }

    [Fact]
    public void NormalizesRestAddressToV2()
    {
        var config = ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?>
            {
                ["CAMUNDA_REST_ADDRESS"] = "https://zeebe.example.com",
            });

        Assert.EndsWith("/v2", config.RestAddress);
    }

    [Fact]
    public void DoesNotDoubleAppendV2()
    {
        var config = ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?>
            {
                ["CAMUNDA_REST_ADDRESS"] = "https://zeebe.example.com/v2",
            });

        Assert.Equal("https://zeebe.example.com/v2", config.RestAddress);
    }

    [Fact]
    public void ParsesValidationModeSingleWord()
    {
        var config = ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?>
            {
                ["CAMUNDA_SDK_VALIDATION"] = "strict",
            });

        Assert.Equal(ValidationMode.Strict, config.Validation.Request);
        Assert.Equal(ValidationMode.Strict, config.Validation.Response);
    }

    [Fact]
    public void ParsesValidationModeScoped()
    {
        var config = ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?>
            {
                ["CAMUNDA_SDK_VALIDATION"] = "req:warn,res:strict",
            });

        Assert.Equal(ValidationMode.Warn, config.Validation.Request);
        Assert.Equal(ValidationMode.Strict, config.Validation.Response);
    }

    [Fact]
    public void OverridesTakePrecedenceOverEnv()
    {
        var config = ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?> { ["CAMUNDA_SDK_LOG_LEVEL"] = "error" },
            overrides: new Dictionary<string, string> { ["CAMUNDA_SDK_LOG_LEVEL"] = "debug" });

        Assert.Equal("debug", config.LogLevel);
    }

    [Fact]
    public void AcceptsZeebeRestAddressAlias()
    {
        var config = ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?>
            {
                ["ZEEBE_REST_ADDRESS"] = "https://zeebe.local",
            });

        Assert.Contains("zeebe.local", config.RestAddress);
    }

    [Fact]
    public void RequiresMissingOAuthCredentials()
    {
        var act = () => ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?>
            {
                ["CAMUNDA_AUTH_STRATEGY"] = "OAUTH",
            });

        var ex = Assert.Throws<CamundaConfigurationException>(act);
        Assert.Contains(ex.Errors, e => e.Code == ConfigErrorCode.MissingRequired);
    }

    [Fact]
    public void BackpressureProfileDefaults()
    {
        var config = ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?> { });

        Assert.True(config.Backpressure.Enabled);
        Assert.Equal("BALANCED", config.Backpressure.Profile);
        Assert.False(config.Backpressure.ObserveOnly);
    }

    [Fact]
    public void LegacyProfileDisablesBackpressure()
    {
        var config = ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?>
            {
                ["CAMUNDA_SDK_BACKPRESSURE_PROFILE"] = "LEGACY",
            });

        Assert.True(config.Backpressure.ObserveOnly);
    }

    [Fact]
    public void RedactSecretMasksAllButLast4()
    {
        Assert.Equal("****efgh", ConfigurationHydrator.RedactSecret("abcdefgh"));
        Assert.Equal("**", ConfigurationHydrator.RedactSecret("ab"));
    }
}
