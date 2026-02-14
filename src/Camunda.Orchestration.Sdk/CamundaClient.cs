using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Camunda.Orchestration.Sdk.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Camunda.Orchestration.Sdk;
/// <summary>
/// Primary Camunda client. Provides typed methods for all Camunda 8 REST API operations.
/// 
/// Auto-generated operation methods are added in the Generated/ partial class files.
/// This class provides the infrastructure: configuration, auth, retry, backpressure.
/// </summary>
public partial class CamundaClient : IDisposable
{
    private readonly CamundaConfig _config;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly ILogger _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly BackpressureManager _bp;
    private readonly bool _throwOnError;
    internal readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Create a new CamundaClient with the given options.
    /// </summary>
    public CamundaClient(CamundaOptions? options = null)
    {
        options ??= new CamundaOptions();

        _config = ConfigurationHydrator.Hydrate(
            env: options.Env,
            overrides: options.Config,
            configuration: options.Configuration);

        var loggerFactory = options.LoggerFactory ?? CreateDefaultLoggerFactory(_config.LogLevel);
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<CamundaClient>();

        _throwOnError = options.ThrowOnError;

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true,
            Converters =
            {
                new Runtime.TolerantEnumConverterFactory(),
                new Runtime.CamundaKeyJsonConverterFactory(),
                new Runtime.CamundaLongKeyJsonConverterFactory(),
            },
        };

        if (options.HttpClient != null)
        {
            _httpClient = options.HttpClient;
            _ownsHttpClient = false;
        }
        else
        {
            var authHandler = new AuthHandler(_config, options.HttpMessageHandler, _logger);
            _httpClient = new HttpClient(authHandler)
            {
                BaseAddress = string.IsNullOrEmpty(_config.RestAddress)
                    ? null
                    : new Uri(_config.RestAddress.TrimEnd('/') + "/"),
            };
            _ownsHttpClient = true;
        }

        _bp = new BackpressureManager(_config.Backpressure, _logger);

