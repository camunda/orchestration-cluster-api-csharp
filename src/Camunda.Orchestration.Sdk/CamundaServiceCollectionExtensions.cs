using Camunda.Orchestration.Sdk.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Camunda.Orchestration.Sdk;

/// <summary>
/// Extension methods for registering <see cref="CamundaClient"/> in an <see cref="IServiceCollection"/>.
/// </summary>
public static class CamundaServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="CamundaClient"/> using zero-config (environment variables only).
    /// </summary>
    public static IServiceCollection AddCamundaClient(this IServiceCollection services)
    {
        services.AddSingleton(sp =>
        {
            var options = new CamundaOptions
            {
                LoggerFactory = sp.GetService<ILoggerFactory>(),
            };
            return new CamundaClient(options);
        });
        return services;
    }

    /// <summary>
    /// Registers a singleton <see cref="CamundaClient"/> using an <see cref="IConfiguration"/> section.
    /// <para>
    /// Typically called as <c>services.AddCamundaClient(configuration.GetSection("Camunda"))</c>.
    /// PascalCase keys in the section are mapped to canonical <c>CAMUNDA_*</c> env-var names internally.
    /// Environment variables still apply as a base layer; section values override them.
    /// </para>
    /// </summary>
    public static IServiceCollection AddCamundaClient(
        this IServiceCollection services,
        IConfiguration configurationSection)
    {
        services.AddSingleton(sp =>
        {
            var options = new CamundaOptions
            {
                Configuration = configurationSection,
                LoggerFactory = sp.GetService<ILoggerFactory>(),
            };
            return new CamundaClient(options);
        });
        return services;
    }

    /// <summary>
    /// Registers a singleton <see cref="CamundaClient"/> with an options callback for full control.
    /// </summary>
    public static IServiceCollection AddCamundaClient(
        this IServiceCollection services,
        Action<CamundaOptions> configure)
    {
        services.AddSingleton(sp =>
        {
            var options = new CamundaOptions();
            configure(options);
            // Use the DI logger factory if not explicitly set
            options.LoggerFactory ??= sp.GetService<ILoggerFactory>();
            return new CamundaClient(options);
        });
        return services;
    }
}
