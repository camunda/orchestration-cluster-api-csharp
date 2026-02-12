using System.Net.Http.Headers;
using Camunda.Client.Api;
using Camunda.Client.Runtime;

namespace Camunda.Client;

public partial class CamundaClient
{
    /// <summary>
    /// Deploy resources from local filesystem paths.
    /// Reads the specified files, infers MIME types from their extensions,
    /// and calls <see cref="CreateDeploymentAsync"/> with the loaded content.
    /// </summary>
    /// <param name="resourceFilePaths">Absolute or relative file paths to BPMN, DMN, form, or resource files.</param>
    /// <param name="tenantId">Optional tenant ID for multi-tenant deployments.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An <see cref="ExtendedDeploymentResponse"/> with typed access to deployed artifacts.</returns>
    /// <exception cref="ArgumentException">When <paramref name="resourceFilePaths"/> is null or empty.</exception>
    /// <exception cref="FileNotFoundException">When a specified file does not exist.</exception>
    public async Task<ExtendedDeploymentResponse> DeployResourcesFromFilesAsync(
        string[] resourceFilePaths,
        string? tenantId = null,
        CancellationToken ct = default)
    {
        if (resourceFilePaths == null || resourceFilePaths.Length == 0)
            throw new ArgumentException("At least one resource file path must be provided.", nameof(resourceFilePaths));

        using var content = new MultipartFormDataContent();

        foreach (var filePath in resourceFilePaths)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Resource file path must not be null or empty.", nameof(resourceFilePaths));

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Resource file not found: {filePath}", filePath);

            var fileBytes = await File.ReadAllBytesAsync(filePath, ct);
            var fileName = Path.GetFileName(filePath);
            var byteContent = new ByteArrayContent(fileBytes);
            byteContent.Headers.ContentType = new MediaTypeHeaderValue(InferMimeType(fileName));
            content.Add(byteContent, "resources", fileName);
        }

        if (tenantId != null)
            content.Add(new StringContent(tenantId), "tenantId");
        else
            content.Add(new StringContent(_config.DefaultTenantId), "tenantId");

        var raw = await CreateDeploymentAsync(content, ct);
        return new ExtendedDeploymentResponse(raw);
    }

    private static string InferMimeType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".bpmn" or ".dmn" or ".xml" => "application/xml",
            ".json" or ".form" => "application/json",
            _ => "application/octet-stream",
        };
    }
}
