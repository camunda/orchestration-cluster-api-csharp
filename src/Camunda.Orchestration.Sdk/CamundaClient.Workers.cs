using Microsoft.Extensions.Logging;

namespace Camunda.Orchestration.Sdk;
/// <summary>
/// Worker management methods for <see cref="CamundaClient"/>.
/// </summary>
public partial class CamundaClient : IAsyncDisposable
{
    private readonly List<JobWorker> _workers = new();

    /// <summary>
    /// Create a job worker that polls for and processes jobs of the specified type.
    ///
    /// <para>The handler receives an <see cref="ActivatedJob"/> and returns variables to
    /// auto-complete. Throw <see cref="BpmnErrorException"/> for BPMN errors,
    /// <see cref="JobFailureException"/> for explicit failures, or any other exception
    /// to auto-fail with <c>retries - 1</c>.</para>
    /// </summary>
    /// <param name="config">Worker configuration (job type, timeout, concurrency).</param>
    /// <param name="handler">
    /// Async handler that processes each job. Return output variables (or null) to complete.
    /// </param>
    /// <returns>The running <see cref="JobWorker"/> instance.</returns>
    public JobWorker CreateJobWorker(JobWorkerConfig config, JobHandler handler)
    {
        var defaults = _config.WorkerDefaults;

        // Tenant precedence: explicit JobWorkerConfig tenants > CAMUNDA_TENANT_IDS >
        // [DefaultTenantId] (injected on the activation request). An empty explicit
        // list counts as unset. Under TenantFilter.ASSIGNED the server picks the
        // tenants, so the env default must not be applied — it would collide with the
        // ASSIGNED validation in the JobWorker constructor.
        var tenantIds = config.TenantIds;
        if (tenantIds is not { Count: > 0 }
            && config.TenantId is null
            && config.TenantFilter is not TenantFilterEnum.ASSIGNED)
        {
            tenantIds = _config.TenantIds ?? tenantIds;
        }

        var merged = new JobWorkerConfig
        {
            JobType = config.JobType,
            JobTimeoutMs = config.JobTimeoutMs ?? defaults?.JobTimeoutMs,
            MaxConcurrentJobs = config.MaxConcurrentJobs ?? defaults?.MaxConcurrentJobs ?? 10,
            PollIntervalMs = config.PollIntervalMs,
            PollTimeoutMs = config.PollTimeoutMs ?? defaults?.PollTimeoutMs,
            FetchVariables = config.FetchVariables,
            WorkerName = config.WorkerName ?? defaults?.WorkerName,
            AutoStart = config.AutoStart,
            StartupJitterMaxSeconds = config.StartupJitterMaxSeconds > 0
                ? config.StartupJitterMaxSeconds
                : defaults?.StartupJitterMaxSeconds ?? 0,
            TenantIds = tenantIds,
            TenantId = config.TenantId,
            TenantFilter = config.TenantFilter,
            WithLease = config.WithLease,
        };
        var worker = new JobWorker(this, merged, handler, _loggerFactory, _jsonOptions, _timeProvider);
        _workers.Add(worker);
        return worker;
    }

    /// <summary>
    /// Create a job worker with a handler that doesn't return output variables.
    /// The job is auto-completed with no variables on success.
    /// </summary>
    public JobWorker CreateJobWorker(JobWorkerConfig config, Func<ActivatedJob, CancellationToken, Task> handler)
    {
        return CreateJobWorker(config, async (job, ct) =>
        {
            await handler(job, ct).ConfigureAwait(false);
            return null;
        });
    }

