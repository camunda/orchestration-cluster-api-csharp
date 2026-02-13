namespace Camunda.Client.Runtime;

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
    private static readonly Dictionary<string, string> Defaults = new()
    {
        ["CAMUNDA_REST_ADDRESS"] = "",
        ["CAMUNDA_TOKEN_AUDIENCE"] = "",
        ["CAMUNDA_DEFAULT_TENANT_ID"] = "<default>",
        ["CAMUNDA_AUTH_STRATEGY"] = "NONE",
        ["CAMUNDA_OAUTH_URL"] = "",
        ["CAMUNDA_OAUTH_GRANT_TYPE"] = "client_credentials",
        ["CAMUNDA_OAUTH_TIMEOUT_MS"] = "5000",
        ["CAMUNDA_OAUTH_RETRY_MAX"] = "5",
        ["CAMUNDA_OAUTH_RETRY_BASE_DELAY_MS"] = "1000",
        ["CAMUNDA_SDK_LOG_LEVEL"] = "error",
        ["CAMUNDA_SDK_VALIDATION"] = "req:none,res:none",
        ["CAMUNDA_SDK_HTTP_RETRY_MAX_ATTEMPTS"] = "3",
        ["CAMUNDA_SDK_HTTP_RETRY_BASE_DELAY_MS"] = "100",
        ["CAMUNDA_SDK_HTTP_RETRY_MAX_DELAY_MS"] = "2000",
        ["CAMUNDA_SDK_BACKPRESSURE_PROFILE"] = "BALANCED",
        ["CAMUNDA_SDK_EVENTUAL_POLL_DEFAULT_MS"] = "500",
    };

    private static readonly HashSet<string> SecretKeys =
    [
        "CAMUNDA_CLIENT_SECRET",
        "CAMUNDA_BASIC_AUTH_PASSWORD",
    ];

    /// <summary>
    /// Hydrate configuration from environment and optional overrides.
    /// </summary>
    public static CamundaConfig Hydrate(
        Dictionary<string, string?>? env = null,
        Dictionary<string, string>? overrides = null)
    {
        var errors = new List<ConfigErrorDetail>();
        var rawMap = new Dictionary<string, string>();

        // Build merged input: env → override (override wins)
        var input = new Dictionary<string, string>();
        if (env != null)
        {
            foreach (var (k, v) in env)
            {
                if (v != null && v.Trim().Length > 0)
                    input[k] = v.Trim();
            }
        }
        else
        {
            // Read from actual environment
            foreach (var key in Defaults.Keys)
            {
                var v = Environment.GetEnvironmentVariable(key);
                if (v != null && v.Trim().Length > 0)
                    input[key] = v.Trim();
            }
            // Also check for CAMUNDA_CLIENT_ID, CAMUNDA_CLIENT_SECRET, etc.
            foreach (var extra in new[]
                     {
                         "CAMUNDA_CLIENT_ID", "CAMUNDA_CLIENT_SECRET",
                         "CAMUNDA_BASIC_AUTH_USERNAME", "CAMUNDA_BASIC_AUTH_PASSWORD",
                         "CAMUNDA_OAUTH_SCOPE", "CAMUNDA_OAUTH_CACHE_DIR",
                         "ZEEBE_REST_ADDRESS"
                     })
            {
                var v = Environment.GetEnvironmentVariable(extra);
                if (v != null && v.Trim().Length > 0)
                    input[extra] = v.Trim();
            }
        }

        if (overrides != null)
        {
            foreach (var (k, v) in overrides)
                input[k] = v;
        }

        // Alias: ZEEBE_REST_ADDRESS → CAMUNDA_REST_ADDRESS
        if (!input.ContainsKey("CAMUNDA_REST_ADDRESS") && input.TryGetValue("ZEEBE_REST_ADDRESS", out var zeebe))
            input["CAMUNDA_REST_ADDRESS"] = zeebe;

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
        var userSetStrategy = input.ContainsKey("CAMUNDA_AUTH_STRATEGY");
        if (!userSetStrategy &&
            rawMap.GetValueOrDefault("CAMUNDA_AUTH_STRATEGY") == "NONE" &&
            !string.IsNullOrEmpty(rawMap.GetValueOrDefault("CAMUNDA_OAUTH_URL")) &&
            !string.IsNullOrEmpty(rawMap.GetValueOrDefault("CAMUNDA_CLIENT_ID")) &&
            !string.IsNullOrEmpty(rawMap.GetValueOrDefault("CAMUNDA_CLIENT_SECRET")))
        {
            rawMap["CAMUNDA_AUTH_STRATEGY"] = "OAUTH";
        }

        // Validate auth strategy
        var authStrategyRaw = rawMap.GetValueOrDefault("CAMUNDA_AUTH_STRATEGY", "NONE")!.Trim().ToUpperInvariant();
        if (!Enum.TryParse<AuthStrategy>(authStrategyRaw, true, out var authStrategy))
        {
            errors.Add(new ConfigErrorDetail
            {
                Code = ConfigErrorCode.InvalidEnum,
                Key = "CAMUNDA_AUTH_STRATEGY",
                Message = $"Invalid auth strategy '{authStrategyRaw}'. Expected NONE|OAUTH|BASIC."
            });
            authStrategy = AuthStrategy.None;
        }

        // Validate conditional requirements
        if (authStrategy == AuthStrategy.OAuth)
        {
            var missing = new List<string>();
            if (string.IsNullOrEmpty(rawMap.GetValueOrDefault("CAMUNDA_CLIENT_ID")))
                missing.Add("CAMUNDA_CLIENT_ID");
            if (string.IsNullOrEmpty(rawMap.GetValueOrDefault("CAMUNDA_CLIENT_SECRET")))
                missing.Add("CAMUNDA_CLIENT_SECRET");
            if (string.IsNullOrEmpty(rawMap.GetValueOrDefault("CAMUNDA_OAUTH_URL")))
                missing.Add("CAMUNDA_OAUTH_URL");
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
            if (string.IsNullOrEmpty(rawMap.GetValueOrDefault("CAMUNDA_BASIC_AUTH_USERNAME")))
                missing.Add("CAMUNDA_BASIC_AUTH_USERNAME");
            if (string.IsNullOrEmpty(rawMap.GetValueOrDefault("CAMUNDA_BASIC_AUTH_PASSWORD")))
                missing.Add("CAMUNDA_BASIC_AUTH_PASSWORD");
            if (missing.Count > 0)
            {
                errors.Add(new ConfigErrorDetail
                {
                    Code = ConfigErrorCode.MissingRequired,
                    Message = $"Missing required configuration for BASIC: {string.Join(", ", missing)}"
                });
            }
        }

        // Validate integers
        int ParseInt(string key, int fallback)
        {
            var raw = rawMap.GetValueOrDefault(key, fallback.ToString(System.Globalization.CultureInfo.InvariantCulture))!;
            if (int.TryParse(raw, out var val))
                return val;
            errors.Add(new ConfigErrorDetail
            {
                Code = ConfigErrorCode.InvalidInteger,
                Key = key,
                Message = $"Invalid integer '{raw}'. Only unsigned base-10 integers allowed."
            });
            return fallback;
        }

        // Parse validation
        var validation = ParseValidation(rawMap.GetValueOrDefault("CAMUNDA_SDK_VALIDATION", "req:none,res:none")!, errors);

        // Eagerly parse all integer config values so errors are collected before the check
        var retryMaxAttempts = ParseInt("CAMUNDA_SDK_HTTP_RETRY_MAX_ATTEMPTS", 3);
        var retryBaseDelayMs = ParseInt("CAMUNDA_SDK_HTTP_RETRY_BASE_DELAY_MS", 100);
        var retryMaxDelayMs = ParseInt("CAMUNDA_SDK_HTTP_RETRY_MAX_DELAY_MS", 2000);
        var bpInitialMax = ParseInt("CAMUNDA_SDK_BACKPRESSURE_INITIAL_MAX", 16);
        var bpSoftFactor = ParseInt("CAMUNDA_SDK_BACKPRESSURE_SOFT_FACTOR", 70);
        var bpSevereFactor = ParseInt("CAMUNDA_SDK_BACKPRESSURE_SEVERE_FACTOR", 50);
        var bpRecoveryIntervalMs = ParseInt("CAMUNDA_SDK_BACKPRESSURE_RECOVERY_INTERVAL_MS", 1000);
        var bpRecoveryStep = ParseInt("CAMUNDA_SDK_BACKPRESSURE_RECOVERY_STEP", 1);
        var bpDecayQuietMs = ParseInt("CAMUNDA_SDK_BACKPRESSURE_DECAY_QUIET_MS", 2000);
        var bpFloor = ParseInt("CAMUNDA_SDK_BACKPRESSURE_FLOOR", 1);
        var bpSevereThreshold = ParseInt("CAMUNDA_SDK_BACKPRESSURE_SEVERE_THRESHOLD", 3);
        var oauthTimeoutMs = ParseInt("CAMUNDA_OAUTH_TIMEOUT_MS", 5000);
        var oauthRetryMax = ParseInt("CAMUNDA_OAUTH_RETRY_MAX", 5);
        var oauthRetryBaseDelayMs = ParseInt("CAMUNDA_OAUTH_RETRY_BASE_DELAY_MS", 1000);
        var eventualPollDefaultMs = ParseInt("CAMUNDA_SDK_EVENTUAL_POLL_DEFAULT_MS", 500);

        if (errors.Count > 0)
            throw new CamundaConfigurationException(errors);

        // Normalize restAddress to /v2
        var restAddress = rawMap.GetValueOrDefault("CAMUNDA_REST_ADDRESS", "")!;
        if (!string.IsNullOrEmpty(restAddress) && !restAddress.TrimEnd('/').EndsWith("/v2", StringComparison.OrdinalIgnoreCase))
            restAddress = restAddress.TrimEnd('/') + "/v2";

        // Backpressure profile
        var profile = rawMap.GetValueOrDefault("CAMUNDA_SDK_BACKPRESSURE_PROFILE", "BALANCED")!.Trim().ToUpperInvariant();

        return new CamundaConfig
        {
            RestAddress = restAddress,
            TokenAudience = rawMap.GetValueOrDefault("CAMUNDA_TOKEN_AUDIENCE", "")!,
            DefaultTenantId = rawMap.GetValueOrDefault("CAMUNDA_DEFAULT_TENANT_ID", "<default>")!,
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
                ClientId = rawMap.GetValueOrDefault("CAMUNDA_CLIENT_ID"),
                ClientSecret = rawMap.GetValueOrDefault("CAMUNDA_CLIENT_SECRET"),
                OAuthUrl = rawMap.GetValueOrDefault("CAMUNDA_OAUTH_URL", "")!,
                GrantType = rawMap.GetValueOrDefault("CAMUNDA_OAUTH_GRANT_TYPE", "client_credentials")!,
                Scope = rawMap.GetValueOrDefault("CAMUNDA_OAUTH_SCOPE"),
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
                        Username = rawMap.GetValueOrDefault("CAMUNDA_BASIC_AUTH_USERNAME"),
                        Password = rawMap.GetValueOrDefault("CAMUNDA_BASIC_AUTH_PASSWORD"),
                    }
                    : null,
            },
            Validation = validation,
            LogLevel = rawMap.GetValueOrDefault("CAMUNDA_SDK_LOG_LEVEL", "error")!,
            Eventual = new EventualConfig
            {
                PollDefaultMs = eventualPollDefaultMs,
            },
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
                    Key = "CAMUNDA_SDK_VALIDATION",
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
                    Key = "CAMUNDA_SDK_VALIDATION",
                    Message = $"Unknown scope '{scope}'"
                });
                continue;
            }

            if (!Enum.TryParse<ValidationMode>(mode, true, out var parsed))
            {
                errors.Add(new ConfigErrorDetail
                {
                    Code = ConfigErrorCode.InvalidValidationSyntax,
                    Key = "CAMUNDA_SDK_VALIDATION",
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
    /// Redact a secret value for logging.
    /// </summary>
    public static string RedactSecret(string value)
    {
        if (value.Length <= 4)
            return new string('*', value.Length);
        return new string('*', value.Length - 4) + value[^4..];
    }
}
