using System.Net.Http.Headers;
using System.Reflection;

namespace Camunda.Orchestration.Sdk;

/// <summary>
/// Builds the <c>User-Agent</c> header sent on every SDK request. The SDK identifier
/// (<c>camunda-orchestration-sdk-csharp/&lt;version&gt;</c>) is always the first product so
/// gateway usage reports can attribute traffic to this SDK; any application-supplied
/// user agent is appended after it, never replacing it.
/// </summary>
internal static class SdkUserAgent
{
    public const string Name = "camunda-orchestration-sdk-csharp";

    public static string Version { get; } = ResolveVersion();

    public static string Product { get; } = $"{Name}/{Version}";

    private static string ResolveVersion()
    {
        var asm = typeof(SdkUserAgent).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            // Strip SourceLink build metadata ("+<commit sha>").
            var plus = info.IndexOf('+', StringComparison.Ordinal);
            return plus >= 0 ? info[..plus] : info;
        }
        return asm.GetName().Version?.ToString() ?? "0.0.0";
    }

    /// <summary>
    /// Set the request's <c>User-Agent</c> to the SDK product, followed by the configured
    /// application user agent and then any defaults from the caller-supplied
    /// <paramref name="clientDefaults"/> or already on the request.
    /// </summary>
    /// <remarks>
    /// An application user agent that fails <see cref="HttpHeaderValueCollection{T}.TryParseAdd"/>
    /// is dropped, never added without validation: <c>TryAddWithoutValidation</c> trusts the
    /// value, so a malformed one (for example containing CR/LF) would allow HTTP header
    /// injection into REST and OAuth requests.
    /// </remarks>
    public static void Apply(HttpRequestMessage request, string? appUserAgent, HttpHeaderValueCollection<ProductInfoHeaderValue>? clientDefaults = null)
    {
        var existing = request.Headers.UserAgent.ToList();
        var ua = request.Headers.UserAgent;
        ua.Clear();
        ua.ParseAdd(Product);
        if (!string.IsNullOrWhiteSpace(appUserAgent))
            ua.TryParseAdd(appUserAgent.Trim());
        foreach (var v in existing)
        {
            if (!IsSdkProduct(v))
                ua.Add(v);
        }
        if (clientDefaults != null)
        {
            foreach (var v in clientDefaults)
            {
                if (!IsSdkProduct(v))
                    ua.Add(v);
            }
        }
    }

    private static bool IsSdkProduct(ProductInfoHeaderValue v)
        => string.Equals(v.Product?.Name, Name, StringComparison.OrdinalIgnoreCase);
}
