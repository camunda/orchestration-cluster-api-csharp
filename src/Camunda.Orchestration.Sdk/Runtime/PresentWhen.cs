namespace Camunda.Orchestration.Sdk;

/// <summary>
/// Runtime enforcement of the dependent-presence couplings the specification declares with
/// <c>x-present-when</c> (see <c>PresentWhen.Generated.cs</c>).
///
/// <para>C# cannot express, in the type system, that a response field is present only when a
/// request set a runtime flag, so the coupling is enforced here at the activation boundary.
/// The generated table is the ground truth; the tests assert the runtime still matches it, so
/// an upstream change to the markers fails the build rather than drifting silently.</para>
/// </summary>
internal static class PresentWhen
{
    /// <summary>
    /// The one coupling this runtime enforces: <c>ActivatedJobResult.jobLeaseToken</c> is
    /// present only when an activation set the lease flag.
    /// </summary>
    internal const string LeaseCouplingKey = "ActivatedJobResult.jobLeaseToken";

    /// <summary>
    /// Couplings the runtime actively enforces, by <c>Schema.field</c> key. A coupling in
    /// <see cref="PresentWhenCouplings.All"/> absent here is one the specification declares
    /// and the worker silently ignores; the guard tests make that visible.
    /// </summary>
    internal static readonly string[] EnforcedCouplings = { LeaseCouplingKey };

    internal static string CouplingKey(PresentWhenCoupling c) => $"{c.ResponseSchema}.{c.ResponseField}";

    /// <summary>
    /// The request flag governing the lease coupling, read from the generated table so a
    /// rename upstream is reflected here rather than hardcoded.
    /// </summary>
    internal static string LeaseRequestFlag()
    {
        foreach (var c in PresentWhenCouplings.All)
        {
            if (CouplingKey(c) == LeaseCouplingKey)
                return c.RequestFlag;
        }

        return "withLease";
    }

    /// <summary>
    /// Enforce the lease coupling for a single activated job. <paramref name="requested"/>
    /// reports whether the activation set the lease flag; <paramref name="token"/> is the
    /// lease token the server returned. A lease that was asked for but not returned is
    /// rejected, because every fenced command would otherwise go out unfenced.
    /// </summary>
    internal static void RequireLeasePresence(bool requested, string jobKey, string? token)
    {
        if (!requested || !string.IsNullOrEmpty(token))
            return;

        throw new LeaseNotHonoredException(jobKey, LeaseRequestFlag());
    }
}
