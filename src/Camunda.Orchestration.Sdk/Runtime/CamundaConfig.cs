namespace Camunda.Orchestration.Sdk;

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
    public string DefaultTenantId { get; init; } = ConfigSchema.StringDefault(ConfigKeys.DefaultTenantId);

    public HttpRetryConfig HttpRetry { get; init; } = new();
    public BackpressureConfig Backpressure { get; init; } = new();
    public OAuthConfig OAuth { get; init; } = new();
    public AuthConfig Auth { get; init; } = new();
    public ValidationConfig Validation { get; init; } = new();
    public string LogLevel { get; init; } = ConfigSchema.StringDefault(ConfigKeys.LogLevel);
    public EventualConfig? Eventual { get; init; }
    public WorkerDefaultsConfig? WorkerDefaults { get; init; }
    public TlsConfig? Tls { get; init; }
}

public sealed class HttpRetryConfig
{
    public int MaxAttempts { get; init; } = ConfigSchema.IntDefault(ConfigKeys.HttpRetryMaxAttempts);
    public int BaseDelayMs { get; init; } = ConfigSchema.IntDefault(ConfigKeys.HttpRetryBaseDelayMs);
    public int MaxDelayMs { get; init; } = ConfigSchema.IntDefault(ConfigKeys.HttpRetryMaxDelayMs);
}

public sealed class BackpressureConfig
{
    public bool Enabled { get; init; } = true;
    public string Profile { get; init; } = ConfigSchema.StringDefault(ConfigKeys.BackpressureProfile);
    public bool ObserveOnly { get; init; }
    public int InitialMax { get; init; } = ConfigSchema.IntDefault(ConfigKeys.BackpressureInitialMax);
    public double SoftFactor { get; init; } = ConfigSchema.IntDefault(ConfigKeys.BackpressureSoftFactor) / 100.0;
    public double SevereFactor { get; init; } = ConfigSchema.IntDefault(ConfigKeys.BackpressureSevereFactor) / 100.0;
    public int RecoveryIntervalMs { get; init; } = ConfigSchema.IntDefault(ConfigKeys.BackpressureRecoveryIntervalMs);
    public int RecoveryStep { get; init; } = ConfigSchema.IntDefault(ConfigKeys.BackpressureRecoveryStep);
    public int DecayQuietMs { get; init; } = ConfigSchema.IntDefault(ConfigKeys.BackpressureDecayQuietMs);
    public int Floor { get; init; } = ConfigSchema.IntDefault(ConfigKeys.BackpressureFloor);
    public int SevereThreshold { get; init; } = ConfigSchema.IntDefault(ConfigKeys.BackpressureSevereThreshold);
}

public sealed class OAuthConfig
{
    public string? ClientId { get; init; }
    public string? ClientSecret { get; init; }
    public string OAuthUrl { get; init; } = string.Empty;
    public string GrantType { get; init; } = ConfigSchema.StringDefault(ConfigKeys.OAuthGrantType);
    public string? Scope { get; init; }
    public int TimeoutMs { get; init; } = ConfigSchema.IntDefault(ConfigKeys.OAuthTimeoutMs);
    public OAuthRetryConfig Retry { get; init; } = new();
}

public sealed class OAuthRetryConfig
{
    public int Max { get; init; } = ConfigSchema.IntDefault(ConfigKeys.OAuthRetryMax);
    public int BaseDelayMs { get; init; } = ConfigSchema.IntDefault(ConfigKeys.OAuthRetryBaseDelayMs);
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
    public string Raw { get; init; } = ConfigSchema.StringDefault(ConfigKeys.Validation);
}

public sealed class EventualConfig
{
    public int PollDefaultMs { get; init; } = ConfigSchema.IntDefault(ConfigKeys.EventualPollDefaultMs);
}

public sealed class WorkerDefaultsConfig
{
    public long? JobTimeoutMs { get; init; }
    public int? MaxConcurrentJobs { get; init; }
    public long? PollTimeoutMs { get; init; }
    public string? WorkerName { get; init; }
    public double? StartupJitterMaxSeconds { get; init; }
}

/// <summary>
/// TLS / mTLS configuration for custom certificates.
/// </summary>
public sealed class TlsConfig
{
    /// <summary>Inline PEM client certificate (overrides CertPath).</summary>
    public string? Cert { get; init; }

    /// <summary>Path to PEM client certificate file.</summary>
    public string? CertPath { get; init; }

    /// <summary>Inline PEM client private key (overrides KeyPath).</summary>
    public string? Key { get; init; }

    /// <summary>Path to PEM client private key file.</summary>
    public string? KeyPath { get; init; }

    /// <summary>Inline PEM CA bundle (overrides CaPath).</summary>
    public string? Ca { get; init; }

    /// <summary>Path to PEM CA certificate bundle file.</summary>
    public string? CaPath { get; init; }

    /// <summary>Passphrase for an encrypted private key.</summary>
    public string? KeyPassphrase { get; init; }
}
