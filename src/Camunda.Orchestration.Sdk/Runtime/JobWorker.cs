using System.Text.Json;
using Camunda.Orchestration.Sdk.Api;
using Microsoft.Extensions.Logging;

namespace Camunda.Orchestration.Sdk.Runtime;

/// <summary>
/// Delegate for job handler functions. Return the output variables to complete the
/// job with, or <c>null</c> to complete with no output variables.
///
/// <para>To signal a BPMN error, throw <see cref="BpmnErrorException"/>.</para>
/// <para>To explicitly fail a job with custom retries, throw <see cref="JobFailureException"/>.</para>
/// <para>Any other unhandled exception auto-fails the job with <c>retries - 1</c>.</para>
/// </summary>
public delegate Task<object?> JobHandler(ActivatedJob job, CancellationToken ct);

/// <summary>
/// Configuration for a <see cref="JobWorker"/>.
/// </summary>
public sealed class JobWorkerConfig
{
    /// <summary>
    /// The BPMN job type to subscribe to (e.g. <c>"payment-service"</c>).
    /// </summary>
    public required string JobType { get; init; }

    /// <summary>
    /// How long (in ms) the job is reserved for this worker before the broker
    /// makes it available to other workers.
    /// </summary>
    public required long JobTimeoutMs { get; init; }

    /// <summary>
    /// Maximum number of jobs that may be in-flight (activated and being handled)
    /// concurrently by this worker. Controls how many jobs are requested per poll
    /// and how many handler tasks run in parallel.
    ///
    /// <para>
    /// For I/O-bound handlers (HTTP calls, database queries), higher values (32–128)
    /// improve throughput because async handlers release threads during awaits.
    /// </para>
    /// <para>
    /// For CPU-bound handlers, set to <see cref="Environment.ProcessorCount"/> or lower
    /// to avoid over-subscribing the thread pool.
    /// </para>
    /// <para>Set to <c>1</c> for sequential (single-job-at-a-time) processing.</para>
    /// </summary>
    public int MaxConcurrentJobs { get; init; } = 10;

    /// <summary>
    /// Delay (in ms) between poll cycles when no jobs are available or when at capacity.
    /// Default: 500 ms.
    /// </summary>
    public int PollIntervalMs { get; init; } = 500;

    /// <summary>
    /// Long-poll timeout (in ms) sent to the broker. The broker holds the activation
    /// request open until jobs are available or this timeout elapses.
    /// <c>null</c> or <c>0</c> = broker default; negative = long polling disabled.
    /// </summary>
    public long? PollTimeoutMs { get; init; }

    /// <summary>
    /// Variable names to fetch from the process instance scope. <c>null</c> = fetch all.
    /// </summary>
    public List<string>? FetchVariables { get; init; }

    /// <summary>
    /// Worker name sent to the broker for logging and diagnostics.
    /// Auto-generated if not set.
    /// </summary>
    public string? WorkerName { get; init; }

    /// <summary>
    /// Whether to start polling immediately on creation. Default: <c>true</c>.
    /// </summary>
    public bool AutoStart { get; init; } = true;
}

/// <summary>
/// An activated job received from the Camunda broker, with typed variable access.
/// This is what job handler functions receive.
/// </summary>
public sealed class ActivatedJob
{
    private readonly ActivatedJobResult _raw;

    internal ActivatedJob(ActivatedJobResult raw)
    {
        _raw = raw;
    }

    /// <summary>The job type (matches the BPMN task definition type).</summary>
    public string Type => _raw.Type;

    /// <summary>The BPMN process ID of the job's process definition.</summary>
    public ProcessDefinitionId ProcessDefinitionId => _raw.ProcessDefinitionId;

    /// <summary>The version of the job's process definition.</summary>
    public int ProcessDefinitionVersion => _raw.ProcessDefinitionVersion;

    /// <summary>The associated task element ID.</summary>
    public ElementId ElementId => _raw.ElementId;

    /// <summary>Raw custom headers (typically a <see cref="JsonElement"/> at runtime).</summary>
    public object CustomHeaders => _raw.CustomHeaders;

    /// <summary>The name of the worker that activated this job.</summary>
    public string Worker => _raw.Worker;

    /// <summary>Retries remaining for this job.</summary>
    public int Retries => _raw.Retries;

    /// <summary>UNIX epoch timestamp (ms) when the job lock expires.</summary>
    public long Deadline => _raw.Deadline;

    /// <summary>Raw variables (typically a <see cref="JsonElement"/> at runtime).</summary>
    public object Variables => _raw.Variables;

    /// <summary>The tenant that owns this job.</summary>
    public TenantId TenantId => _raw.TenantId;

    /// <summary>Unique identifier for this job.</summary>
    public JobKey JobKey => _raw.JobKey;

    /// <summary>The process instance this job belongs to.</summary>
    public ProcessInstanceKey ProcessInstanceKey => _raw.ProcessInstanceKey;

