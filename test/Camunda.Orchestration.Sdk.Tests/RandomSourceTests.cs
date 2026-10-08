using System.Net;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using Xunit.Abstractions;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// The seeded generator itself (camunda/sdk-infra#50): it must reproduce the cross-SDK
/// SplitMix64 sequence exactly, or the same seed would mean different jitter per SDK.
/// </summary>
public class SeededRandomSourceTests(ITestOutputHelper output)
{
    [Fact]
    public void MatchesThePublishedSplitMix64Reference()
    {
        // SplitMix64 with seed 0 emits 0xE220A8397B1DCDAF first (Vigna's reference implementation).
        const ulong reference = 0xE220A8397B1DCDAF;

        Assert.Equal((reference >> 11) * (1.0 / (1UL << 53)), new SeededRandomSource(0).NextDouble());
    }

    [Fact]
    public void MatchesTheCrossSdkConformanceVector()
    {
        var random = new SeededRandomSource(42);

        double[] expected =
        [
            0.7415648787718233,
            0.1599103928769201,
            0.27860113025513866,
            0.34419071652363753,
            0.03803016854024621,
        ];

        Assert.Equal(expected, Enumerable.Range(0, expected.Length).Select(_ => random.NextDouble()));
    }

    [Fact]
    public void TheSameSeedReplaysTheSameSequence()
    {
        var random = SeededRandomSource.FromEnvironment();
        output.WriteLine(random.ToString());
        var replay = new SeededRandomSource(random.Seed);

        for (var i = 0; i < 1_000; i++)
            Assert.Equal(random.NextDouble(), replay.NextDouble());
    }

    [Fact]
    public void DrawsStayInTheUnitInterval()
    {
        var random = SeededRandomSource.FromEnvironment();
        output.WriteLine(random.ToString());

        for (var i = 0; i < 100_000; i++)
        {
            var u = random.NextDouble();
            Assert.True(u is >= 0.0 and < 1.0, $"draw {i} = {u} is outside [0, 1) ({random})");
        }
    }

    /// <summary>
    /// Concurrent draws must consume the sequence without losing or repeating a step, so
    /// the multiset of values matches a sequential replay exactly.
    /// </summary>
    [Fact]
    public async Task ConcurrentDrawsConsumeTheSequenceExactlyOnce()
    {
        var random = SeededRandomSource.FromEnvironment();
        output.WriteLine(random.ToString());
        const int threads = 8, perThread = 10_000;

        var drawn = await Task.WhenAll(Enumerable.Range(0, threads).Select(_ => Task.Run(() =>
        {
            var values = new double[perThread];
            for (var i = 0; i < perThread; i++)
                values[i] = random.NextDouble();
            return values;
        })));

        var replay = new SeededRandomSource(random.Seed);
        var sequential = Enumerable.Range(0, threads * perThread).Select(_ => replay.NextDouble()).Order();

        Assert.Equal(sequential, drawn.SelectMany(v => v).Order());
    }

    [Fact]
    public void ToStringReportsTheSeedAndHowToReplayIt()
    {
        Assert.Equal(
            "SeededRandomSource(seed=12345; replay with CAMUNDA_TEST_SEED=12345)",
            new SeededRandomSource(12345).ToString());
    }
}

[CollectionDefinition(nameof(SeedEnvironmentSerial), DisableParallelization = true)]
public sealed class SeedEnvironmentSerial;

/// <summary>
/// <see cref="SeededRandomSource.FromEnvironment"/> reads process-wide state, so these run
/// outside the parallel test pool.
/// </summary>
[Collection(nameof(SeedEnvironmentSerial))]
public class SeededRandomSourceEnvironmentTests
{
    private static void WithSeedVariable(string? value, Action body)
    {
        var previous = Environment.GetEnvironmentVariable(SeededRandomSource.SeedEnvironmentVariable);
        Environment.SetEnvironmentVariable(SeededRandomSource.SeedEnvironmentVariable, value);
        try
        { body(); }
        finally
        { Environment.SetEnvironmentVariable(SeededRandomSource.SeedEnvironmentVariable, previous); }
    }

    [Fact]
    public void ReplaysTheSeedFromTheEnvironment()
    {
        WithSeedVariable("42", () =>
        {
            var random = SeededRandomSource.FromEnvironment();

            Assert.Equal(42UL, random.Seed);
            Assert.Equal(0.7415648787718233, random.NextDouble());
        });
    }

    [Fact]
    public void AcceptsTheFullUnsigned64BitRange()
    {
        WithSeedVariable(ulong.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            () => Assert.Equal(ulong.MaxValue, SeededRandomSource.FromEnvironment().Seed));
    }

    [Theory]
    [InlineData("not-a-seed")]
    [InlineData("-1")]
    [InlineData("18446744073709551616")]
    [InlineData("0x2A")]
    [InlineData(" ")]
    [InlineData(" 42")]
    [InlineData("42\n")]
    public void RejectsAMalformedSeedRatherThanSilentlyPickingAnother(string raw)
    {
        WithSeedVariable(raw, () =>
        {
            var ex = Assert.Throws<InvalidOperationException>(SeededRandomSource.FromEnvironment);
            Assert.Contains(SeededRandomSource.SeedEnvironmentVariable, ex.Message);
        });
    }
}

