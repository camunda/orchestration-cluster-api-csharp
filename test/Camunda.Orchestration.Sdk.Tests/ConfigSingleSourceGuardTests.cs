namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Guard tests for the "single source of truth for configuration defaults" refactor
/// (mirrors the JS SDK's config-single-source guards / issue #145).
///
/// These lock in two invariants so that collapsing the currently triple-maintained
/// defaults (CamundaConfig initializers, ConfigurationHydrator.Defaults, and the
/// ParseInt fallbacks) into a single schema stays behaviour-preserving:
///
///   1. Hydration with no input yields the canonical default values.
///   2. Directly-constructed sub-config objects (which are used as-is by runtime
///      components and tests, e.g. `new BackpressureConfig()`) carry the SAME
///      defaults that hydration produces.
///
/// They are written to pass against the pre-refactor code (green), and must stay
/// green after the schema is introduced (green/green).
/// </summary>
public class ConfigSingleSourceGuardTests
{
    private static CamundaConfig HydrateEmpty() =>
        ConfigurationHydrator.Hydrate(env: new Dictionary<string, string?>());

    [Fact]
    public void HydrationYieldsCanonicalDefaults()
    {
        var c = HydrateEmpty();

        Assert.Equal("http://localhost:8080/v2", c.RestAddress);
        Assert.Equal("zeebe.camunda.io", c.TokenAudience);
        Assert.Equal("<default>", c.DefaultTenantId);
        Assert.Equal("error", c.LogLevel);

        Assert.Equal(3, c.HttpRetry.MaxAttempts);
        Assert.Equal(100, c.HttpRetry.BaseDelayMs);
        Assert.Equal(2000, c.HttpRetry.MaxDelayMs);

        Assert.True(c.Backpressure.Enabled);
        Assert.Equal("BALANCED", c.Backpressure.Profile);
        Assert.False(c.Backpressure.ObserveOnly);
        Assert.Equal(16, c.Backpressure.InitialMax);
        Assert.Equal(0.70, c.Backpressure.SoftFactor);
        Assert.Equal(0.50, c.Backpressure.SevereFactor);
        Assert.Equal(1000, c.Backpressure.RecoveryIntervalMs);
        Assert.Equal(1, c.Backpressure.RecoveryStep);
        Assert.Equal(2000, c.Backpressure.DecayQuietMs);
        Assert.Equal(1, c.Backpressure.Floor);
        Assert.Equal(3, c.Backpressure.SevereThreshold);

        Assert.Equal("https://login.cloud.camunda.io/oauth/token", c.OAuth.OAuthUrl);
        Assert.Equal("client_credentials", c.OAuth.GrantType);
        Assert.Equal(5000, c.OAuth.TimeoutMs);
        Assert.Equal(5, c.OAuth.Retry.Max);
        Assert.Equal(1000, c.OAuth.Retry.BaseDelayMs);

        Assert.Equal(AuthStrategy.None, c.Auth.Strategy);
        Assert.Equal(ValidationMode.None, c.Validation.Request);
        Assert.Equal(ValidationMode.None, c.Validation.Response);
        Assert.Equal("req:none,res:none", c.Validation.Raw);
        Assert.Equal(500, c.Eventual!.PollDefaultMs);
    }

    /// <summary>
    /// The sub-config classes are constructed directly (e.g. BackpressureManager and
    /// several tests do `new BackpressureConfig()`), so their initializer defaults are
    /// part of the contract. They must equal the values hydration produces — this is the
    /// invariant that lets both sites be driven from one schema without drift.
    /// Fields whose class default is intentionally a placeholder overwritten by the
    /// hydrator (RestAddress, TokenAudience, OAuthUrl) are excluded.
    /// </summary>
    [Fact]
    public void DirectlyConstructedSubConfigsMatchHydratedDefaults()
    {
        var c = HydrateEmpty();

        var httpRetry = new HttpRetryConfig();
        Assert.Equal(c.HttpRetry.MaxAttempts, httpRetry.MaxAttempts);
        Assert.Equal(c.HttpRetry.BaseDelayMs, httpRetry.BaseDelayMs);
        Assert.Equal(c.HttpRetry.MaxDelayMs, httpRetry.MaxDelayMs);

        var backpressure = new BackpressureConfig();
        Assert.Equal(c.Backpressure.Enabled, backpressure.Enabled);
        Assert.Equal(c.Backpressure.Profile, backpressure.Profile);
        Assert.Equal(c.Backpressure.ObserveOnly, backpressure.ObserveOnly);
        Assert.Equal(c.Backpressure.InitialMax, backpressure.InitialMax);
        Assert.Equal(c.Backpressure.SoftFactor, backpressure.SoftFactor);
        Assert.Equal(c.Backpressure.SevereFactor, backpressure.SevereFactor);
        Assert.Equal(c.Backpressure.RecoveryIntervalMs, backpressure.RecoveryIntervalMs);
        Assert.Equal(c.Backpressure.RecoveryStep, backpressure.RecoveryStep);
        Assert.Equal(c.Backpressure.DecayQuietMs, backpressure.DecayQuietMs);
        Assert.Equal(c.Backpressure.Floor, backpressure.Floor);
        Assert.Equal(c.Backpressure.SevereThreshold, backpressure.SevereThreshold);

        var oauth = new OAuthConfig();
        Assert.Equal(c.OAuth.GrantType, oauth.GrantType);
        Assert.Equal(c.OAuth.TimeoutMs, oauth.TimeoutMs);

        var oauthRetry = new OAuthRetryConfig();
        Assert.Equal(c.OAuth.Retry.Max, oauthRetry.Max);
        Assert.Equal(c.OAuth.Retry.BaseDelayMs, oauthRetry.BaseDelayMs);

        var eventual = new EventualConfig();
        Assert.Equal(c.Eventual!.PollDefaultMs, eventual.PollDefaultMs);
    }
}
