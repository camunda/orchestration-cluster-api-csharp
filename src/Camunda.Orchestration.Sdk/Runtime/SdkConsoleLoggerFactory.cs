using Microsoft.Extensions.Logging;

namespace Camunda.Orchestration.Sdk.Runtime;

/// <summary>
/// Minimal console logger used as the default when no <see cref="ILoggerFactory"/> is injected.
/// Respects <c>CAMUNDA_SDK_LOG_LEVEL</c> for minimum log level filtering.
/// Output format mirrors the JS SDK: <c>[camunda-sdk][level][category] message</c>.
/// </summary>
internal sealed class SdkConsoleLoggerFactory : ILoggerFactory
{
    private readonly LogLevel _minLevel;

    public SdkConsoleLoggerFactory(LogLevel minLevel)
    {
        _minLevel = minLevel;
    }

    public ILogger CreateLogger(string categoryName)
    {
        // Shorten "Camunda.Orchestration.Sdk.CamundaClient" → "CamundaClient"
        var shortName = categoryName;
        var lastDot = categoryName.LastIndexOf('.');
        if (lastDot >= 0 && lastDot < categoryName.Length - 1)
            shortName = categoryName[(lastDot + 1)..];

        return new SdkConsoleLogger(shortName, _minLevel);
    }

    public void AddProvider(ILoggerProvider provider) { }
    public void Dispose() { }
}

internal sealed class SdkConsoleLogger : ILogger
{
    private readonly string _category;
    private readonly LogLevel _minLevel;

    public SdkConsoleLogger(string category, LogLevel minLevel)
    {
        _category = category;
        _minLevel = minLevel;
    }

    public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLevel && logLevel != LogLevel.None;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var levelTag = logLevel switch
        {
            LogLevel.Trace => "trace",
            LogLevel.Debug => "debug",
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "error",
            LogLevel.Critical => "error",
            _ => "info",
        };

        var message = formatter(state, exception);
        var line = $"[camunda-sdk][{levelTag}][{_category}] {message}";

        if (logLevel >= LogLevel.Error)
            Console.Error.WriteLine(line);
        else
            Console.WriteLine(line);

        if (exception != null)
        {
            var exLine = $"[camunda-sdk][{levelTag}][{_category}] {exception}";
            Console.Error.WriteLine(exLine);
        }
    }

    /// <summary>
    /// Map <c>CAMUNDA_SDK_LOG_LEVEL</c> string values to <see cref="LogLevel"/>.
    /// </summary>
    public static LogLevel ParseSdkLogLevel(string? level)
    {
        return level?.Trim().ToLowerInvariant() switch
        {
            "silent" => LogLevel.None,
            "error" => LogLevel.Error,
            "warn" => LogLevel.Warning,
            "info" => LogLevel.Information,
            "debug" => LogLevel.Debug,
            "trace" or "silly" => LogLevel.Trace,
            _ => LogLevel.Error,
        };
    }
}
