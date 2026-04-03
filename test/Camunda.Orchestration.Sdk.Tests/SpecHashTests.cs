namespace Camunda.Orchestration.Sdk.Tests;

public class SpecHashTests
{
    [Fact]
    public void SpecHash_IsNotEmpty()
    {
        Assert.False(string.IsNullOrEmpty(CamundaClient.SpecHash));
    }

    [Fact]
    public void SpecHash_HasValidSha256Format()
    {
        // sha256: prefix followed by 64 hex chars
        Assert.Matches(@"^sha256:[0-9a-fA-F]{64}$", CamundaClient.SpecHash);
    }
}
