using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Camunda.Orchestration.Sdk.Runtime;

/// <summary>
/// Options for constructing a <see cref="CamundaClient"/>.
/// Mirrors the JS SDK's CamundaOptions with idiomatic C# conventions.
/// </summary>
public sealed class CamundaOptions
{
    /// <summary>
    /// Strongly typed env-style overrides (CAMUNDA_* keys).
    /// </summary>
    public Dictionary<string, string>? Config { get; set; }

    /// <summary>
    /// An <see cref="IConfiguration"/> section (typically <c>configuration.GetSection("Camunda")</c>)
    /// to bind settings from <c>appsettings.json</c> or any other configuration provider.
    /// <para>
    /// Keys use PascalCase property names (e.g. <c>RestAddress</c>, <c>Auth:Strategy</c>)
    /// and are mapped to the canonical <c>CAMUNDA_*</c> env-var names internally.
    /// </para>
    /// <para>
    /// Precedence (highest wins): <see cref="Config"/> &gt; <see cref="Configuration"/> &gt; environment variables &gt; defaults.
    /// </para>
    /// </summary>
    public IConfiguration? Configuration { get; set; }

    /// <summary>
    /// Custom HttpClient factory. If not provided, a default HttpClient is created.
    /// </summary>
    public HttpClient? HttpClient { get; set; }

    /// <summary>
    /// Custom HttpMessageHandler for the internal HttpClient (ignored if HttpClient is set).
    /// Useful for tests (e.g., MockHttpMessageHandler).
    /// </summary>
    public HttpMessageHandler? HttpMessageHandler { get; set; }

    /// <summary>
    /// Provide a custom env map (mainly for tests). Defaults to Environment.GetEnvironmentVariable.
    /// </summary>
    public Dictionary<string, string?>? Env { get; set; }

    /// <summary>
    /// Logger factory for SDK logging.
    /// </summary>
    public ILoggerFactory? LoggerFactory { get; set; }

    /// <summary>
    /// If true (default), non-2xx HTTP responses throw instead of returning an error object.
    /// </summary>
    public bool ThrowOnError { get; set; } = true;
}
