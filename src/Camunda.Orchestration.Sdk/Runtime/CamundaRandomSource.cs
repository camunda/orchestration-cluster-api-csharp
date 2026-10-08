namespace Camunda.Orchestration.Sdk;

/// <summary>
/// The live <see cref="IRandomSource"/>: the platform's shared, unseeded generator.
///
/// <para>This is the one place in the SDK runtime allowed to read ambient randomness.
/// Production jitter stays random so that a fleet of workers does not retry or start in
/// lockstep.</para>
/// </summary>
public sealed class CamundaRandomSource : IRandomSource
{
    /// <summary>
    /// The default live randomness source.
    /// </summary>
    public static CamundaRandomSource Live { get; } = new();

    private CamundaRandomSource() { }

    /// <inheritdoc />
    public double NextDouble() => Random.Shared.NextDouble();
}