    /// <summary>The process definition key.</summary>
    public ProcessDefinitionKey ProcessDefinitionKey => _raw.ProcessDefinitionKey;

    /// <summary>The element instance key.</summary>
    public ElementInstanceKey ElementInstanceKey => _raw.ElementInstanceKey;

    /// <summary>The job kind.</summary>
    public JobKindEnum Kind => _raw.Kind;

    /// <summary>The listener event type.</summary>
    public JobListenerEventTypeEnum ListenerEventType => _raw.ListenerEventType;

    /// <summary>User task properties (if this is a user task job).</summary>
    public UserTaskProperties? UserTask => _raw.UserTask;

    /// <summary>Tags associated with this job.</summary>
    public List<Tag>? Tags => _raw.Tags;

    /// <summary>
    /// Deserialize the job's <c>variables</c> payload into a strongly-typed DTO.
    /// </summary>
    /// <typeparam name="T">The target DTO type.</typeparam>
    /// <param name="options">Optional JSON serializer options. Uses camelCase by default.</param>
    public T? GetVariables<T>(JsonSerializerOptions? options = null)
        => _raw.Variables.DeserializeAs<T>(options);

    /// <summary>
    /// Deserialize the job's <c>customHeaders</c> payload into a strongly-typed DTO.
    /// </summary>
    /// <typeparam name="T">The target DTO type.</typeparam>
    /// <param name="options">Optional JSON serializer options. Uses camelCase by default.</param>
    public T? GetCustomHeaders<T>(JsonSerializerOptions? options = null)
        => _raw.CustomHeaders.DeserializeAs<T>(options);
}

/// <summary>
/// A long-running worker that polls the Camunda broker for jobs of a specific type,
/// dispatches them to a handler, and auto-completes or auto-fails based on the outcome.
///
/// <para><b>Concurrency model:</b> jobs are dispatched as concurrent <see cref="Task"/>s
/// on the .NET thread pool. <see cref="JobWorkerConfig.MaxConcurrentJobs"/> controls how
/// many jobs may be in-flight simultaneously. For async handlers (the typical case), the
/// thread pool thread is released during <c>await</c> points, so many jobs can be handled
/// by a small number of OS threads. For CPU-bound handlers, set <c>MaxConcurrentJobs</c>
/// to <see cref="Environment.ProcessorCount"/> to match available cores.</para>
/// </summary>
public sealed class JobWorker : IAsyncDisposable, IDisposable
{
    private static int _counter;

    private readonly CamundaClient _client;
    private readonly JobWorkerConfig _config;
    private readonly JobHandler _handler;
    private readonly string _name;
    private readonly ILogger _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    private CancellationTokenSource? _cts;
    private Task? _pollTask;
    private int _activeJobs;

    internal JobWorker(
        CamundaClient client,
        JobWorkerConfig config,
        JobHandler handler,
        ILoggerFactory loggerFactory,
        JsonSerializerOptions jsonOptions)
    {
        _client = client;
        _config = config;
        _handler = handler;
        _jsonOptions = jsonOptions;
        _name = config.WorkerName ?? $"worker-{config.JobType}-{Interlocked.Increment(ref _counter)}";
        _logger = loggerFactory.CreateLogger($"Camunda.Orchestration.Sdk.JobWorker.{_name}");

        if (config.AutoStart)
            Start();
    }

    /// <summary>Number of jobs currently being processed.</summary>
    public int ActiveJobs => Volatile.Read(ref _activeJobs);

    /// <summary>Whether the poll loop is currently running.</summary>
    public bool IsRunning => _pollTask is { IsCompleted: false };

    /// <summary>The worker's name (auto-generated or from config).</summary>
    public string Name => _name;

    /// <summary>
    /// Start the polling loop. No-op if already running.
    /// </summary>
    public void Start()
    {
        if (_cts != null)
            return;
        _cts = new CancellationTokenSource();
        _pollTask = PollLoopAsync(_cts.Token);
    }

