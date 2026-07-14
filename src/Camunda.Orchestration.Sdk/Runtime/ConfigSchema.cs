namespace Camunda.Orchestration.Sdk;

/// <summary>
/// Canonical <c>CAMUNDA_*</c> environment variable names. Referencing these constants
/// (rather than string literals) keeps key usages compile-checked across the schema,
/// the hydrator, and the config classes.
/// </summary>
internal static class ConfigKeys
{
    public const string RestAddress = "CAMUNDA_REST_ADDRESS";
    public const string TokenAudience = "CAMUNDA_TOKEN_AUDIENCE";
    public const string DefaultTenantId = "CAMUNDA_DEFAULT_TENANT_ID";
    public const string AuthStrategy = "CAMUNDA_AUTH_STRATEGY";
    public const string ClientId = "CAMUNDA_CLIENT_ID";
    public const string ClientSecret = "CAMUNDA_CLIENT_SECRET";
    public const string BasicAuthUsername = "CAMUNDA_BASIC_AUTH_USERNAME";
    public const string BasicAuthPassword = "CAMUNDA_BASIC_AUTH_PASSWORD";
    public const string OAuthUrl = "CAMUNDA_OAUTH_URL";
    public const string OAuthGrantType = "CAMUNDA_OAUTH_GRANT_TYPE";
    public const string OAuthScope = "CAMUNDA_OAUTH_SCOPE";
    public const string OAuthCacheDir = "CAMUNDA_OAUTH_CACHE_DIR";
    public const string OAuthTimeoutMs = "CAMUNDA_OAUTH_TIMEOUT_MS";
    public const string OAuthRetryMax = "CAMUNDA_OAUTH_RETRY_MAX";
    public const string OAuthRetryBaseDelayMs = "CAMUNDA_OAUTH_RETRY_BASE_DELAY_MS";
    public const string LogLevel = "CAMUNDA_SDK_LOG_LEVEL";
    public const string Validation = "CAMUNDA_SDK_VALIDATION";
    public const string HttpRetryMaxAttempts = "CAMUNDA_SDK_HTTP_RETRY_MAX_ATTEMPTS";
    public const string HttpRetryBaseDelayMs = "CAMUNDA_SDK_HTTP_RETRY_BASE_DELAY_MS";
    public const string HttpRetryMaxDelayMs = "CAMUNDA_SDK_HTTP_RETRY_MAX_DELAY_MS";
    public const string BackpressureProfile = "CAMUNDA_SDK_BACKPRESSURE_PROFILE";
    public const string BackpressureInitialMax = "CAMUNDA_SDK_BACKPRESSURE_INITIAL_MAX";
    public const string BackpressureSoftFactor = "CAMUNDA_SDK_BACKPRESSURE_SOFT_FACTOR";
    public const string BackpressureSevereFactor = "CAMUNDA_SDK_BACKPRESSURE_SEVERE_FACTOR";
    public const string BackpressureRecoveryIntervalMs = "CAMUNDA_SDK_BACKPRESSURE_RECOVERY_INTERVAL_MS";
    public const string BackpressureRecoveryStep = "CAMUNDA_SDK_BACKPRESSURE_RECOVERY_STEP";
    public const string BackpressureDecayQuietMs = "CAMUNDA_SDK_BACKPRESSURE_DECAY_QUIET_MS";
    public const string BackpressureFloor = "CAMUNDA_SDK_BACKPRESSURE_FLOOR";
    public const string BackpressureSevereThreshold = "CAMUNDA_SDK_BACKPRESSURE_SEVERE_THRESHOLD";
    public const string EventualPollDefaultMs = "CAMUNDA_SDK_EVENTUAL_POLL_DEFAULT_MS";
    public const string WorkerTimeout = "CAMUNDA_WORKER_TIMEOUT";
    public const string WorkerMaxConcurrentJobs = "CAMUNDA_WORKER_MAX_CONCURRENT_JOBS";
    public const string WorkerRequestTimeout = "CAMUNDA_WORKER_REQUEST_TIMEOUT";
    public const string WorkerName = "CAMUNDA_WORKER_NAME";
    public const string WorkerStartupJitterMaxSeconds = "CAMUNDA_WORKER_STARTUP_JITTER_MAX_SECONDS";
    public const string MtlsCert = "CAMUNDA_MTLS_CERT";
    public const string MtlsKey = "CAMUNDA_MTLS_KEY";
    public const string MtlsCa = "CAMUNDA_MTLS_CA";
    public const string MtlsCertPath = "CAMUNDA_MTLS_CERT_PATH";
    public const string MtlsKeyPath = "CAMUNDA_MTLS_KEY_PATH";
    public const string MtlsCaPath = "CAMUNDA_MTLS_CA_PATH";
    public const string MtlsKeyPassphrase = "CAMUNDA_MTLS_KEY_PASSPHRASE";
}

