namespace Camunda.Client.Runtime;

/// <summary>
/// SDK error types mirroring the JS SDK's error structure.
/// </summary>
public class CamundaSdkException : Exception
{
    public string? OperationId { get; init; }
    public int? Status { get; init; }

    public CamundaSdkException(string message, string? operationId = null, int? status = null, Exception? inner = null)
        : base(message, inner)
    {
        OperationId = operationId;
        Status = status;
    }
}

/// <summary>
/// HTTP-specific SDK error with RFC 7807 Problem Details.
/// </summary>
public sealed class HttpSdkException : CamundaSdkException
{
    public string? Type { get; init; }
    public string? Title { get; init; }
    public string? Detail { get; init; }
    public string? Instance { get; init; }
    public bool IsBackpressure { get; init; }

    public HttpSdkException(string message, int status, string? operationId = null, Exception? inner = null)
        : base(message, operationId, status, inner)
    {
    }
}

/// <summary>
/// Thrown when an eventually consistent endpoint times out waiting for data.
/// </summary>
public sealed class EventualConsistencyTimeoutException : CamundaSdkException
{
    public int WaitedMs { get; init; }

    public EventualConsistencyTimeoutException(string message, string? operationId = null, int waitedMs = 0)
        : base(message, operationId)
    {
        WaitedMs = waitedMs;
    }
}

/// <summary>
/// Thrown when a cancellable operation is cancelled.
/// </summary>
public sealed class CancelSdkException : CamundaSdkException
{
    public CancelSdkException() : base("Cancelled") { }
}

/// <summary>
/// Throw from a job handler to trigger a BPMN error boundary event on the job's task.
/// The error code is matched against error catch events in the process model.
/// </summary>
public sealed class BpmnErrorException : Exception
{
    /// <summary>The error code matched against BPMN error catch events.</summary>
    public string ErrorCode { get; }

    /// <summary>Optional additional context message.</summary>
    public string? ErrorMessage { get; }

    /// <summary>Optional variables to set at the error catch event scope.</summary>
    public object? Variables { get; }

    public BpmnErrorException(string errorCode, string? errorMessage = null, object? variables = null)
        : base(errorMessage ?? $"BPMN error: {errorCode}")
    {
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        Variables = variables;
    }
}

/// <summary>
/// Throw from a job handler to explicitly fail a job with custom retry settings.
/// </summary>
public sealed class JobFailureException : Exception
{
    /// <summary>How many retries the job should have remaining. <c>null</c> = server decides.</summary>
    public int? Retries { get; }

    /// <summary>Retry back-off in milliseconds. <c>null</c> = immediate retry.</summary>
    public long? RetryBackOffMs { get; }

    public JobFailureException(string message, int? retries = null, long? retryBackOffMs = null)
        : base(message)
    {
        Retries = retries;
        RetryBackOffMs = retryBackOffMs;
    }
}
