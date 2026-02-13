using Camunda.Orchestration.Sdk.Runtime;
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
        var worker = new JobWorker(this, config, handler, _loggerFactory, _jsonOptions);
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

        try
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown signal
        }

        await StopAllWorkersAsync(gracePeriod.Value).ConfigureAwait(false);
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
        _bp.Dispose();
        GC.SuppressFinalize(this);
    }
}
