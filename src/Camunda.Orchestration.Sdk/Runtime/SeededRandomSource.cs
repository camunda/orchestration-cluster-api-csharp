using System.Globalization;

namespace Camunda.Orchestration.Sdk;

/// <summary>
/// A deterministic <see cref="IRandomSource"/> for tests: the same seed yields the same
/// sequence of draws on every run, and in every Camunda SDK.
///
/// <para>The generator is SplitMix64, taking the top 53 bits of each output as a double. It
/// is specified by the cross-SDK contract (camunda/sdk-infra#50) rather than borrowed from
/// the platform, so a seed reproduces the same jitter regardless of runtime version or
/// language.</para>
///
/// <para>Draws are thread-safe. Reproducibility holds for draws made in a fixed order; if
/// several workers draw concurrently, which of them receives which value is up to the
/// scheduler.</para>
/// </summary>
public sealed class SeededRandomSource : IRandomSource
{
    /// <summary>
    /// Environment variable read by <see cref="FromEnvironment"/> to replay a seed.
    /// </summary>
    public const string SeedEnvironmentVariable = "CAMUNDA_TEST_SEED";

    private const ulong Gamma = 0x9E3779B97F4A7C15;
    private const double Unit = 1.0 / (1UL << 53);

    private ulong _state;

    /// <summary>
    /// Creates a source that replays the sequence for <paramref name="seed"/>.
    /// </summary>
    public SeededRandomSource(ulong seed)
    {
        Seed = seed;
        _state = seed;
    }

    /// <summary>
    /// The seed this source was created with. Report it on failure to make the run replayable.
    /// </summary>
    public ulong Seed { get; }

    /// <summary>
    /// Creates a source seeded from <c>CAMUNDA_TEST_SEED</c> when it is set, otherwise from a
    /// fresh random seed. Report <see cref="Seed"/> on failure, and set the variable to it to
    /// replay the run.
    /// </summary>
    /// <exception cref="InvalidOperationException">The variable is set but is not an unsigned 64-bit integer.</exception>
    public static SeededRandomSource FromEnvironment()
    {
        var raw = Environment.GetEnvironmentVariable(SeedEnvironmentVariable);
        if (raw is null)
            return new SeededRandomSource((ulong)(CamundaRandomSource.Live.NextDouble() * (1UL << 53)));

        if (!ulong.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var seed))
            throw new InvalidOperationException(
                $"{SeedEnvironmentVariable}='{raw}' is not an unsigned 64-bit decimal integer.");

        return new SeededRandomSource(seed);
    }

    /// <inheritdoc />
    public double NextDouble()
    {
        unchecked
        {
            var z = Interlocked.Add(ref _state, Gamma);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
            z ^= z >> 31;
            return (z >> 11) * Unit;
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"SeededRandomSource(seed={Seed}; replay with {SeedEnvironmentVariable}={Seed})";
}
