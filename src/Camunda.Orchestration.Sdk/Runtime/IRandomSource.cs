namespace Camunda.Orchestration.Sdk;

/// <summary>
/// Source of the randomness behind SDK runtime jitter: retry backoff, OAuth retry backoff,
/// and worker startup staggering.
///
/// <para>The randomness counterpart of <see cref="CamundaOptions.TimeProvider"/>. Pinning the
/// clock makes cadence virtual; supplying a <see cref="SeededRandomSource"/> as well makes it
/// reproducible, so a timing-dependent failure can be replayed draw for draw.</para>
///
/// <para>Implementations must be safe to call from multiple threads.</para>
/// </summary>
public interface IRandomSource
{
    /// <summary>
    /// Returns a uniformly distributed value in [0, 1).
    /// </summary>
    double NextDouble();
}
