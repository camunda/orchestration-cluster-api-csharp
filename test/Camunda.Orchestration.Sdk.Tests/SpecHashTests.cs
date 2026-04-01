using FluentAssertions;

namespace Camunda.Orchestration.Sdk.Tests;

public class SpecHashTests
{
    [Fact]
    public void SpecHash_IsNotEmpty()
    {
        CamundaClient.SpecHash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void SpecHash_HasValidSha256Format()
    {
        // sha256: prefix followed by 64 hex chars
        CamundaClient.SpecHash.Should().MatchRegex(@"^sha256:[0-9a-fA-F]{64}$");
    }
}