/// <summary>Primitive type of a configuration value (for parsing / validation).</summary>
internal enum ConfigValueType
{
    String,
    Int,
    Bool,
    Enum,
}

/// <summary>
/// Describes a single configuration key. The schema is the single source of truth for
/// each key's default value, canonical env-var name, legacy aliases, and
/// <c>IConfiguration</c> binding paths.
/// </summary>
internal sealed class ConfigKeyDescriptor
{
    /// <summary>Canonical <c>CAMUNDA_*</c> environment variable name.</summary>
    public required string EnvVar { get; init; }

    public ConfigValueType Type { get; init; } = ConfigValueType.String;

    /// <summary>Default value in canonical string form; <c>null</c> when the key has no default.</summary>
    public string? Default { get; init; }

    /// <summary>Allowed values for <see cref="ConfigValueType.Enum"/> keys.</summary>
    public string[]? Choices { get; init; }

    /// <summary>Legacy env-var names accepted as fallbacks when the canonical key is unset.</summary>
    public string[] Aliases { get; init; } = [];

    /// <summary>PascalCase <c>IConfiguration</c> binding paths that map to this key.</summary>
    public string[] ConfigPaths { get; init; } = [];

    /// <summary>Whether the value must be redacted in logs / diagnostics.</summary>
    public bool Secret { get; init; }

    public string? Doc { get; init; }
}