    /// <summary>
    /// Block until cancellation is requested, keeping all registered workers alive.
    /// This is the typical entry point for worker-only applications.
    ///
    /// <para>When the token is cancelled, all workers are stopped gracefully.</para>
    /// </summary>
    /// <param name="ct">Cancellation token that signals shutdown.</param>
    /// <param name="gracePeriod">
    /// Time to wait for in-flight jobs to finish during shutdown. Default: 10 seconds.
    /// </param>
    public async Task RunWorkersAsync(TimeSpan? gracePeriod = null, CancellationToken ct = default)
    {
        gracePeriod ??= TimeSpan.FromSeconds(10);

        // Keep every registered worker alive until either shutdown is signalled or a worker's
        // poll loop faults terminally (e.g. LeaseNotHonoredException). A terminal fault must
        // reach the caller; without observing worker Completion here it would die on a
        // background task while RunWorkersAsync blocked forever on the infinite delay. Both the
        // shutdown wait and the fault observer run on the linked token, so whichever loses the
        // race is cancelled by observerStop.Cancel() rather than leaking a pending delay/timer.
        using var observerStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var shutdown = ShutdownSignalAsync(observerStop.Token);
        var fault = ObserveFirstWorkerFaultAsync(observerStop.Token);

        var winner = await Task.WhenAny(shutdown, fault).ConfigureAwait(false);
        observerStop.Cancel();

        await StopAllWorkersAsync(gracePeriod.Value).ConfigureAwait(false);

        // Only a genuine fault is rethrown. Shutdown and clean/cancelled worker completions
        // return null. If shutdown won the race, StopAllWorkersAsync already drained the
        // workers (StopAsync observes and swallows the lease fault on that path).
        if (winner.IsCompletedSuccessfully && winner.Result is { } ex)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(ex);
    }

    private async Task<Exception?> ShutdownSignalAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, _timeProvider, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown signal.
        }

        return null;
    }

    /// <summary>
    /// Return the first terminal worker fault, or <c>null</c> if <paramref name="ct"/> is
    /// cancelled (shutdown) before any worker faults. A clean or cancelled worker completion
    /// is deliberately ignored: stopping one worker directly must not tear down the rest.
    /// </summary>
    private async Task<Exception?> ObserveFirstWorkerFaultAsync(CancellationToken ct)
    {
        var pending = new List<Task>();
        foreach (var worker in _workers)
        {
            if (worker.Completion is { } completion)
                pending.Add(completion);
        }

        while (pending.Count > 0 && !ct.IsCancellationRequested)
        {
            var cancelled = new TaskCompletionSource();
            using var registration = ct.Register(() => cancelled.TrySetResult());

            var finished = await Task.WhenAny(pending.Append(cancelled.Task)).ConfigureAwait(false);
            if (finished == cancelled.Task)
                return null;

            pending.Remove(finished);
            if (finished.IsFaulted)
                return finished.Exception?.InnerException ?? finished.Exception;
            // A clean or cancelled completion — a worker stopped on its own — is not a fault;
            // keep waiting on the remaining workers.
        }

        // No pending worker tasks (none started, or all exited cleanly). RunWorkersAsync must
        // still honor its wait-until-cancellation contract rather than returning here and
        // tearing everything down, so block until shutdown.
        if (!ct.IsCancellationRequested)
            await WaitForCancellationAsync(ct).ConfigureAwait(false);

        return null;
    }

    private static async Task WaitForCancellationAsync(CancellationToken ct)
    {
        var cancelled = new TaskCompletionSource();
        using var registration = ct.Register(() => cancelled.TrySetResult());
        await cancelled.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// Stop all registered workers and wait for in-flight jobs to drain.
    /// </summary>
    public async Task StopAllWorkersAsync(TimeSpan? gracePeriod = null)
    {
        gracePeriod ??= TimeSpan.FromSeconds(5);

        var tasks = new List<Task<StopResult>>(_workers.Count);
        foreach (var worker in _workers)
            tasks.Add(worker.StopAsync(gracePeriod));

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns a snapshot of all registered workers.
    /// </summary>
    public IReadOnlyList<JobWorker> GetWorkers() => _workers.AsReadOnly();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAllWorkersAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        if (_ownsHttpClient)
            _httpClient.Dispose();
        await _bp.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
