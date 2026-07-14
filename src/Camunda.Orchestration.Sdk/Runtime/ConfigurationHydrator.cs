using Microsoft.Extensions.Configuration;

namespace Camunda.Orchestration.Sdk;

/// <summary>
/// Configuration hydration errors.
/// </summary>
public enum ConfigErrorCode
{
    MissingRequired,
    InvalidEnum,
    InvalidBoolean,
    InvalidInteger,
    InvalidValidationSyntax
}

public sealed class ConfigErrorDetail
{
    public string? Key { get; init; }
    public ConfigErrorCode Code { get; init; }
    public required string Message { get; init; }
}

/// <summary>
/// Thrown when configuration hydration encounters validation errors.
/// </summary>
public sealed class CamundaConfigurationException : Exception
{
    public IReadOnlyList<ConfigErrorDetail> Errors { get; }

    public CamundaConfigurationException(IReadOnlyList<ConfigErrorDetail> errors)
        : base(string.Join(Environment.NewLine,
            errors.Select(e => $"{e.Code}{(e.Key != null ? $"({e.Key})" : "")}: {e.Message}")))
    {
        Errors = errors;
    }
}

/// <summary>
/// Hydrates a <see cref="CamundaConfig"/> from environment variables and overrides.
/// Mirrors the JS SDK's hydrateConfig function.
/// </summary>
public static class ConfigurationHydrator
{
    // Defaults are derived from the single-source ConfigSchema.
    private static readonly IReadOnlyDictionary<string, string> Defaults = ConfigSchema.Defaults;

