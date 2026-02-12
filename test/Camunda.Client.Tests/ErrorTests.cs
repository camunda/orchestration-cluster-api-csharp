using Camunda.Client.Runtime;
using FluentAssertions;

namespace Camunda.Client.Tests;

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

        ex.Status.Should().Be(404);
        ex.OperationId.Should().Be("getProcessInstance");
        ex.Title.Should().Be("NOT_FOUND");
        ex.Detail.Should().Be("Process instance not found");
    }

    [Fact]
    public void EventualConsistencyTimeoutExceptionCarriesWaitTime()
    {
        var ex = new EventualConsistencyTimeoutException("timeout", "searchProcessInstances", 5000);

        ex.WaitedMs.Should().Be(5000);
        ex.OperationId.Should().Be("searchProcessInstances");
    }

    [Fact]
    public void CamundaAuthExceptionCarriesCode()
    {
        var ex = new CamundaAuthException(CamundaAuthErrorCode.TokenFetchFailed, "Failed");

        ex.Code.Should().Be(CamundaAuthErrorCode.TokenFetchFailed);
        ex.Message.Should().Contain("TokenFetchFailed");
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

        ex.Errors.Should().HaveCount(2);
        ex.Message.Should().Contain("CAMUNDA_AUTH_STRATEGY");
        ex.Message.Should().Contain("CAMUNDA_OAUTH_TIMEOUT_MS");
    }
}