/// <summary>
/// End-to-end: the client's jitter is drawn from <see cref="CamundaOptions.RandomSource"/>,
/// so with a pinned clock and a seed every delay is exact rather than a range.
/// </summary>
public class InjectedRandomSourceTests(ITestOutputHelper output)
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }

    private static CamundaClient CreateClient(TimeProvider clock, IRandomSource random, HttpMessageHandler? handler = null) =>
        CamundaClient.Create(new CamundaOptions
        {
            Config = new Dictionary<string, string>
            {
                ["CAMUNDA_REST_ADDRESS"] = "http://localhost:8080/v2",
                ["CAMUNDA_AUTH_STRATEGY"] = "NONE",
                ["CAMUNDA_SDK_HTTP_RETRY_BASE_DELAY_MS"] = "10000",
                ["CAMUNDA_SDK_HTTP_RETRY_MAX_DELAY_MS"] = "60000",
            },
            HttpMessageHandler = handler,
            TimeProvider = clock,
            RandomSource = random,
        });

    /// <summary>Spin on a real timer until <paramref name="condition"/> holds, or give up.</summary>
    private static async Task<bool> WaitFor(Func<bool> condition, int timeoutMs = 5_000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
                return true;
            await Task.Delay(5);
        }
        return condition();
    }

    private static Task<int> FailOnceThenSucceed(Func<int> attempts) => attempts() == 1
        ? throw new HttpRequestException("unavailable", null, HttpStatusCode.ServiceUnavailable)
        : Task.FromResult(42);

    /// <summary>
    /// Asserts the retry was scheduled for exactly <paramref name="delayMs"/> and fires on it.
    /// </summary>
    private static async Task AssertRetryFiresAtExactly(int delayMs, InstrumentedFakeTimeProvider clock, Task<int> pending, Func<int> attempts, string context)
    {
        await clock.WaitForTimersAsync(1);
        Assert.True(
            clock.DueTimes.SequenceEqual([TimeSpan.FromMilliseconds(delayMs)]),
            $"expected one {delayMs}ms retry timer, saw [{string.Join(", ", clock.DueTimes)}] ({context})");
        Assert.Equal(1, attempts());

        clock.Advance(TimeSpan.FromMilliseconds(delayMs));
        Assert.Equal(42, await pending.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(2, attempts());
    }

    [Fact]
    public async Task HttpRetryBackoffDrawsFromTheInjectedSource()
    {
        var clock = new InstrumentedFakeTimeProvider(DateTimeOffset.UnixEpoch);
        var random = new SeededRandomSource(42);
        using var client = CreateClient(clock, random);
        var attempts = 0;

        var pending = client.InvokeWithRetryAsync(
            () => FailOnceThenSucceed(() => Interlocked.Increment(ref attempts)), "test");

        // Seed 42 draws 0.7415... first: a 10s backoff plus (0.7415 - 0.5) * 20% = +483ms.
        await AssertRetryFiresAtExactly(10_483, clock, pending, () => Volatile.Read(ref attempts), random.ToString());
    }

    /// <summary>
    /// The same property for an arbitrary seed: the delay stays within the documented ±10%
    /// and is exactly the one a replay of the seed predicts. A failure reports the seed.
    /// </summary>
    [Fact]
    public async Task HttpRetryBackoffIsReplayableForAnySeed()
    {
        var clock = new InstrumentedFakeTimeProvider(DateTimeOffset.UnixEpoch);
        var random = SeededRandomSource.FromEnvironment();
        output.WriteLine(random.ToString());
        using var client = CreateClient(clock, random);
        var attempts = 0;

        var predicted = 10_000 + (int)(10_000 * 0.2 * (new SeededRandomSource(random.Seed).NextDouble() - 0.5));
        Assert.InRange(predicted, 9_000, 11_000);

        var pending = client.InvokeWithRetryAsync(
            () => FailOnceThenSucceed(() => Interlocked.Increment(ref attempts)), "test");

        await AssertRetryFiresAtExactly(predicted, clock, pending, () => Volatile.Read(ref attempts), random.ToString());
    }

    /// <summary>
    /// Startup jitter is drawn when each worker starts, in creation order, so with a seed
    /// each worker's first poll lands at an exact, predictable instant.
    /// </summary>
    [Fact]
    public async Task WorkerStartupJitterDrawsFromTheInjectedSourceInCreationOrder()
    {
        var clock = new InstrumentedFakeTimeProvider(DateTimeOffset.UnixEpoch);
        var random = new SeededRandomSource(42);
        var polls = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();

        var handler = new StubHandler(req =>
        {
            var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            polls.AddOrUpdate(body.Contains("\"first\"") ? "first" : "second", 1, (_, n) => n + 1);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"jobs":[]}""", Encoding.UTF8, "application/json"),
            };
        });

        using var client = CreateClient(clock, random, handler);
        JobWorkerConfig Config(string type) => new()
        {
            JobType = type,
            JobTimeoutMs = 60_000,
            PollIntervalMs = 600_000,
            StartupJitterMaxSeconds = 10,
        };
        await using var first = client.CreateJobWorker(Config("first"), (_, _) => Task.FromResult<object?>(null));
        await using var second = client.CreateJobWorker(Config("second"), (_, _) => Task.FromResult<object?>(null));
        int Polls(string type) => polls.GetValueOrDefault(type);

        // Draws 0.7415... then 0.1599... of a 10s maximum: 7415ms, then 1599ms.
        await clock.WaitForTimersAsync(2);
        Assert.True(
            clock.DueTimes.Take(2).SequenceEqual([TimeSpan.FromMilliseconds(7_415), TimeSpan.FromMilliseconds(1_599)]),
            $"expected startup timers [7415ms, 1599ms], saw [{string.Join(", ", clock.DueTimes)}] ({random})");

        clock.Advance(TimeSpan.FromMilliseconds(1_599));
        Assert.True(await WaitFor(() => Polls("second") == 1), $"second worker did not poll at 1599ms ({random})");

        clock.Advance(TimeSpan.FromMilliseconds(7_415 - 1_599));
        Assert.True(await WaitFor(() => Polls("first") == 1), $"first worker did not poll at 7415ms ({random})");
        Assert.Equal(1, Polls("second"));
    }
}
