using Microsoft.Extensions.Time.Testing;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Tests for backpressure management, mirroring the JS SDK's backpressure tests.
/// </summary>
public class BackpressureTests
{
    [Fact]
    public void InitialStateIsHealthy()
    {
        var bp = new BackpressureManager(new BackpressureConfig(), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, TimeProvider.System);
        var state = bp.GetState();

        Assert.Equal("healthy", state.Severity);
        Assert.Equal(0, state.Consecutive);
        Assert.Equal(16, state.PermitsMax); // BALANCED default
    }

    [Fact]
    public void RecordBackpressureIncrementsConsecutive()
    {
        var bp = new BackpressureManager(new BackpressureConfig(), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, TimeProvider.System);

        bp.RecordBackpressure();
        Assert.Equal(1, bp.GetState().Consecutive);
        Assert.Equal("soft", bp.GetState().Severity);

        bp.RecordBackpressure();
        Assert.Equal(2, bp.GetState().Consecutive);
    }

    [Fact]
    public void SevereThresholdTriggersWhenExceeded()
    {
        var bp = new BackpressureManager(
            new BackpressureConfig { SevereThreshold = 2 },
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            TimeProvider.System);

        bp.RecordBackpressure();
        bp.RecordBackpressure();
        Assert.Equal("severe", bp.GetState().Severity);
    }

    [Fact]
    public void LegacyProfileIsObserveOnly()
    {
        var bp = new BackpressureManager(
            new BackpressureConfig { ObserveOnly = true, Enabled = false },
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            TimeProvider.System);

        bp.RecordBackpressure();
        Assert.Null(bp.GetState().PermitsMax);
    }

    [Fact]
    public async Task AcquireAndReleaseWorkWithSemaphore()
    {
        var bp = new BackpressureManager(
            new BackpressureConfig { InitialMax = 2 },
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            TimeProvider.System);

        await bp.AcquireAsync();
        await bp.AcquireAsync();
        bp.Release();
        bp.Release();
    }