        _logger.LogDebug("CamundaClient constructed with auth strategy {Strategy}", _config.Auth.Strategy);
    }

    /// <summary>
    /// The current hydrated configuration (read-only).
    /// </summary>
    public CamundaConfig Config => _config;

    /// <summary>
    /// Current backpressure state snapshot.
    /// </summary>
    public BackpressureState GetBackpressureState() => _bp.GetState();

    /// <summary>
    /// Invoke an API operation with retry and backpressure management.
    /// </summary>
    internal async Task<T> InvokeWithRetryAsync<T>(
        Func<Task<T>> operation,
        string operationId,
        bool exempt = false,
        CancellationToken ct = default)
    {
        if (!exempt)
            await _bp.AcquireAsync(ct);

        try
        {
            var result = await HttpRetryExecutor.ExecuteWithRetryAsync(
                operation,
                _config.HttpRetry,
                _logger,
                ex =>
                {
                    var decision = HttpRetryExecutor.DefaultClassify(ex);
                    if (decision.Retryable && decision.Reason.Contains("429"))
                        _bp.RecordBackpressure();
                    return decision;
                },
                ct);

            _bp.RecordHealthy();
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (ex is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests })
                _bp.RecordBackpressure();
            throw;
        }
        finally
        {
            if (!exempt)
                _bp.Release();
        }
    }

    /// <summary>
    /// Execute an HTTP request and deserialize the response.
    /// </summary>
    internal async Task<TResponse> SendAsync<TResponse>(
        HttpMethod method,
        string path,
        object? body = null,
        CancellationToken ct = default)
    {
        _logger.LogDebug("HTTP {Method} {Path}", method, path);

        // Strip leading '/' so the path resolves relative to BaseAddress.
        // Without this, "/topology" would be absolute from the host root,
        // overriding the "/v2" segment of the base address.
        var relativePath = path.TrimStart('/');
        using var request = new HttpRequestMessage(method, relativePath);

        if (body != null)
        {
            var json = JsonSerializer.Serialize(body, _jsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            var exception = BuildHttpException(response.StatusCode, errorBody, path);
            _logger.LogWarning("HTTP {Method} {Path} failed with {Status}", method, path, (int)response.StatusCode);

            if (_throwOnError)
                throw exception;
        }
        else
        {
            _logger.LogDebug("HTTP {Method} {Path} -> {Status}", method, path, (int)response.StatusCode);
        }

        if (response.StatusCode == HttpStatusCode.NoContent || typeof(TResponse) == typeof(VoidResponse))
            return default!;

        var content = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(content))
            return default!;

        return JsonSerializer.Deserialize<TResponse>(content, _jsonOptions)!;
    }

    /// <summary>
    /// Execute an HTTP request that returns no content (void operations like DELETE).
    /// </summary>
    internal async Task SendVoidAsync(
        HttpMethod method,
        string path,
        object? body = null,
        CancellationToken ct = default)
    {
        _logger.LogDebug("HTTP {Method} {Path}", method, path);

        var relativePath = path.TrimStart('/');
        using var request = new HttpRequestMessage(method, relativePath);

        if (body != null)
        {
            var json = JsonSerializer.Serialize(body, _jsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("HTTP {Method} {Path} failed with {Status}", method, path, (int)response.StatusCode);
            throw BuildHttpException(response.StatusCode, errorBody, path);
        }

        _logger.LogDebug("HTTP {Method} {Path} -> {Status}", method, path, (int)response.StatusCode);
    }

    /// <summary>
    /// Send a multipart/form-data request (used for deployment).
    /// </summary>
    internal async Task<TResponse> SendMultipartAsync<TResponse>(
        string path,
        MultipartFormDataContent content,
        CancellationToken ct = default)
    {
        _logger.LogDebug("HTTP POST {Path} (multipart)", path);

        var relativePath = path.TrimStart('/');
        using var request = new HttpRequestMessage(HttpMethod.Post, relativePath) { Content = content };
        var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("HTTP POST {Path} (multipart) failed with {Status}", path, (int)response.StatusCode);
            throw BuildHttpException(response.StatusCode, errorBody, path);
        }

        _logger.LogDebug("HTTP POST {Path} (multipart) -> {Status}", path, (int)response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<TResponse>(responseContent, _jsonOptions)!;
    }

    private static HttpSdkException BuildHttpException(HttpStatusCode statusCode, string body, string path)
    {
        string? type = null, title = null, detail = null, instance = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
            title = root.TryGetProperty("title", out var ti) ? ti.GetString() : null;
            detail = root.TryGetProperty("detail", out var d) ? d.GetString() : null;
            instance = root.TryGetProperty("instance", out var i) ? i.GetString() : null;
        }
        catch
        {
            // Not a problem details response
        }

        var message = title ?? detail ?? $"HTTP {(int)statusCode}";
        var isBp = statusCode == HttpStatusCode.TooManyRequests ||
                   (statusCode == HttpStatusCode.ServiceUnavailable && title == "RESOURCE_EXHAUSTED") ||
                   (statusCode == HttpStatusCode.InternalServerError && detail?.Contains("RESOURCE_EXHAUSTED") == true);

        return new HttpSdkException(message, (int)statusCode, path)
        {
            Type = type,
            Title = title,
            Detail = detail,
            Instance = instance,
            IsBackpressure = isBp,
        };
    }

    /// <summary>
    /// Inject default tenantId into a body if the body has a TenantId property that is null.
    /// </summary>
    internal void InjectDefaultTenantId(IDictionary<string, object?>? bodyDict)
    {
        if (bodyDict == null)
            return;
        if (!bodyDict.TryGetValue("tenantId", out var tenantId) || tenantId != null)
            return;
        bodyDict["tenantId"] = _config.DefaultTenantId;
        _logger.LogTrace("tenant.default.inject: {Tenant}", _config.DefaultTenantId);
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
        _bp.Dispose();
        GC.SuppressFinalize(this);
    }

    private static ILoggerFactory CreateDefaultLoggerFactory(string sdkLogLevel)
    {
        var level = SdkConsoleLogger.ParseSdkLogLevel(sdkLogLevel);
        if (level == LogLevel.None)
            return NullLoggerFactory.Instance;
        return new SdkConsoleLoggerFactory(level);
    }
}

/// <summary>
/// Marker type for void HTTP responses.
/// </summary>
internal readonly struct VoidResponse;

/// <summary>
/// Factory method for creating CamundaClient instances.
/// </summary>
public static class Camunda
{
    /// <summary>
    /// Create a new CamundaClient.
    /// </summary>
    public static CamundaClient CreateClient(CamundaOptions? options = null)
        => new(options);
}