    /// <summary>
    /// Stop the polling loop and optionally wait for in-flight jobs to drain.
    /// </summary>
    /// <param name="gracePeriod">
    /// Maximum time to wait for active jobs to finish. <c>null</c> = don't wait.
    /// </param>
    /// <returns>A snapshot of remaining active jobs and whether the grace period was exceeded.</returns>
    public async Task<StopResult> StopAsync(TimeSpan? gracePeriod = null)
    {
        _cts?.Cancel();

        if (_pollTask != null)
        {
            try
            { await _pollTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        if (gracePeriod.HasValue && ActiveJobs > 0)
        {
            var deadline = DateTimeOffset.UtcNow + gracePeriod.Value;
            while (ActiveJobs > 0 && DateTimeOffset.UtcNow < deadline)
                await Task.Delay(50).ConfigureAwait(false);
        }

        var remaining = ActiveJobs;
        return new StopResult(remaining, remaining > 0);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        _cts?.Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _cts?.Cancel();

        if (_pollTask != null)
        {
            try
            { _pollTask.GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { }
            catch (AggregateException) { }
        }

        _cts?.Dispose();
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        _logger.LogInformation("JobWorker '{Name}' started for type '{JobType}'",
            _name, _config.JobType);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var capacity = _config.MaxConcurrentJobs - ActiveJobs;
                if (capacity <= 0)
                {
                    await Task.Delay(_config.PollIntervalMs, ct).ConfigureAwait(false);
                    continue;
                }

                try
                {
                    var response = await _client.ActivateJobsAsync(new JobActivationRequest
                    {
                        Type = _config.JobType,
                        Worker = _name,
                        Timeout = _config.JobTimeoutMs,
                        MaxJobsToActivate = capacity,
                        FetchVariable = _config.FetchVariables,
                        RequestTimeout = _config.PollTimeoutMs ?? 0,
                    }, ct: ct).ConfigureAwait(false);

                    if (response?.Jobs == null || response.Jobs.Count == 0)
                    {
                        await Task.Delay(_config.PollIntervalMs, ct).ConfigureAwait(false);
                        continue;
                    }

                    foreach (var rawJob in response.Jobs)
                    {
                        var jobResult = DeserializeJob(rawJob);
                        if (jobResult == null)
                        {
                            _logger.LogWarning("JobWorker '{Name}': failed to deserialize activated job", _name);
                            continue;
                        }

                        var job = new ActivatedJob(jobResult);
                        Interlocked.Increment(ref _activeJobs);

                        // Fire-and-forget — concurrency is controlled by capacity calculation
                        _ = Task.Run(() => ExecuteJobAsync(job, ct), CancellationToken.None);
                    }

                    // Got jobs — poll again immediately
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "JobWorker '{Name}': error during activation poll", _name);
                    await Task.Delay(_config.PollIntervalMs, ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal shutdown
        }

        _logger.LogInformation("JobWorker '{Name}' stopped", _name);
    }

    private async Task ExecuteJobAsync(ActivatedJob job, CancellationToken ct)
    {
        try
        {
            var result = await _handler(job, ct).ConfigureAwait(false);

            // Auto-complete with the returned variables
            await _client.CompleteJobAsync(job.JobKey, new JobCompletionRequest
            {
                Variables = result,
            }, ct: ct).ConfigureAwait(false);

            _logger.LogDebug("JobWorker '{Name}': completed job {JobKey}", _name, job.JobKey);
        }
        catch (BpmnErrorException ex)
        {
            await SafeThrowErrorAsync(job, ex.ErrorCode, ex.ErrorMessage, ex.Variables);
        }
        catch (JobFailureException ex)
        {
            await SafeFailAsync(job, ex.Message, ex.Retries, ex.RetryBackOffMs);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Worker is stopping — don't fail the job; it will be re-activated after timeout
            _logger.LogDebug("JobWorker '{Name}': job {JobKey} cancelled due to worker shutdown",
                _name, job.JobKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JobWorker '{Name}': handler failed for job {JobKey}", _name, job.JobKey);
            await SafeFailAsync(job, ex.Message, Math.Max(0, job.Retries - 1), null);
        }
        finally
        {
            Interlocked.Decrement(ref _activeJobs);
        }
    }

    private async Task SafeFailAsync(ActivatedJob job, string errorMessage, int? retries, long? retryBackOff)
    {
        try
        {
            await _client.FailJobAsync(job.JobKey, new JobFailRequest
            {
                ErrorMessage = errorMessage,
                Retries = retries,
                RetryBackOff = retryBackOff,
            }, ct: CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JobWorker '{Name}': failed to report job failure for {JobKey}",
                _name, job.JobKey);
        }
    }

    private async Task SafeThrowErrorAsync(ActivatedJob job, string errorCode, string? errorMessage, object? variables)
    {
        try
        {
            await _client.ThrowJobErrorAsync(job.JobKey, new JobErrorRequest
            {
                ErrorCode = errorCode,
                ErrorMessage = errorMessage,
                Variables = variables,
            }, ct: CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JobWorker '{Name}': failed to throw BPMN error for {JobKey}",
                _name, job.JobKey);
        }
    }

    private ActivatedJobResult? DeserializeJob(object rawJob)
    {
        try
        {
            if (rawJob is JsonElement je)
                return je.Deserialize<ActivatedJobResult>(_jsonOptions);

            if (rawJob is ActivatedJobResult already)
                return already;

            // Fallback: re-serialize and deserialize
            var json = JsonSerializer.Serialize(rawJob, _jsonOptions);
            return JsonSerializer.Deserialize<ActivatedJobResult>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "JobWorker '{Name}': could not deserialize job object", _name);
            return null;
        }
    }
}

/// <summary>
/// Result of a <see cref="JobWorker.StopAsync"/> call.
/// </summary>
/// <param name="RemainingJobs">Number of jobs still in-flight when stop completed.</param>
/// <param name="TimedOut">Whether the grace period was exceeded with jobs still active.</param>
public readonly record struct StopResult(int RemainingJobs, bool TimedOut);