    // ---- Zero / negative capacity must be rejected (#479 review) ----
    // A gate constructed with a nonpositive cap can never admit a request: a zero
    // InitialMax queues every acquisition forever, and a zero Floor lets the cap
    // degrade to zero and stall. Reject the misconfiguration at construction rather
    // than wedge the gate at runtime.

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsNonPositiveInitialMax(int initialMax)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BackpressureManager(
                new BackpressureConfig { InitialMax = initialMax },
                Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
                TimeProvider.System));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsNonPositiveFloor(int floor)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BackpressureManager(
                new BackpressureConfig { Floor = floor },
                Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
                TimeProvider.System));
    }

    [Fact]
    public void Constructor_AllowsNonPositiveCaps_WhenGateDisabled()
    {
        // Observe-only / disabled managers never open the gate, so a nonpositive cap
        // is inert and must not throw.
        var bp = new BackpressureManager(
            new BackpressureConfig { Enabled = false, InitialMax = 0, Floor = 0 },
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            TimeProvider.System);

        Assert.Null(bp.GetState().PermitsMax);
    }

    // ---- Recovery must continue past the first interval (#479 review) ----
    // Severity decay and permit recovery were coupled in one block: the first healthy
    // completion after the quiet period zeroed _consecutive *and* raised the cap once,
    // so every later completion skipped the block and the cap stuck one step above the
    // floor even after the broker recovered.

    [Fact]
    public void RecordHealthy_KeepsRecoveringPermits_AcrossMultipleIntervals()
    {
        var clock = new FakeTimeProvider();
        var bp = new BackpressureManager(
            new BackpressureConfig
            {
                InitialMax = 16,
                SevereThreshold = 3,
                SevereFactor = 0.5,
                SoftFactor = 0.7,
                Floor = 1,
                RecoveryStep = 1,
                DecayQuietMs = 1000,
                RecoveryIntervalMs = 1000,
            },
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            clock);

        // Three consecutive signals shrink the cap well below the initial 16.
        bp.RecordBackpressure();
        bp.RecordBackpressure();
        bp.RecordBackpressure();
        var degraded = bp.GetState().PermitsMax;
        Assert.True(degraded < 16);

        // Cross the quiet period: the first healthy completion decays severity and
        // recovers one step.
        clock.Advance(TimeSpan.FromMilliseconds(1500));
        bp.RecordHealthy();
        var afterFirst = bp.GetState().PermitsMax;
        Assert.True(afterFirst > degraded);

        // The regression: subsequent healthy completions (one per recovery interval)
        // must keep restoring capacity instead of stopping after the first increment.
        clock.Advance(TimeSpan.FromMilliseconds(1000));
        bp.RecordHealthy();
        var afterSecond = bp.GetState().PermitsMax;
        Assert.True(afterSecond > afterFirst);

        clock.Advance(TimeSpan.FromMilliseconds(1000));
        bp.RecordHealthy();
        Assert.True(bp.GetState().PermitsMax > afterSecond);
    }

    [Fact]
    public void RecordHealthy_RecoversOnlyOncePerRecoveryInterval()
    {
        var clock = new FakeTimeProvider();
        var bp = new BackpressureManager(
            new BackpressureConfig
            {
                InitialMax = 4,
                RecoveryStep = 1,
                DecayQuietMs = 1000,
                RecoveryIntervalMs = 1000,
            },
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            clock);

        bp.RecordBackpressure(); // cap: int(4 * 0.7) = 2
        Assert.Equal(2, bp.GetState().PermitsMax);

        clock.Advance(TimeSpan.FromMilliseconds(1500));
        bp.RecordHealthy(); // decay + recover -> 3
        Assert.Equal(3, bp.GetState().PermitsMax);

        // A second healthy completion within the same recovery interval must not
        // recover again — recovery is rate-limited by the clock, not by call count.
        bp.RecordHealthy();
        Assert.Equal(3, bp.GetState().PermitsMax);
    }

    // ---- Recovery must wait for a quiet period (#479 review) ----
    // Recovery restores capacity only "if the system stays quiet" (README). A success
    // recorded immediately after a backpressure signal must not raise the cap while
    // the broker is still shedding load — that would re-admit queued requests into an
    // overloaded broker. Recovery requires both the recovery interval AND a quiet
    // period since the last signal.

    [Fact]
    public void RecordHealthy_DoesNotRecover_ImmediatelyAfterBackpressure()
    {
        var clock = new FakeTimeProvider();
        var bp = new BackpressureManager(
            new BackpressureConfig
            {
                InitialMax = 4,
                RecoveryStep = 1,
                DecayQuietMs = 2000,
                RecoveryIntervalMs = 1000,
            },
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            clock);

        bp.RecordBackpressure(); // cap: int(4 * 0.7) = 2
        Assert.Equal(2, bp.GetState().PermitsMax);

        // A success one recovery interval later but still within the quiet window must
        // NOT recover: the broker signalled pressure only a moment ago.
        clock.Advance(TimeSpan.FromMilliseconds(1000));
        bp.RecordHealthy();
        Assert.Equal(2, bp.GetState().PermitsMax);

        // Once the quiet period has also elapsed, recovery resumes.
        clock.Advance(TimeSpan.FromMilliseconds(1500)); // now 2500ms past the signal
        bp.RecordHealthy();
        Assert.Equal(3, bp.GetState().PermitsMax);
    }

    // ---- A pre-cancelled token must not be granted a permit (#479 review) ----
    // With free capacity the fast path returned a permit without observing the
    // cancellation token, whereas the previous SemaphoreSlim.WaitAsync(ct) rejected an
    // already-cancelled acquisition. InvokeWithRetryAsync would then start the
    // operation (and serialize its body) despite cancellation.

    [Fact]
    public async Task AcquireAsync_Throws_WhenTokenAlreadyCancelled_WithFreeCapacity()
    {
        var bp = new BackpressureManager(
            new BackpressureConfig { InitialMax = 4 },
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            TimeProvider.System);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Free capacity is available, yet the pre-cancelled token must still win.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => bp.AcquireAsync(cts.Token));

        // No permit may have been consumed by the cancelled acquisition.
        Assert.Equal(4, bp.GetState().PermitsMax);
    }

    [Fact]
    public async Task AcquireAsync_Throws_WhenTokenAlreadyCancelled_WhileQueued()
    {
        var bp = new BackpressureManager(
            new BackpressureConfig { InitialMax = 1 },
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            TimeProvider.System);

        await bp.AcquireAsync(); // hold the only permit so the next acquire must queue

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => bp.AcquireAsync(cts.Token));
    }

    // ---- Cancelled waiters must be removed from the queue, not retained (#479 review) ----
    // With a plain FIFO queue a cancelled waiter stayed enqueued until a release dequeued
    // and discarded it, so repeated cancelled acquisitions under a held permit grew the
    // queue without bound — retaining completed waiter tasks and their tokens after the
    // callers had finished. Cancelled nodes are now removed under _gate.

    [Fact]
    public async Task AcquireAsync_CancelledWaiters_DoNotAccumulateOrBlockLaterWaiters()
    {
        var bp = new BackpressureManager(
            new BackpressureConfig { InitialMax = 1 },
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            TimeProvider.System);

        await bp.AcquireAsync(); // hold the only permit so every later acquire must queue

        // Cancel many queued acquisitions while the permit stays held. Under the old
        // queue these stacked up as dead waiters; now each is removed as it cancels.
        for (var i = 0; i < 50; i++)
        {
            using var cts = new CancellationTokenSource();
            var queued = bp.AcquireAsync(cts.Token);
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await queued);
        }

        // A waiter that is still pending must be granted the released permit — the
        // cancelled acquisitions must not have consumed capacity or left dead weight
        // ahead of it.
        var pending = bp.AcquireAsync();
        bp.Release();
        await pending.WaitAsync(TimeSpan.FromSeconds(5));

        // Releasing the pending waiter returns the gate to full capacity: a fresh
        // acquire is granted immediately (fast path), proving no permit leaked.
        bp.Release();
        await bp.AcquireAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task AcquireAsync_DisposeDuringContention_FaultsOnlyLiveWaiters()
    {
        var bp = new BackpressureManager(
            new BackpressureConfig { InitialMax = 1 },
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            TimeProvider.System);

        await bp.AcquireAsync(); // hold the only permit

        // A cancelled waiter is removed from the queue, so Dispose must not try to fault
        // it — only the still-pending waiter is faulted.
        using var cts = new CancellationTokenSource();
        var cancelled = bp.AcquireAsync(cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await cancelled);

        var pending = bp.AcquireAsync();
        bp.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await pending);
    }

    // ---- Cancellation cleanup racing Dispose must not throw InvalidOperationException ----
    // WaitAsync(ct)'s cancellation continuation is scheduled asynchronously
    // (RunContinuationsAsynchronously), so Dispose() can drain the queue — detaching the
    // node and faulting its source — before the cancellation handler re-acquires _gate.
    // The handler then found the source not-successfully-completed and ran an
    // UNCONDITIONAL _waiters.Remove(node) on the already-detached node, throwing
    // InvalidOperationException and replacing the intended cancellation exception
    // (#480 review 5677142565). The removal must be guarded by the node still belonging
    // to the queue.

    [Fact]
    public async Task AcquireAsync_CancellationRacingDispose_ThrowsCancellationNotInvalidOperation()
    {
        // The race window is narrow, so loop to make the interleaving likely. Each
        // iteration is independent; any InvalidOperationException from the cancellation
        // path fails the test. Iteration count is a safety net, not a correctness
        // signal — the assertion is the deterministic guard.
        for (var iter = 0; iter < 2000; iter++)
        {
            var bp = new BackpressureManager(
                new BackpressureConfig { InitialMax = 1 },
                Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
                TimeProvider.System);

            await bp.AcquireAsync(); // hold the only permit so the next acquire must queue

            using var cts = new CancellationTokenSource();
            var queued = bp.AcquireAsync(cts.Token);

            // Race the waiter's cancellation against disposal of the manager.
            var cancelTask = Task.Run(() => cts.Cancel());
            var disposeTask = Task.Run(() => bp.Dispose());
            await Task.WhenAll(cancelTask, disposeTask);

            // The queued acquisition must observe either its cancellation (the caller's
            // token) or the disposal fault — but never InvalidOperationException, which
            // would prove the cancellation handler removed a node Dispose had detached.
            var ex = await Record.ExceptionAsync(async () => await queued);
            Assert.True(
                ex is OperationCanceledException or ObjectDisposedException,
                $"iter {iter}: expected cancellation/disposal but got {(ex == null ? "no exception" : ex.GetType().Name)}: {ex?.Message}");
        }
    }
}
