// Compilable usage examples for deployment and resource operations.
// These examples are type-checked during build to guard against API regressions.
using Camunda.Orchestration.Sdk;

public static class DeploymentExamples
{
    #region CreateDeployment
    public static async Task CreateDeploymentExample()
    {
        using var client = CamundaClient.Create();

        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(File.ReadAllBytes("process.bpmn"));
        content.Add(fileContent, "resources", "process.bpmn");

        var result = await client.CreateDeploymentAsync(content);
        Console.WriteLine($"Deployment key: {result.DeploymentKey}");
    }
    #endregion CreateDeployment

    #region DeleteResource
    public static async Task DeleteResourceExample()
    {
        using var client = CamundaClient.Create();

        await client.DeleteResourceAsync(
            new ResourceKey("123456"),
            new DeleteResourceRequest());
    }
    #endregion DeleteResource

    #region GetResource
    public static async Task GetResourceExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetResourceAsync(new ResourceKey("123456"));
        Console.WriteLine($"Resource: {result.ResourceName}");
    }
    #endregion GetResource

    #region GetResourceContent
    public static async Task GetResourceContentExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetResourceContentAsync(new ResourceKey("123456"));
        Console.WriteLine($"Content: {result}");
    }
    #endregion GetResourceContent
}
