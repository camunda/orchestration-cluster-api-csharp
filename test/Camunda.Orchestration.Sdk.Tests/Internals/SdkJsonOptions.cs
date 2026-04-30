using System.Text.Json;
using System.Text.Json.Serialization;

namespace Camunda.Orchestration.Sdk.Tests.Internals;

/// <summary>
/// Builds <see cref="JsonSerializerOptions"/> identical to those constructed by
/// <c>CamundaClient</c> at runtime. Sweep tests use this so they exercise the
/// real wire contract rather than a divergent test-only configuration.
/// </summary>
internal static class SdkJsonOptions
{
    public static JsonSerializerOptions Create()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true,
            Converters =
            {
                new TolerantEnumConverterFactory(),
                new CamundaKeyJsonConverterFactory(),
                new CamundaLongKeyJsonConverterFactory(),
            },
        };
    }
}