    /// <summary>
    /// Hydrate configuration from environment and optional overrides.
    /// </summary>
    public static CamundaConfig Hydrate(
        Dictionary<string, string?>? env = null,
        Dictionary<string, string>? overrides = null,
        IConfiguration? configuration = null)
    {
        var errors = new List<ConfigErrorDetail>();
        var rawMap = new Dictionary<string, string>();

        // Build merged input: env → override (override wins)
        var input = new Dictionary<string, string>();
        if (env != null)
        {
            foreach (var (k, v) in env)
            {
                var trimmed = v?.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    input[k] = trimmed;
            }
        }
        else
        {
            // Read every schema key (and its legacy aliases) from the actual environment.
            foreach (var key in ConfigSchema.AllEnvVars)
            {
                var trimmed = Environment.GetEnvironmentVariable(key)?.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    input[key] = trimmed;
            }
            foreach (var (alias, _) in ConfigSchema.Aliases)
            {
                var trimmed = Environment.GetEnvironmentVariable(alias)?.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    input[alias] = trimmed;
            }
        }

        if (overrides != null)
        {
            foreach (var (k, v) in overrides)
                input[k] = v;
        }

        // IConfiguration section (appsettings.json etc.) — between env and overrides in precedence
        if (configuration != null)
        {
            var configValues = ExtractFromConfiguration((IConfiguration)configuration);
            foreach (var (k, v) in configValues)
            {
                // Configuration wins over env vars but loses to explicit overrides
                if (overrides == null || !overrides.ContainsKey(k))
                    input[k] = v;
            }
        }

        // Aliases: accept schema-declared legacy env-var names when the canonical key is unset.
        foreach (var (alias, envVar) in ConfigSchema.Aliases)
        {
            if (!input.ContainsKey(envVar) && input.TryGetValue(alias, out var aliasVal))
                input[envVar] = aliasVal;
        }

        // Fill defaults for missing keys
        foreach (var (k, def) in Defaults)
        {
            if (!input.ContainsKey(k) && def.Length > 0)
                rawMap[k] = def;
            else if (input.TryGetValue(k, out var v))
                rawMap[k] = v;
        }

        // Copy extra keys
        foreach (var k in input.Keys)
        {
            if (!rawMap.ContainsKey(k))
                rawMap[k] = input[k];
        }

        // Auth strategy inference
        var userSetStrategy = input.ContainsKey(ConfigKeys.AuthStrategy);
        if (!userSetStrategy &&
            rawMap.GetValueOrDefault(ConfigKeys.AuthStrategy) == "NONE" &&
            !string.IsNullOrEmpty(rawMap.GetValueOrDefault(ConfigKeys.OAuthUrl)) &&
            !string.IsNullOrEmpty(rawMap.GetValueOrDefault(ConfigKeys.ClientId)) &&
            !string.IsNullOrEmpty(rawMap.GetValueOrDefault(ConfigKeys.ClientSecret)))
        {
            rawMap[ConfigKeys.AuthStrategy] = "OAUTH";
        }

        // Validate auth strategy
        var authStrategyRaw = rawMap.GetValueOrDefault(ConfigKeys.AuthStrategy, "NONE")!.Trim().ToUpperInvariant();
        if (!Enum.TryParse<AuthStrategy>(authStrategyRaw, true, out var authStrategy))
        {
            errors.Add(new ConfigErrorDetail
            {
                Code = ConfigErrorCode.InvalidEnum,
                Key = ConfigKeys.AuthStrategy,
                Message = $"Invalid auth strategy '{authStrategyRaw}'. Expected NONE|OAUTH|BASIC."
            });
            authStrategy = AuthStrategy.None;
        }

        // Validate conditional requirements
        if (authStrategy == AuthStrategy.OAuth)
        {
            var missing = new List<string>();
            if (string.IsNullOrEmpty(rawMap.GetValueOrDefault(ConfigKeys.ClientId)))
                missing.Add(ConfigKeys.ClientId);
            if (string.IsNullOrEmpty(rawMap.GetValueOrDefault(ConfigKeys.ClientSecret)))
                missing.Add(ConfigKeys.ClientSecret);
            if (string.IsNullOrEmpty(rawMap.GetValueOrDefault(ConfigKeys.OAuthUrl)))
                missing.Add(ConfigKeys.OAuthUrl);
            if (missing.Count > 0)
            {
                errors.Add(new ConfigErrorDetail
                {
                    Code = ConfigErrorCode.MissingRequired,
                    Message = $"Missing required configuration for OAUTH: {string.Join(", ", missing)}"
                });
            }
        }
        else if (authStrategy == AuthStrategy.Basic)
        {
            var missing = new List<string>();
            if (string.IsNullOrEmpty(rawMap.GetValueOrDefault(ConfigKeys.BasicAuthUsername)))
                missing.Add(ConfigKeys.BasicAuthUsername);
            if (string.IsNullOrEmpty(rawMap.GetValueOrDefault(ConfigKeys.BasicAuthPassword)))
                missing.Add(ConfigKeys.BasicAuthPassword);
            if (missing.Count > 0)
            {
                errors.Add(new ConfigErrorDetail
                {
                    Code = ConfigErrorCode.MissingRequired,
                    Message = $"Missing required configuration for BASIC: {string.Join(", ", missing)}"
                });
            }
        }

        // TLS / mTLS validation.
        // CA-only is valid (trust a self-signed server cert without client identity).
        // Client cert and key must come as a pair.
        // A passphrase without a client key is invalid.
        var mtlsCertProvided = !string.IsNullOrEmpty(rawMap.GetValueOrDefault(ConfigKeys.MtlsCert))
                            || !string.IsNullOrEmpty(rawMap.GetValueOrDefault(ConfigKeys.MtlsCertPath));
        var mtlsKeyProvided = !string.IsNullOrEmpty(rawMap.GetValueOrDefault(ConfigKeys.MtlsKey))
                           || !string.IsNullOrEmpty(rawMap.GetValueOrDefault(ConfigKeys.MtlsKeyPath));
        if (mtlsCertProvided != mtlsKeyProvided)
        {
            errors.Add(new ConfigErrorDetail
            {
                Code = ConfigErrorCode.MissingRequired,
                Message = "Incomplete mTLS configuration: both certificate "
                    + "(CAMUNDA_MTLS_CERT or CAMUNDA_MTLS_CERT_PATH) and key "
                    + "(CAMUNDA_MTLS_KEY or CAMUNDA_MTLS_KEY_PATH) must be provided together."
            });
        }
        if (!string.IsNullOrEmpty(rawMap.GetValueOrDefault(ConfigKeys.MtlsKeyPassphrase)) && !mtlsKeyProvided)
        {
            errors.Add(new ConfigErrorDetail
            {
                Code = ConfigErrorCode.MissingRequired,
                Message = "CAMUNDA_MTLS_KEY_PASSPHRASE is set but no client key was provided."
            });
        }
        var hasTls = mtlsCertProvided || mtlsKeyProvided
            || !string.IsNullOrEmpty(rawMap.GetValueOrDefault(ConfigKeys.MtlsCa))
            || !string.IsNullOrEmpty(rawMap.GetValueOrDefault(ConfigKeys.MtlsCaPath));

        // Validate integers. Unsigned + invariant to match the contract in the error
        // message: rejects signs, digit separators, and decimals. Leading/trailing
        // whitespace is trimmed first (env values are already trimmed at ingest).
        int ParseInt(string key, int fallback)
        {
            var raw = rawMap.GetValueOrDefault(key, fallback.ToString(System.Globalization.CultureInfo.InvariantCulture))!;
            if (int.TryParse(raw.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var val))
                return val;
            errors.Add(new ConfigErrorDetail
            {
                Code = ConfigErrorCode.InvalidInteger,
                Key = key,
                Message = $"Invalid integer '{raw}'. Only unsigned base-10 integers allowed."
            });
            return fallback;
        }

        // Signed integer parse (invariant) for keys documented to accept negatives,
        // e.g. CAMUNDA_WORKER_REQUEST_TIMEOUT (negative = long polling disabled).
        int ParseSignedInt(string key, int fallback)
        {
            var raw = rawMap.GetValueOrDefault(key, fallback.ToString(System.Globalization.CultureInfo.InvariantCulture))!;
            if (int.TryParse(raw.Trim(), System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var val))
                return val;
            errors.Add(new ConfigErrorDetail
            {
                Code = ConfigErrorCode.InvalidInteger,
                Key = key,
                Message = $"Invalid integer '{raw}'. Only base-10 integers (optionally negative) allowed."
            });
            return fallback;
        }

        // Parse an integer key using its schema default as the fallback (no literal duplication).
        int ParseSchemaInt(string key) => ParseInt(key, ConfigSchema.IntDefault(key));

        // Parse validation
        var validation = ParseValidation(rawMap.GetValueOrDefault(ConfigKeys.Validation, ConfigSchema.StringDefault(ConfigKeys.Validation))!, errors);

        // Eagerly parse all integer config values so errors are collected before the check
        var retryMaxAttempts = ParseSchemaInt(ConfigKeys.HttpRetryMaxAttempts);
        var retryBaseDelayMs = ParseSchemaInt(ConfigKeys.HttpRetryBaseDelayMs);
        var retryMaxDelayMs = ParseSchemaInt(ConfigKeys.HttpRetryMaxDelayMs);
        var bpInitialMax = ParseSchemaInt(ConfigKeys.BackpressureInitialMax);
        var bpSoftFactor = ParseSchemaInt(ConfigKeys.BackpressureSoftFactor);
        var bpSevereFactor = ParseSchemaInt(ConfigKeys.BackpressureSevereFactor);
        var bpRecoveryIntervalMs = ParseSchemaInt(ConfigKeys.BackpressureRecoveryIntervalMs);
        var bpRecoveryStep = ParseSchemaInt(ConfigKeys.BackpressureRecoveryStep);
        var bpDecayQuietMs = ParseSchemaInt(ConfigKeys.BackpressureDecayQuietMs);
        var bpFloor = ParseSchemaInt(ConfigKeys.BackpressureFloor);
        var bpSevereThreshold = ParseSchemaInt(ConfigKeys.BackpressureSevereThreshold);
        var oauthTimeoutMs = ParseSchemaInt(ConfigKeys.OAuthTimeoutMs);
        var oauthRetryMax = ParseSchemaInt(ConfigKeys.OAuthRetryMax);
        var oauthRetryBaseDelayMs = ParseSchemaInt(ConfigKeys.OAuthRetryBaseDelayMs);
        var eventualPollDefaultMs = ParseSchemaInt(ConfigKeys.EventualPollDefaultMs);

        // Parse optional worker defaults
        int? workerTimeout = rawMap.ContainsKey(ConfigKeys.WorkerTimeout)
            ? ParseInt(ConfigKeys.WorkerTimeout, 0)
            : null;
        int? workerMaxConcurrent = rawMap.ContainsKey(ConfigKeys.WorkerMaxConcurrentJobs)
            ? ParseInt(ConfigKeys.WorkerMaxConcurrentJobs, 0)
            : null;
        int? workerRequestTimeout = rawMap.ContainsKey(ConfigKeys.WorkerRequestTimeout)
            ? ParseSignedInt(ConfigKeys.WorkerRequestTimeout, 0)
            : null;
        var workerName = rawMap.GetValueOrDefault(ConfigKeys.WorkerName);
        int? workerJitter = rawMap.ContainsKey(ConfigKeys.WorkerStartupJitterMaxSeconds)
            ? ParseInt(ConfigKeys.WorkerStartupJitterMaxSeconds, 0)
            : null;
        var hasWorkerDefaults = workerTimeout != null || workerMaxConcurrent != null
            || workerRequestTimeout != null || workerName != null || workerJitter != null;

        if (errors.Count > 0)
            throw new CamundaConfigurationException(errors);

        // Normalize restAddress to /v2
        var restAddress = rawMap.GetValueOrDefault(ConfigKeys.RestAddress, ConfigSchema.StringDefault(ConfigKeys.RestAddress))!;
        if (!string.IsNullOrEmpty(restAddress) && !restAddress.TrimEnd('/').EndsWith("/v2", StringComparison.OrdinalIgnoreCase))
            restAddress = restAddress.TrimEnd('/') + "/v2";

        // Backpressure profile
        var profile = rawMap.GetValueOrDefault(ConfigKeys.BackpressureProfile, ConfigSchema.StringDefault(ConfigKeys.BackpressureProfile))!.Trim().ToUpperInvariant();

        return new CamundaConfig
        {
            RestAddress = restAddress,
            TokenAudience = rawMap.GetValueOrDefault(ConfigKeys.TokenAudience, ConfigSchema.StringDefault(ConfigKeys.TokenAudience))!,
            DefaultTenantId = rawMap.GetValueOrDefault(ConfigKeys.DefaultTenantId, ConfigSchema.StringDefault(ConfigKeys.DefaultTenantId))!,
            HttpRetry = new HttpRetryConfig
            {
                MaxAttempts = retryMaxAttempts,
                BaseDelayMs = retryBaseDelayMs,
                MaxDelayMs = retryMaxDelayMs,
            },
            Backpressure = new BackpressureConfig
            {
                Enabled = profile != "LEGACY",
                Profile = profile,
                ObserveOnly = profile == "LEGACY",
                InitialMax = bpInitialMax,
                SoftFactor = Math.Clamp(bpSoftFactor / 100.0, 0.01, 1.0),
                SevereFactor = Math.Clamp(bpSevereFactor / 100.0, 0.01, 1.0),
                RecoveryIntervalMs = bpRecoveryIntervalMs,
                RecoveryStep = bpRecoveryStep,
                DecayQuietMs = bpDecayQuietMs,
                Floor = bpFloor,
                SevereThreshold = bpSevereThreshold,
            },
            OAuth = new OAuthConfig
            {
                ClientId = rawMap.GetValueOrDefault(ConfigKeys.ClientId),
                ClientSecret = rawMap.GetValueOrDefault(ConfigKeys.ClientSecret),
                OAuthUrl = rawMap.GetValueOrDefault(ConfigKeys.OAuthUrl, ConfigSchema.StringDefault(ConfigKeys.OAuthUrl))!,
                GrantType = rawMap.GetValueOrDefault(ConfigKeys.OAuthGrantType, ConfigSchema.StringDefault(ConfigKeys.OAuthGrantType))!,
                Scope = rawMap.GetValueOrDefault(ConfigKeys.OAuthScope),
                TimeoutMs = oauthTimeoutMs,
                Retry = new OAuthRetryConfig
                {
                    Max = oauthRetryMax,
                    BaseDelayMs = oauthRetryBaseDelayMs,
                },
            },
            Auth = new AuthConfig
            {
                Strategy = authStrategy,
                Basic = authStrategy == AuthStrategy.Basic
                    ? new BasicAuthConfig
                    {
                        Username = rawMap.GetValueOrDefault(ConfigKeys.BasicAuthUsername),
                        Password = rawMap.GetValueOrDefault(ConfigKeys.BasicAuthPassword),
                    }
                    : null,
            },
            Validation = validation,
            LogLevel = rawMap.GetValueOrDefault(ConfigKeys.LogLevel, ConfigSchema.StringDefault(ConfigKeys.LogLevel))!,
            Eventual = new EventualConfig
            {
                PollDefaultMs = eventualPollDefaultMs,
            },
            WorkerDefaults = hasWorkerDefaults
                ? new WorkerDefaultsConfig
                {
                    JobTimeoutMs = workerTimeout,
                    MaxConcurrentJobs = workerMaxConcurrent,
                    PollTimeoutMs = workerRequestTimeout,
                    WorkerName = workerName,
                    StartupJitterMaxSeconds = workerJitter,
                }
                : null,
            Tls = hasTls
                ? new TlsConfig
                {
                    Cert = rawMap.GetValueOrDefault(ConfigKeys.MtlsCert),
                    CertPath = rawMap.GetValueOrDefault(ConfigKeys.MtlsCertPath),
                    Key = rawMap.GetValueOrDefault(ConfigKeys.MtlsKey),
                    KeyPath = rawMap.GetValueOrDefault(ConfigKeys.MtlsKeyPath),
                    Ca = rawMap.GetValueOrDefault(ConfigKeys.MtlsCa),
                    CaPath = rawMap.GetValueOrDefault(ConfigKeys.MtlsCaPath),
                    KeyPassphrase = rawMap.GetValueOrDefault(ConfigKeys.MtlsKeyPassphrase),
                }
                : null,
        };
    }

