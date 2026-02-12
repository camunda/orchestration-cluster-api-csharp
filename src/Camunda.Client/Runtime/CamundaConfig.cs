namespace Camunda.Client.Runtime;

/// <summary>
/// Supported authentication strategies.
/// </summary>
public enum AuthStrategy
{
    None,
    OAuth,
    Basic
}

/// <summary>
/// Validation modes for request/response validation.
/// </summary>
public enum ValidationMode
{
    None,
    Warn,
    Strict,
    Fanatical
}

/// <summary>
/// Hydrated Camunda configuration. Immutable after construction.
/// </summary>
public sealed class CamundaConfig
{
    public string RestAddress { get; init; } = string.Empty;
    public string TokenAudience { get; init; } = string.Empty;
    public string DefaultTenantId { get; init; } = "<default>";

    public HttpRetryConfig HttpRetry { get; init; } = new();
    public BackpressureConfig Backpressure { get; init; } = new();
    public OAuthConfig OAuth { get; init; } = new();
    public AuthConfig Auth { get; init; } = new();
    public ValidationConfig Validation { get; init; } = new();
    public string LogLevel { get; init; } = "error";
    public EventualConfig? Eventual { get; init; }
}

public sealed class HttpRetryConfig
{
    public int MaxAttempts { get; init; } = 3;
    public int BaseDelayMs { get; init; } = 100;
    public int MaxDelayMs { get; init; } = 2000;
}

public sealed class BackpressureConfig
{
    public bool Enabled { get; init; } = true;
    public string Profile { get; init; } = "BALANCED";
    public bool ObserveOnly { get; init; }
    public int InitialMax { get; init; } = 16;
    public double SoftFactor { get; init; } = 0.70;
    public double SevereFactor { get; init; } = 0.50;
    public int RecoveryIntervalMs { get; init; } = 1000;
    public int RecoveryStep { get; init; } = 1;
    public int DecayQuietMs { get; init; } = 2000;
    public int Floor { get; init; } = 1;
    public int SevereThreshold { get; init; } = 3;
}

public sealed class OAuthConfig
{
    public string? ClientId { get; init; }
    public string? ClientSecret { get; init; }
    public string OAuthUrl { get; init; } = string.Empty;
    public string GrantType { get; init; } = "client_credentials";
    public string? Scope { get; init; }
    public int TimeoutMs { get; init; } = 5000;
    public OAuthRetryConfig Retry { get; init; } = new();
}

public sealed class OAuthRetryConfig
{
    public int Max { get; init; } = 5;
    public int BaseDelayMs { get; init; } = 1000;
}

public sealed class AuthConfig
{
    public AuthStrategy Strategy { get; init; } = AuthStrategy.None;
    public BasicAuthConfig? Basic { get; init; }
}

public sealed class BasicAuthConfig
{
    public string? Username { get; init; }
    public string? Password { get; init; }
}

public sealed class ValidationConfig
{
    public ValidationMode Request { get; init; } = ValidationMode.None;
    public ValidationMode Response { get; init; } = ValidationMode.None;
    public string Raw { get; init; } = "req:none,res:none";
}

public sealed class EventualConfig
{
    public int PollDefaultMs { get; init; } = 500;
}
