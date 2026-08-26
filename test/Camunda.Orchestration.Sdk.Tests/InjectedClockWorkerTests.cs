using System.Net;
using System.Text;
using Microsoft.Extensions.Time.Testing;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// End-to-end checks that a <see cref="JobWorker"/>'s cadence and its handler-facing
/// time both resolve through the client's injected clock.
/// </summary>
public class InjectedClockWorkerTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private const string OneJob = """
        {"jobs":[{
            "type":"clock-test","processDefinitionId":"p","processDefinitionVersion":1,
            "elementId":"e","customHeaders":{},"worker":"w","retries":3,
            "deadline":1700000000000,"variables":{},"tenantId":"<default>",
            "jobKey":"123456","processInstanceKey":"789012","processDefinitionKey":"345678",
            "elementInstanceKey":"901234","kind":"BPMN_ELEMENT","listenerEventType":"UNSPECIFIED"
        }]}
        """;

    private static CamundaClient CreateClient(TimeProvider clock, HttpMessageHandler handler) =>
        CamundaClient.Create(new CamundaOptions
        {
            Config = new Dictionary<string, string>
            {
                ["CAMUNDA_REST_ADDRESS"] = "http://localhost:8080/v2",
                ["CAMUNDA_AUTH_STRATEGY"] = "NONE",
            },
            HttpMessageHandler = handler,
            TimeProvider = clock,
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

    /// <summary>
    /// Ruling 3: a 25h jump across a 5s poll loop must not replay the ~18,000 polls that
    /// fell in the gap. It should wake the loop exactly once.
    /// </summary>
    [Fact]
    public async Task PollLoopDoesNotReplayMissedPollsAcrossALargeTimeJump()
    {
        var clock = new InstrumentedFakeTimeProvider(DateTimeOffset.UnixEpoch);
        var polls = 0;

        var handler = new StubHandler(_ =>
        {
            Interlocked.Increment(ref polls);
            return Json("""{"jobs":[]}""");
        });

        using var client = CreateClient(clock, handler);
        await using var worker = client.CreateJobWorker(
            new JobWorkerConfig { JobType = "clock-test", JobTimeoutMs = 60_000, PollIntervalMs = 5_000 },
            (job, ct) => Task.FromResult<object?>(null));

        // Wait for the loop to be parked on the clock, not merely to have polled: it
        // registers its delay only after handling the response, so advancing on the poll
        // count would race and could fire nothing.
        Assert.True(await WaitFor(() => clock.TimersCreated >= 1), "worker never parked on the clock");
        var before = Volatile.Read(ref polls);

        clock.Advance(TimeSpan.FromHours(25));

        // The loop has re-parked, so every poll the jump was going to cause has happened.
        Assert.True(await WaitFor(() => clock.TimersCreated >= 2), "advance did not wake the poll loop");

        Assert.Equal(1, Volatile.Read(ref polls) - before);
    }

    /// <summary>
    /// The worker's poll cadence is virtual: with no clock advance, no further polls occur
    /// no matter how much real time passes.
    /// </summary>
    [Fact]
    public async Task PollLoopMakesNoProgressWhileTheClockIsHeld()
    {
        var clock = new InstrumentedFakeTimeProvider(DateTimeOffset.UnixEpoch);
        var polls = 0;

        var handler = new StubHandler(_ =>
        {
            Interlocked.Increment(ref polls);
            return Json("""{"jobs":[]}""");
        });

        using var client = CreateClient(clock, handler);
        await using var worker = client.CreateJobWorker(
            new JobWorkerConfig { JobType = "clock-test", JobTimeoutMs = 60_000, PollIntervalMs = 1 },
            (job, ct) => Task.FromResult<object?>(null));

        Assert.True(await WaitFor(() => clock.TimersCreated >= 1), "worker never parked on the clock");
        var settled = Volatile.Read(ref polls);
        var timers = clock.TimersCreated;

        // A 1ms poll interval would produce hundreds of polls in this window on a real clock.
        await Task.Delay(300);

        Assert.Equal(settled, Volatile.Read(ref polls));
        Assert.Equal(timers, clock.TimersCreated);
    }

    /// <summary>
    /// Handler-facing time: the job carries the client's clock, so in-handler waits are
    /// virtual too and handlers never need an ambient primitive.
    /// </summary>
    [Fact]
    public async Task HandlerReceivesTheInjectedClock()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var served = 0;
        TimeProvider? observed = null;
        DateTimeOffset? observedNow = null;

        var handler = new StubHandler(req =>
        {
            var uri = req.RequestUri!.ToString();
            if (uri.Contains("activation", StringComparison.Ordinal))
                return Json(Interlocked.Increment(ref served) == 1 ? OneJob : """{"jobs":[]}""");
            return Json("{}");
        });

        using var client = CreateClient(clock, handler);
        await using var worker = client.CreateJobWorker(
            new JobWorkerConfig { JobType = "clock-test", JobTimeoutMs = 60_000, PollIntervalMs = 5_000 },
            (job, ct) =>
            {
                observed = job.Clock;
                observedNow = job.Clock.GetUtcNow();
                return Task.FromResult<object?>(null);
            });

        Assert.True(await WaitFor(() => observed != null), "handler never ran");

        Assert.Same(clock, observed);
        Assert.Equal(DateTimeOffset.UnixEpoch, observedNow);
    }
}