/// <summary>
/// The canonical configuration schema. Every default value, alias, and
/// <c>IConfiguration</c> binding path is declared exactly once here; the hydrator and
/// the <see cref="CamundaConfig"/> classes derive from it rather than restating literals.
/// Mirrors the JS SDK's <c>SCHEMA</c> (issue #145).
/// </summary>
internal static class ConfigSchema
{
    public static readonly IReadOnlyList<ConfigKeyDescriptor> All =
    [
        new() { EnvVar = ConfigKeys.RestAddress, Default = "http://localhost:8080/v2", Aliases = ["ZEEBE_REST_ADDRESS"], ConfigPaths = ["RestAddress"], Doc = "Base REST endpoint address." },
        new() { EnvVar = ConfigKeys.TokenAudience, Default = "zeebe.camunda.io", ConfigPaths = ["TokenAudience"], Doc = "Token audience for OAuth flows." },
        new() { EnvVar = ConfigKeys.DefaultTenantId, Default = "<default>", ConfigPaths = ["DefaultTenantId"], Doc = "Default tenant id applied when none is provided." },
        new() { EnvVar = ConfigKeys.AuthStrategy, Type = ConfigValueType.Enum, Choices = ["NONE", "OAUTH", "BASIC"], Default = "NONE", ConfigPaths = ["Auth:Strategy"], Doc = "Authentication strategy." },
        new() { EnvVar = ConfigKeys.ClientId, ConfigPaths = ["Auth:ClientId", "OAuth:ClientId"], Doc = "OAuth client id (required when strategy is OAUTH)." },
        new() { EnvVar = ConfigKeys.ClientSecret, Secret = true, ConfigPaths = ["Auth:ClientSecret", "OAuth:ClientSecret"], Doc = "OAuth client secret (required when strategy is OAUTH)." },
        new() { EnvVar = ConfigKeys.BasicAuthUsername, ConfigPaths = ["Auth:BasicUsername"], Doc = "Basic auth username (required when strategy is BASIC)." },
        new() { EnvVar = ConfigKeys.BasicAuthPassword, Secret = true, ConfigPaths = ["Auth:BasicPassword"], Doc = "Basic auth password (required when strategy is BASIC)." },
        new() { EnvVar = ConfigKeys.OAuthUrl, Default = "https://login.cloud.camunda.io/oauth/token", ConfigPaths = ["OAuth:Url"], Doc = "OAuth token URL." },
        new() { EnvVar = ConfigKeys.OAuthGrantType, Default = "client_credentials", ConfigPaths = ["OAuth:GrantType"], Doc = "OAuth grant type." },
        new() { EnvVar = ConfigKeys.OAuthScope, ConfigPaths = ["OAuth:Scope"], Doc = "Optional OAuth scope." },
        new() { EnvVar = ConfigKeys.OAuthCacheDir, Doc = "Directory for disk caching OAuth tokens." },
        new() { EnvVar = ConfigKeys.OAuthTimeoutMs, Type = ConfigValueType.Int, Default = "5000", ConfigPaths = ["OAuth:TimeoutMs"], Doc = "Timeout in ms for OAuth token fetch." },
        new() { EnvVar = ConfigKeys.OAuthRetryMax, Type = ConfigValueType.Int, Default = "5", ConfigPaths = ["OAuth:RetryMax"], Doc = "Maximum OAuth token fetch attempts." },
        new() { EnvVar = ConfigKeys.OAuthRetryBaseDelayMs, Type = ConfigValueType.Int, Default = "1000", ConfigPaths = ["OAuth:RetryBaseDelayMs"], Doc = "Base delay (ms) for OAuth retry backoff." },
        new() { EnvVar = ConfigKeys.LogLevel, Default = "error", ConfigPaths = ["LogLevel"], Doc = "SDK log level." },
        new() { EnvVar = ConfigKeys.Validation, Default = "req:none,res:none", ConfigPaths = ["Validation"], Doc = "Validation mini-language controlling req/res modes." },
        new() { EnvVar = ConfigKeys.HttpRetryMaxAttempts, Type = ConfigValueType.Int, Default = "3", ConfigPaths = ["HttpRetry:MaxAttempts"], Doc = "Maximum total HTTP attempts for transient failures." },
        new() { EnvVar = ConfigKeys.HttpRetryBaseDelayMs, Type = ConfigValueType.Int, Default = "100", ConfigPaths = ["HttpRetry:BaseDelayMs"], Doc = "Base delay (ms) for HTTP retry backoff." },
        new() { EnvVar = ConfigKeys.HttpRetryMaxDelayMs, Type = ConfigValueType.Int, Default = "2000", ConfigPaths = ["HttpRetry:MaxDelayMs"], Doc = "Maximum delay cap (ms) for HTTP retry backoff." },
        new() { EnvVar = ConfigKeys.BackpressureProfile, Type = ConfigValueType.Enum, Choices = ["BALANCED", "CONSERVATIVE", "AGGRESSIVE", "LEGACY"], Default = "BALANCED", ConfigPaths = ["Backpressure:Profile"], Doc = "Preset profile for backpressure tuning." },
        new() { EnvVar = ConfigKeys.BackpressureInitialMax, Type = ConfigValueType.Int, Default = "16", ConfigPaths = ["Backpressure:InitialMax"], Doc = "Initial bootstrap concurrency cap." },
        new() { EnvVar = ConfigKeys.BackpressureSoftFactor, Type = ConfigValueType.Int, Default = "70", ConfigPaths = ["Backpressure:SoftFactor"], Doc = "Percentage multiplier on soft backpressure (e.g. 70 => 0.7x)." },
        new() { EnvVar = ConfigKeys.BackpressureSevereFactor, Type = ConfigValueType.Int, Default = "50", ConfigPaths = ["Backpressure:SevereFactor"], Doc = "Percentage multiplier on severe backpressure (e.g. 50 => 0.5x)." },
        new() { EnvVar = ConfigKeys.BackpressureRecoveryIntervalMs, Type = ConfigValueType.Int, Default = "1000", ConfigPaths = ["Backpressure:RecoveryIntervalMs"], Doc = "Interval (ms) between passive recovery checks." },
        new() { EnvVar = ConfigKeys.BackpressureRecoveryStep, Type = ConfigValueType.Int, Default = "1", ConfigPaths = ["Backpressure:RecoveryStep"], Doc = "Permits regained per recovery interval." },
        new() { EnvVar = ConfigKeys.BackpressureDecayQuietMs, Type = ConfigValueType.Int, Default = "2000", ConfigPaths = ["Backpressure:DecayQuietMs"], Doc = "Quiet period (ms) required to downgrade severity." },
        new() { EnvVar = ConfigKeys.BackpressureFloor, Type = ConfigValueType.Int, Default = "1", ConfigPaths = ["Backpressure:Floor"], Doc = "Minimum floor concurrency when degraded." },
        new() { EnvVar = ConfigKeys.BackpressureSevereThreshold, Type = ConfigValueType.Int, Default = "3", ConfigPaths = ["Backpressure:SevereThreshold"], Doc = "Consecutive events required to enter severe state." },
        new() { EnvVar = ConfigKeys.EventualPollDefaultMs, Type = ConfigValueType.Int, Default = "500", ConfigPaths = ["Eventual:PollDefaultMs"], Doc = "Default poll interval (ms) for eventually consistent endpoints." },
        new() { EnvVar = ConfigKeys.WorkerTimeout, Type = ConfigValueType.Int, ConfigPaths = ["Worker:Timeout"], Doc = "Default job timeout (ms) for all workers." },
        new() { EnvVar = ConfigKeys.WorkerMaxConcurrentJobs, Type = ConfigValueType.Int, ConfigPaths = ["Worker:MaxConcurrentJobs"], Doc = "Default max parallel jobs for all workers." },
        new() { EnvVar = ConfigKeys.WorkerRequestTimeout, Type = ConfigValueType.Int, ConfigPaths = ["Worker:RequestTimeout"], Doc = "Default long-poll timeout (ms) for all workers." },
        new() { EnvVar = ConfigKeys.WorkerName, ConfigPaths = ["Worker:Name"], Doc = "Default worker name for all workers." },
        new() { EnvVar = ConfigKeys.WorkerStartupJitterMaxSeconds, Type = ConfigValueType.Int, ConfigPaths = ["Worker:StartupJitterMaxSeconds"], Doc = "Default startup jitter (seconds) for all workers." },
        new() { EnvVar = ConfigKeys.MtlsCert, Doc = "Inline PEM client certificate." },
        new() { EnvVar = ConfigKeys.MtlsKey, Secret = true, Doc = "Inline PEM client private key." },
        new() { EnvVar = ConfigKeys.MtlsCa, Doc = "Inline PEM CA bundle." },
        new() { EnvVar = ConfigKeys.MtlsCertPath, Doc = "Path to client certificate (PEM)." },
        new() { EnvVar = ConfigKeys.MtlsKeyPath, Doc = "Path to client private key (PEM)." },
        new() { EnvVar = ConfigKeys.MtlsCaPath, Doc = "Path to CA certificate bundle (PEM)." },
        new() { EnvVar = ConfigKeys.MtlsKeyPassphrase, Secret = true, Doc = "Optional passphrase for encrypted private key." },
    ];

