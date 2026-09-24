namespace Camunda.Orchestration.Sdk;

/// <summary>
/// Runtime enforcement of the dependent-presence couplings the specification declares with
/// <c>x-present-when</c> (see <c>PresentWhen.Generated.cs</c>).
///
/// <para>C# cannot express, in the type system, that a response field is present only when a
/// request set a runtime flag, so the coupling is enforced here at the activation boundary.
/// The generated table is the ground truth; the static initializer refuses to load if the
/// table contains a coupling the runtime does not enforce, so a new <c>x-present-when</c>
/// marker fails fast rather than shipping a coupling the worker silently ignores.</para>
/// </summary>
internal static class PresentWhen
{
    /// <summary>
    /// The one coupling this runtime enforces: <c>ActivatedJobResult.jobLeaseToken</c> is
    /// present only when an activation set the lease flag.
    /// </summary>
    internal const string LeaseCouplingKey = "ActivatedJobResult.jobLeaseToken";

    /// <summary>
    /// Couplings the runtime actively enforces, by <c>Schema.field</c> key. This must cover
    /// every row of <see cref="PresentWhenCouplings.All"/>; the static initializer asserts it,
    /// so a generated coupling with no enforcement cannot ship.
    /// </summary>
    internal static readonly string[] EnforcedCouplings = { LeaseCouplingKey };

    static PresentWhen()
    {
        var enforced = new HashSet<string>(EnforcedCouplings, StringComparer.Ordinal);
        var unenforced = PresentWhenCouplings.All
            .Select(CouplingKey)
            .Where(key => !enforced.Contains(key))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (unenforced.Length > 0)
        {
            throw new InvalidOperationException(
                $"x-present-when coupling(s) [{string.Join(", ", unenforced)}] are declared in the "
                + "generated table but the runtime enforces none of them. Wire enforcement here and "
                + "add the key to EnforcedCouplings rather than shipping a coupling the worker ignores.");
        }
    }

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
