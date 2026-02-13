using System.Text.Json;

namespace Camunda.Orchestration.Sdk.Runtime;

/// <summary>
/// Extension methods for deserializing Camunda variable and custom header payloads
/// from untyped <c>object</c> properties into strongly-typed DTOs.
///
/// <para>
/// Camunda API responses return <c>variables</c> and <c>customHeaders</c> as
/// <c>object</c> properties which, at runtime, are <see cref="JsonElement"/> values.
/// These extensions let you opt in to typed deserialization:
/// </para>
///
/// <example>
/// <code>
/// // Define your domain DTO
/// public record OrderVars(string OrderId, decimal Amount);
///
/// // Deserialize variables from a process instance result
/// var result = await client.CreateProcessInstanceAsync(
///     new ProcessInstanceCreationInstructionById
///     {
///         ProcessDefinitionId = ProcessDefinitionId.AssumeExists("order-process"),
///         Variables = new OrderVars("ord-123", 99.99m),  // input: just assign your DTO
///     });
///
/// var vars = result.Variables.DeserializeAs&lt;OrderVars&gt;();  // output: typed extraction
/// </code>
/// </example>
///
/// <para>
/// For <b>input</b> (sending variables), simply assign your DTO to the <c>Variables</c>
/// property — <c>System.Text.Json</c> serializes the runtime type automatically.
/// </para>
///
/// <para>
/// For <b>output</b> (receiving variables), call <see cref="DeserializeAs{T}"/> on the
/// <c>Variables</c> or <c>CustomHeaders</c> property to deserialize the underlying
/// <see cref="JsonElement"/> into your DTO type.
/// </para>
/// </summary>
public static class TypedVariables
{
    /// <summary>
    /// Deserializes a Camunda variable or custom header payload to the specified type.
    /// Works on <c>object</c> properties that are <see cref="JsonElement"/> at runtime
    /// (the standard shape returned by the Camunda REST API).
    /// </summary>
    /// <typeparam name="T">The target DTO type to deserialize into.</typeparam>
    /// <param name="payload">
    /// The <c>Variables</c> or <c>CustomHeaders</c> property value from a Camunda API response.
    /// </param>
    /// <param name="options">Optional JSON serializer options. When <c>null</c>,
    /// uses default options with camelCase naming to match Camunda's JSON format.</param>
    /// <returns>The deserialized DTO, or <c>default</c> if the payload is null.</returns>
    public static T? DeserializeAs<T>(this object? payload, JsonSerializerOptions? options = null)
    {
        options ??= DefaultOptions;

        return payload switch
        {
            JsonElement je => je.Deserialize<T>(options),
            T typed => typed,
            null => default,
            _ => JsonSerializer.Deserialize<T>(
                    JsonSerializer.Serialize(payload, options), options),
        };
    }

    /// <summary>
    /// Default JSON options matching the Camunda REST API conventions.
    /// </summary>
    internal static readonly JsonSerializerOptions DefaultOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };
}