    private static readonly Dictionary<string, ConfigKeyDescriptor> ByEnvVar =
        All.ToDictionary(d => d.EnvVar, StringComparer.Ordinal);

    /// <summary>Env-var name → canonical default string, for every key that declares a default.</summary>
    public static readonly IReadOnlyDictionary<string, string> Defaults =
        All.Where(d => d.Default != null).ToDictionary(d => d.EnvVar, d => d.Default!, StringComparer.Ordinal);

    /// <summary><c>IConfiguration</c> binding path → canonical env-var name (case-insensitive).</summary>
    public static readonly IReadOnlyDictionary<string, string> ConfigKeyMap =
        All.SelectMany(d => d.ConfigPaths.Select(p => (Path: p, d.EnvVar)))
           .ToDictionary(x => x.Path, x => x.EnvVar, StringComparer.OrdinalIgnoreCase);

    /// <summary>Env-var names whose values must be redacted.</summary>
    public static readonly IReadOnlySet<string> SecretKeys =
        All.Where(d => d.Secret).Select(d => d.EnvVar).ToHashSet(StringComparer.Ordinal);

    /// <summary>All canonical env-var names.</summary>
    public static readonly IReadOnlyList<string> AllEnvVars = All.Select(d => d.EnvVar).ToList();

    /// <summary>(legacy alias → canonical env-var) pairs declared in the schema.</summary>
    public static readonly IReadOnlyList<(string Alias, string EnvVar)> Aliases =
        All.SelectMany(d => d.Aliases.Select(a => (Alias: a, d.EnvVar))).ToList();

    private static string RequireDefault(string envVar)
    {
        if (ByEnvVar.TryGetValue(envVar, out var d) && d.Default != null)
            return d.Default;
        throw new InvalidOperationException($"No schema default declared for '{envVar}'.");
    }

    /// <summary>Schema default for a string key.</summary>
    public static string StringDefault(string envVar) => RequireDefault(envVar);

    /// <summary>Schema default for an integer key.</summary>
    public static int IntDefault(string envVar) =>
        int.Parse(RequireDefault(envVar), System.Globalization.CultureInfo.InvariantCulture);
}
