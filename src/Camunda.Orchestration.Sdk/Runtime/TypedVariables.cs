using System.Text.Json;

namespace Camunda.Orchestration.Sdk;

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
///
/// <para>
/// <b>Received payloads are untrusted input.</b> The content of <c>Variables</c> is set by
/// whoever wrote the variables into the process instance. <c>CustomHeaders</c> are defined in the
/// process model instead, so they are controlled by whoever can deploy or change the model — a
/// narrower group, but still not your own code. Deserialization is explicit — only into the type
/// you name, with no unrestricted type-name resolution, so the payload cannot name a type for the
/// SDK to load; <c>System.Text.Json</c> will still dispatch to polymorphic subtypes your own
/// target type declares. Depth is bounded by the <c>System.Text.Json</c> default
/// <c>MaxDepth</c> of 64 when no custom <c>MaxDepth</c> is given; for a job's variables that
/// bound was already applied to the activation response the element came from, so options passed
/// here can tighten it but cannot admit a payload that was rejected earlier. That guarantee is
/// qualified: custom options passed to <see cref="DeserializeAs{T}"/> on an element that was
/// parsed elsewhere can raise the limit, and an object that is already a typed DTO skips
/// conversion, so no depth bound is applied to it. There is no size cap, no schema validation and
/// no sanitisation: deserializing into a DTO converts the shape, it does not validate the values.
/// By default, for an ordinary DTO, unmatched properties are ignored and missing ones keep their
/// initializer or default; <c>required</c> members and options you pass can make a mismatch
/// throw a <c>JsonException</c>, whose message can name payload properties and, if it goes
/// unhandled inside a job handler, ends up in the worker log and in the failure message sent to
/// the broker.
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
