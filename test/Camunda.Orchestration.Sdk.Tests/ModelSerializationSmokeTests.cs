using System.Reflection;
using System.Text.Json;
using Camunda.Orchestration.Sdk.Tests.Internals;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Sweep test: every concrete public model type with a public parameterless
/// constructor must be serializable with the runtime JSON options without
/// throwing, and the produced JSON must parse back into a JSON object.
///
/// This is a smoke test, not a structural round-trip test — the rubber-duck
/// review at #67 noted that asserting structural equality on default-instance
/// round-trips produces false positives for <c>null!</c>-initialized
/// non-nullable references and default <see cref="ICamundaKey"/> structs.
/// The sharper assertion is "serializer does not throw on the public surface".
/// </summary>
public class ModelSerializationSmokeTests
{
    private static readonly JsonSerializerOptions s_options = SdkJsonOptions.Create();

    public static IEnumerable<object[]> ConcretePublicModelTypes()
    {
        var asm = typeof(CamundaClient).Assembly;
        foreach (var t in asm.GetTypes())
        {
            if (!t.IsPublic)
                continue;
            if (!t.IsClass)
                continue;
            if (t.IsAbstract)
                continue; // covered by polymorphic sweep
            if (t.IsGenericTypeDefinition)
                continue;
            if (t == typeof(CamundaClient))
                continue;
            if (t.Namespace == null)
                continue;
            if (!t.Namespace.StartsWith("Camunda.Orchestration.Sdk", StringComparison.Ordinal))
                continue;
            // Must have a public parameterless constructor (DTOs do).
            if (t.GetConstructor(Type.EmptyTypes) == null)
                continue;
            // Skip runtime infrastructure that isn't a wire model.
            if (t.Namespace.Contains(".Runtime", StringComparison.Ordinal))
                continue;
            // Skip exceptions and option/config holders that aren't part of the wire surface.
            if (typeof(Exception).IsAssignableFrom(t))
                continue;
            if (t.Name.EndsWith("Options", StringComparison.Ordinal))
                continue;
            if (t.Name.EndsWith("Config", StringComparison.Ordinal))
                continue;

            yield return new object[] { t };
        }
    }

    [Theory]
    [MemberData(nameof(ConcretePublicModelTypes))]
    public void DefaultInstance_Serializes_To_JsonObject(Type type)
    {
        var instance = Activator.CreateInstance(type);
        Assert.NotNull(instance);

        string json;
        try
        {
            json = JsonSerializer.Serialize(instance, type, s_options);
        }
        catch (Exception ex)
        {
            throw new Xunit.Sdk.XunitException(
                $"Serializing default instance of {type.FullName} threw {ex.GetType().Name}: {ex.Message}");
        }

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }

    [Fact]
    public void ModelTypeCount_IsNonTrivial()
    {
        // Guard against the sweep silently degenerating to zero types
        // (e.g. namespace rename, reflection filter regression).
        var count = ConcretePublicModelTypes().Count();
        Assert.True(count > 200, $"Expected >200 concrete public model types, found {count}");
    }
}
