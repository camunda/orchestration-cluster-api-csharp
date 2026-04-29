namespace Camunda.Orchestration.Sdk;

/// <summary>
/// Implemented by request body types that have an optional <c>tenantIds</c>
/// array property (e.g. <see cref="JobActivationRequest"/>). The SDK uses
/// this to inject <c>[DefaultTenantId]</c> when the caller does not supply
/// a tenant list explicitly. Mirrors <see cref="ITenantIdSettable"/> for
/// the plural array shape.
/// </summary>
public interface ITenantIdsSettable
{
    /// <summary>
    /// Sets the tenant ID list to <c>[tenantId]</c> if it has not already
    /// been set (or is empty) by the caller.
    /// </summary>
    void SetDefaultTenantIds(string tenantId);
}