    private static ValidationConfig ParseValidation(string raw, List<ConfigErrorDetail> errors)
    {
        var val = raw.Trim();
        if (val.Length == 0)
            return new ValidationConfig();

        if (Enum.TryParse<ValidationMode>(val, true, out var single))
            return new ValidationConfig { Request = single, Response = single, Raw = $"req:{val.ToLowerInvariant()},res:{val.ToLowerInvariant()}" };

        var parts = val.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var req = ValidationMode.None;
        var res = ValidationMode.None;

        foreach (var part in parts)
        {
            var split = part.Split(':', 2);
            if (split.Length != 2)
            {
                errors.Add(new ConfigErrorDetail
                {
                    Code = ConfigErrorCode.InvalidValidationSyntax,
                    Key = ConfigKeys.Validation,
                    Message = $"Malformed segment '{part}'"
                });
                continue;
            }

            var scope = split[0].Trim().ToLowerInvariant();
            var mode = split[1].Trim().ToLowerInvariant();

            if (scope != "req" && scope != "res")
            {
                errors.Add(new ConfigErrorDetail
                {
                    Code = ConfigErrorCode.InvalidValidationSyntax,
                    Key = ConfigKeys.Validation,
                    Message = $"Unknown scope '{scope}'"
                });
                continue;
            }

            if (!Enum.TryParse<ValidationMode>(mode, true, out var parsed))
            {
                errors.Add(new ConfigErrorDetail
                {
                    Code = ConfigErrorCode.InvalidValidationSyntax,
                    Key = ConfigKeys.Validation,
                    Message = $"Unknown mode '{mode}'"
                });
                continue;
            }

            if (scope == "req")
                req = parsed;
            else
                res = parsed;
        }

        return new ValidationConfig
        {
            Request = req,
            Response = res,
            Raw = $"req:{req.ToString().ToLowerInvariant()},res:{res.ToString().ToLowerInvariant()}",
        };
    }

    /// <summary>
    /// Maps PascalCase <c>IConfiguration</c> keys (from <c>appsettings.json</c>) to canonical
    /// <c>CAMUNDA_*</c> env-var names. Derived from the single-source <see cref="ConfigSchema"/>
    /// (each descriptor's <c>ConfigPaths</c>).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ConfigKeyMap = ConfigSchema.ConfigKeyMap;

    /// <summary>
    /// Extract configuration values from an <see cref="IConfiguration"/> section,
    /// mapping PascalCase keys to canonical <c>CAMUNDA_*</c> env-var names.
    /// </summary>
    internal static Dictionary<string, string> ExtractFromConfiguration(IConfiguration configuration)
    {
        var result = new Dictionary<string, string>();

        foreach (var (configKey, envKey) in ConfigKeyMap)
        {
            var value = configuration[configKey];
            if (!string.IsNullOrEmpty(value))
                result[envKey] = value;
        }

        return result;
    }

    /// <summary>
    /// Redact a secret value for logging.
    /// </summary>
    public static string RedactSecret(string value)
    {
        if (value.Length <= 4)
            return new string('*', value.Length);
        return new string('*', value.Length - 4) + value[^4..];
    }
}
