namespace Camunda.Client.Runtime;

/// <summary>
/// Implemented by request body types that have an optional tenantId property.
/// The SDK uses this to inject the configured default tenant ID when the caller
/// does not supply one explicitly.
/// </summary>
public interface ITenantIdSettable
{
    /// <summary>
    /// Sets the tenant ID if it has not already been set by the caller.
    /// </summary>
    void SetDefaultTenantId(string tenantId);
}
