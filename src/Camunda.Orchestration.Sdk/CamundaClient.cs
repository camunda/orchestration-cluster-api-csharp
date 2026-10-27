using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Camunda.Orchestration.Sdk;
/// <summary>
/// Primary Camunda client. Provides typed methods for all Camunda 8 REST API operations.
/// 
/// Auto-generated operation methods are added in the Generated/ partial class files.
/// This class provides the infrastructure: configuration, auth, retry, backpressure.
/// </summary>
public partial class CamundaClient : IDisposable, IEngineClockTarget
{
    /// <summary>
    /// Create a new CamundaClient.
    /// </summary>
    public static CamundaClient Create(CamundaOptions? options = null)
        => new(options);

    private readonly CamundaConfig _config;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly ILogger _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly BackpressureManager _bp;
    private readonly TimeProvider _timeProvider;
    private readonly IRandomSource _randomSource;
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

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true,
            Converters =
            {
                new TolerantEnumConverterFactory(),
                new CamundaKeyJsonConverterFactory(),
                new CamundaLongKeyJsonConverterFactory(),
            },
        };

        _timeProvider = options.TimeProvider ?? CamundaTimeProvider.Live;
        _randomSource = options.RandomSource ?? CamundaRandomSource.Live;

        if (options.HttpClient != null)
        {
            _httpClient = options.HttpClient;
            _ownsHttpClient = false;
        }
        else
        {
            var tlsHandler = TlsHelper.BuildHandler(_config.Tls);
            var innerHandler = options.HttpMessageHandler ?? tlsHandler;
            var authHandler = new AuthHandler(_config, innerHandler, _logger, _timeProvider, _randomSource);
            _httpClient = new HttpClient(authHandler)
            {
                BaseAddress = string.IsNullOrEmpty(_config.RestAddress)
                    ? null
                    : new Uri(_config.RestAddress.TrimEnd('/') + "/"),
            };
            _ownsHttpClient = true;
        }

        _bp = new BackpressureManager(_config.Backpressure, _logger, _timeProvider);

        if (_logger.IsEnabled(LogLevel.Debug))
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
                _timeProvider,
                _randomSource,
                ex =>
                {
                    var decision = HttpRetryExecutor.DefaultClassify(ex);
                    // Record every backpressure response exactly once, here in the
                    // classifier. The executor invokes this callback for every failed
                    // attempt — including the final one, before it checks the attempt
                    // budget and rethrows — so recording here (and nowhere else) feeds
                    // the manager exactly one signal per HTTP response, even when
                    // MaxAttempts = 1.
                    if (IsBackpressureSignal(ex))
                        _bp.RecordBackpressure();
                    return decision;
                },
                ct);

            _bp.RecordHealthy();
            return result;
        }
        finally
        {
            if (!exempt)
                _bp.Release();
        }
    }

    /// <summary>
    /// Whether an exception carries a broker/gateway backpressure signal: HTTP 429,
    /// or a 503/500 the server marked <c>RESOURCE_EXHAUSTED</c> (surfaced as
    /// <see cref="HttpSdkException.IsBackpressure"/>). An ordinary 503/500 outage is
    /// still retried, but it is <em>not</em> load shedding, so it must not shrink the
    /// adaptive gate: SDK exceptions therefore match only on the parsed
    /// <see cref="HttpSdkException.IsBackpressure"/> flag or an explicit 429. Transport
    /// <see cref="HttpRequestException"/>s carry no parsed body, so they fall back to a
    /// status-only match on 429/503.
    /// </summary>
    private static bool IsBackpressureSignal(Exception ex) => ex switch
    {
        HttpSdkException sdkEx => sdkEx.IsBackpressure || sdkEx.Status == 429,
        HttpRequestException httpEx => httpEx.StatusCode
            is HttpStatusCode.TooManyRequests
            or HttpStatusCode.ServiceUnavailable,
        _ => false,
    };

    /// <summary>
    /// Execute an HTTP request and deserialize the response.
    /// </summary>
    internal async Task<TResponse> SendAsync<TResponse>(
        HttpMethod method,
        string path,
        object? body = null,
        bool useRootServer = false,
        CancellationToken ct = default)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("HTTP {Method} {Path}", method, path);

        using var request = new HttpRequestMessage(method, ResolveRequestUri(path, useRootServer));

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
            if (_logger.IsEnabled(LogLevel.Warning))
                _logger.LogWarning("HTTP {Method} {Path} failed with {Status}", method, path, (int)response.StatusCode);

            throw exception;
        }
        else
        {
            if (_logger.IsEnabled(LogLevel.Debug))
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
        bool useRootServer = false,
        CancellationToken ct = default)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("HTTP {Method} {Path}", method, path);

        using var request = new HttpRequestMessage(method, ResolveRequestUri(path, useRootServer));

        if (body != null)
        {
            var json = JsonSerializer.Serialize(body, _jsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            if (_logger.IsEnabled(LogLevel.Warning))
                _logger.LogWarning("HTTP {Method} {Path} failed with {Status}", method, path, (int)response.StatusCode);
            throw BuildHttpException(response.StatusCode, errorBody, path);
        }

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("HTTP {Method} {Path} -> {Status}", method, path, (int)response.StatusCode);
    }

    /// <summary>
    /// Execute an HTTP request and return the response body as raw bytes.
    /// Used for operations that return application/octet-stream (binary content).
    /// </summary>
    internal async Task<byte[]> SendBinaryAsync(
        HttpMethod method,
        string path,
        object? body = null,
        bool useRootServer = false,
        CancellationToken ct = default)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("HTTP {Method} {Path} (binary)", method, path);

        using var request = new HttpRequestMessage(method, ResolveRequestUri(path, useRootServer));

        if (body != null)
        {
            var json = JsonSerializer.Serialize(body, _jsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            if (_logger.IsEnabled(LogLevel.Warning))
                _logger.LogWarning("HTTP {Method} {Path} (binary) failed with {Status}", method, path, (int)response.StatusCode);
            throw BuildHttpException(response.StatusCode, errorBody, path);
        }

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("HTTP {Method} {Path} (binary) -> {Status}", method, path, (int)response.StatusCode);

        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    /// <summary>
    /// Send a multipart/form-data request (used for deployment).
    /// </summary>
    internal async Task<TResponse> SendMultipartAsync<TResponse>(
        string path,
        MultipartFormDataContent content,
        bool useRootServer = false,
        CancellationToken ct = default)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("HTTP POST {Path} (multipart)", path);

        // This delegate is re-invoked across retry attempts (see InvokeWithRetryAsync),
        // always with the SAME caller-owned content. HttpClient disposes a request's
        // content once the send completes, so attaching the caller's content directly
        // would destroy it after the first attempt and a retried attempt would fail
        // reading a disposed multipart body. Send a fresh, replayable copy each attempt
        // and leave the caller's content intact for the next one.
        using var request = new HttpRequestMessage(HttpMethod.Post, ResolveRequestUri(path, useRootServer))
        {
            Content = await CloneMultipartAsync(content, ct),
        };
        var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            if (_logger.IsEnabled(LogLevel.Warning))
                _logger.LogWarning("HTTP POST {Path} (multipart) failed with {Status}", path, (int)response.StatusCode);
            throw BuildHttpException(response.StatusCode, errorBody, path);
        }

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("HTTP POST {Path} (multipart) -> {Status}", path, (int)response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<TResponse>(responseContent, _jsonOptions)!;
    }

    /// <summary>
    /// Builds a fresh, independently sendable copy of a multipart payload so the caller's
    /// content survives being sent (and disposed) on each retry attempt. The original
    /// boundary is reused so the serialized body is byte-identical across attempts, and
    /// the caller's content is only read, never disposed.
    /// </summary>
    /// <remarks>
    /// To avoid buffering large uploads into memory, a part backed by a replayable
    /// (seekable) stream is cloned as a thin wrapper that re-reads the caller's stream
    /// from its original position on each attempt and rewinds it afterwards — the stream
    /// is never buffered wholesale and never disposed (caller ownership is preserved).
    /// Only a non-seekable part (which cannot be rewound for a retry) is buffered, once,
    /// into bytes. See https://github.com/camunda/orchestration-cluster-api-csharp/issues/479.
    /// </remarks>
    private static async Task<MultipartFormDataContent> CloneMultipartAsync(
        MultipartFormDataContent source,
        CancellationToken ct)
    {
        // Reuse the source's boundary so every attempt serializes to the same bytes.
        var boundary = source.Headers.ContentType?.Parameters
            .FirstOrDefault(p => string.Equals(p.Name, "boundary", StringComparison.OrdinalIgnoreCase))
            ?.Value?.Trim('"');
        var copy = string.IsNullOrEmpty(boundary)
            ? new MultipartFormDataContent()
            : new MultipartFormDataContent(boundary);

        foreach (var part in source)
        {
            var clonePart = await CreateReplayablePartAsync(part, ct);
            foreach (var header in part.Headers)
            {
                // Content-Length is recomputed by the cloned part; copying it would
                // duplicate the header. All other part headers (Content-Type,
                // Content-Disposition with name/filename) must be preserved verbatim.
                if (string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                    continue;
                clonePart.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            copy.Add(clonePart);
        }

        return copy;

        static async Task<HttpContent> CreateReplayablePartAsync(HttpContent part, CancellationToken ct)
        {
            // A seekable stream can be replayed from its original position on each
            // attempt, so wrap it instead of buffering it. ReadAsStreamAsync returns the
            // underlying stream for StreamContent without buffering; for content that
            // has already been buffered (e.g. StringContent, ByteArrayContent) it returns
            // that in-memory buffer, which is likewise cheap to re-read.
            var stream = await part.ReadAsStreamAsync(ct);
            if (stream.CanSeek)
                return new ReplayableStreamContent(stream);

            // A non-seekable stream cannot be rewound for a retry, so buffer it once.
            return new ByteArrayContent(await part.ReadAsByteArrayAsync(ct));
        }
    }

    /// <summary>
    /// An <see cref="HttpContent"/> that serializes a seekable stream from a captured
    /// position and rewinds it afterwards, so the same caller-owned stream can be
    /// re-sent on every retry without being buffered into memory. Disposing this content
    /// only detaches it from the stream — it never disposes the caller's stream.
    /// </summary>
    private sealed class ReplayableStreamContent : HttpContent
    {
        private readonly Stream _stream;
        private readonly long _origin;

        public ReplayableStreamContent(Stream stream)
        {
            _stream = stream;
            _origin = stream.Position;
        }

        protected override async Task SerializeToStreamAsync(Stream target, TransportContext? context)
        {
            _stream.Position = _origin;
            await _stream.CopyToAsync(target);
            _stream.Position = _origin; // leave the caller's stream rewound for reuse
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _stream.Length - _origin;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            // Detach only — never dispose the caller-owned stream.
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Resolves an operation path against the configured base address.
    /// </summary>
    /// <param name="path">The operation path from the spec (starts with '/').</param>
    /// <param name="useRootServer">
    /// When true, the path-item-level <c>servers</c> override applies: the upstream spec
    /// declares the cluster-admin operations under <c>/cluster/v2/...</c> with a server URL
    /// that drops the document's <c>/v2</c> base path (<c>{schema}://{host}:{port}</c>), so
    /// the request must resolve against the bare root rather than the <c>/v2</c> base.
    /// See https://github.com/camunda/orchestration-cluster-api-csharp/issues/470.
    /// </param>
    private Uri ResolveRequestUri(string path, bool useRootServer)
    {
        var relativePath = path.TrimStart('/');
        var baseAddress = _httpClient.BaseAddress;
        if (baseAddress == null)
        {
            // No BaseAddress configured (custom HttpClient without one): keep the previous
            // relative-URI behaviour and let the inner handler resolve it.
            return new Uri(relativePath, UriKind.Relative);
        }

        if (!useRootServer)
        {
            // Relative resolution keeps the "/v2" segment of the base address.
            // Without this, "/topology" would be absolute from the host root,
            // overriding the "/v2" segment of the base address.
            return new Uri(baseAddress, relativePath);
        }

        // Root-server override: strip a single trailing "/v2" segment from the base
        // address, then append the (already "/cluster/v2/...-prefixed") path.
        // Use the path-only left part: AbsoluteUri would carry any query/fragment of a
        // caller-supplied BaseAddress (e.g. "https://host/v2/?routing=admin"), and
        // concatenating after them would fold the operation path into the query.
        // Strip only when "/v2" is a trailing *path* segment — never when it is part of
        // the authority. A host literally named "v2" ("https://v2/") has an empty path, so
        // checking the path component avoids deleting the hostname and misrouting the
        // request to "https://cluster/v2/topology".
        var rootBase = baseAddress.GetLeftPart(UriPartial.Path).TrimEnd('/');
        var basePath = baseAddress.AbsolutePath.TrimEnd('/');
        if (basePath.EndsWith("/v2", StringComparison.OrdinalIgnoreCase))
            rootBase = rootBase[..^3];
        return new Uri(rootBase + "/" + relativePath, UriKind.Absolute);
    }

    /// <summary>
    /// Formats a path or query parameter value for the request URL using culture-invariant,
    /// wire-compatible representations. Notably:
    /// <list type="bullet">
    /// <item><see cref="DateTimeOffset"/> and <see cref="DateTime"/> render as ISO 8601 (round-trip "O").</item>
    /// <item><see cref="DateOnly"/> and <see cref="TimeOnly"/> render as ISO 8601 ("O").</item>
    /// <item><see cref="bool"/> renders as lowercase <c>true</c>/<c>false</c>.</item>
    /// <item><see cref="ICamundaLongKey"/> renders its numeric <c>Value</c> invariantly.</item>
    /// <item>All other <see cref="IFormattable"/> values use <see cref="CultureInfo.InvariantCulture"/>.</item>
    /// </list>
    /// This prevents locale-dependent formatting (e.g. <c>01/06/2026</c>) from reaching the server.
    /// </summary>
    private static string FormatParam(object value) => value switch
    {
        DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("O", CultureInfo.InvariantCulture),
        TimeOnly t => t.ToString("O", CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        ICamundaLongKey k => k.Value.ToString(CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()!,
    };

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
        if (_logger.IsEnabled(LogLevel.Trace))
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
[Obsolete("Use CamundaClient.Create() instead.")]
public static class Camunda
{
    /// <summary>
    /// Create a new CamundaClient.
    /// </summary>
    [Obsolete("Use CamundaClient.Create() instead.")]
    public static CamundaClient CreateClient(CamundaOptions? options = null)
        => CamundaClient.Create(options);
}
