namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Tests for error types and error construction.
/// </summary>
public class ErrorTests
{
    [Fact]
    public void HttpSdkExceptionCarriesProblemDetails()
    {
        var ex = new HttpSdkException("Not Found", 404, "getProcessInstance")
        {
            Type = "about:blank",
            Title = "NOT_FOUND",
            Detail = "Process instance not found",
            Instance = "/v2/process-instances/123",
        };

        Assert.Equal(404, ex.Status);
        Assert.Equal("getProcessInstance", ex.OperationId);
        Assert.Equal("NOT_FOUND", ex.Title);
        Assert.Equal("Process instance not found", ex.Detail);
    }

    [Fact]
    public void EventualConsistencyTimeoutExceptionCarriesWaitTime()
    {
        var ex = new EventualConsistencyTimeoutException("timeout", "searchProcessInstances", 5000);

        Assert.Equal(5000, ex.WaitedMs);
        Assert.Equal("searchProcessInstances", ex.OperationId);
    }

    [Fact]
    public void CamundaAuthExceptionCarriesCode()
    {
        var ex = new CamundaAuthException(CamundaAuthErrorCode.TokenFetchFailed, "Failed");

        Assert.Equal(CamundaAuthErrorCode.TokenFetchFailed, ex.Code);
        Assert.Contains("TokenFetchFailed", ex.Message);
    }

    [Fact]
    public void CamundaConfigurationExceptionAggregatesErrors()
    {
        var errors = new List<ConfigErrorDetail>
        {
            new() { Code = ConfigErrorCode.InvalidEnum, Key = "CAMUNDA_AUTH_STRATEGY", Message = "bad" },
            new() { Code = ConfigErrorCode.InvalidInteger, Key = "CAMUNDA_OAUTH_TIMEOUT_MS", Message = "bad" },
        };

        var ex = new CamundaConfigurationException(errors);

        Assert.Equal(2, ex.Errors.Count);
        Assert.Contains("CAMUNDA_AUTH_STRATEGY", ex.Message);
        Assert.Contains("CAMUNDA_OAUTH_TIMEOUT_MS", ex.Message);
    }
}
