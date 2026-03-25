// Compilable usage examples for document operations.
// These examples are type-checked during build to guard against API regressions.
using Camunda.Orchestration.Sdk;

public static class DocumentExamples
{
    #region CreateDocumentLink
    public static async Task CreateDocumentLinkExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.CreateDocumentLinkAsync(
            new DocumentId("doc-123"),
            new DocumentLinkRequest());

        Console.WriteLine($"Document link: {result.Url}");
    }
    #endregion CreateDocumentLink

    #region DeleteDocument
    public static async Task DeleteDocumentExample()
    {
        using var client = CamundaClient.Create();

        await client.DeleteDocumentAsync(new DocumentId("doc-123"));
    }
    #endregion DeleteDocument
}
