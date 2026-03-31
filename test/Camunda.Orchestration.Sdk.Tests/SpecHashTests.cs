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
    public void SpecHash_StartsWithSha256Prefix()
    {
        CamundaClient.SpecHash.Should().StartWith("sha256:");
    }

    [Fact]
    public void SpecHash_HasCorrectLength()
    {
        // sha256: prefix (7 chars) + 64 hex chars = 71
        CamundaClient.SpecHash.Should().HaveLength(71);
    }
}
