using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Camunda.Orchestration.Sdk.Tests.Internals;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Sweep test: every <c>[JsonPolymorphic]</c>-annotated base type in the SDK
/// must dispatch correctly for every <c>[JsonDerivedType]</c> entry.
///
/// Split into two contracts (per the rubber-duck review on #67):
/// <list type="bullet">
///   <item><b>Discriminated</b> bases (those that set
///   <see cref="JsonPolymorphicAttribute.TypeDiscriminatorPropertyName"/>):
///   serializing a derived instance through the base type must emit the
///   discriminator, and deserializing through the base must preserve the
///   concrete type.</item>
///   <item><b>Non-discriminated</b> bases (registered only via
///   <c>[JsonDerivedType(typeof(X))]</c>): serializing the concrete type as
///   itself round-trips. Base-typed deserialization is not supported by
///   System.Text.Json without discriminator metadata, so we do not assert it.</item>
/// </list>
/// </summary>
public class PolymorphicDispatchSweepTests
{
    private static readonly JsonSerializerOptions s_options = SdkJsonOptions.Create();

    public static IEnumerable<object[]> DiscriminatedBases()
    {
        foreach (var (baseType, derivedTypes, discriminator) in EnumeratePolymorphicBases())
        {
            if (discriminator == null)
                continue;
            foreach (var (derived, tag) in derivedTypes)
            {
                if (tag == null)
                    continue; // shouldn't happen for discriminated bases
                yield return new object[] { baseType, derived, tag, discriminator };
            }
        }
    }

    public static IEnumerable<object[]> NonDiscriminatedBases()
    {
        foreach (var (baseType, derivedTypes, discriminator) in EnumeratePolymorphicBases())
        {
            if (discriminator != null)
                continue;
            foreach (var (derived, _) in derivedTypes)
            {
                yield return new object[] { baseType, derived };
            }
        }
    }

    [Theory]
    [MemberData(nameof(DiscriminatedBases))]
    public void Discriminated_DeserializeBase_FromDiscriminatorOnlyJson_PreservesConcreteType(
        Type baseType, Type derivedType, string expectedTag, string discriminatorProp)
    {
        // Test dispatch independently of model default-instance values. A
        // minimal JSON object containing only the discriminator must
        // deserialize as the correct concrete type.
        var json = $"{{\"{discriminatorProp}\":\"{expectedTag}\"}}";
        object? readBack;
        try
        {
            readBack = JsonSerializer.Deserialize(json, baseType, s_options);
        }
        catch (Exception ex)
        {
            throw new Xunit.Sdk.XunitException(
                $"Deserializing {json} as {baseType.Name} threw {ex.GetType().Name}: {ex.Message}");
        }

        Assert.NotNull(readBack);
        Assert.IsType(derivedType, readBack);
    }

    [Theory]
    [MemberData(nameof(DiscriminatedBases))]
    public void Discriminated_SerializeAsBase_EmitsDiscriminator(
        Type baseType, Type derivedType, string expectedTag, string discriminatorProp)
    {
        var instance = Activator.CreateInstance(derivedType);
        Assert.NotNull(instance);

        var json = JsonSerializer.Serialize(instance, baseType, s_options);
        using var doc = JsonDocument.Parse(json);
        Assert.True(
            doc.RootElement.TryGetProperty(discriminatorProp, out var disc),
            $"Expected discriminator property '{discriminatorProp}' on serialized {baseType.Name} (got {json})");
        Assert.Equal(expectedTag, disc.GetString());
    }

    [Theory]
    [MemberData(nameof(NonDiscriminatedBases))]
    public void NonDiscriminated_SerializeDerivedAsBase_DoesNotThrow_AndEmitsObject(
        Type baseType, Type derivedType)
    {
        // For non-discriminated polymorphic bases, System.Text.Json cannot
        // reconstruct the concrete type during deserialization without
        // discriminator metadata. The realistic guarantee is that a derived
        // instance can be serialized through the base type without throwing.
        var instance = Activator.CreateInstance(derivedType);
        Assert.NotNull(instance);

        var json = JsonSerializer.Serialize(instance, baseType, s_options);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }

    [Fact]
    public void PolymorphicBaseCount_IsNonTrivial()
    {
        var count = EnumeratePolymorphicBases().Count();
        Assert.True(count >= 4, $"Expected ≥4 [JsonPolymorphic]-annotated bases, found {count}");
    }

    private static IEnumerable<(Type Base, List<(Type Derived, string? Tag)> Derived, string? Discriminator)>
        EnumeratePolymorphicBases()
    {
        var asm = typeof(CamundaClient).Assembly;
        foreach (var t in asm.GetTypes())
        {
            if (!t.IsPublic)
                continue;
            if (t.Namespace == null)
                continue;
            if (!t.Namespace.StartsWith("Camunda.Orchestration.Sdk", StringComparison.Ordinal))
                continue;

            var derivedAttrs = t.GetCustomAttributes<JsonDerivedTypeAttribute>(inherit: false).ToList();
            if (derivedAttrs.Count == 0)
                continue;

            var polyAttr = t.GetCustomAttribute<JsonPolymorphicAttribute>(inherit: false);
            var discriminator = polyAttr?.TypeDiscriminatorPropertyName;

            var derived = derivedAttrs
                .Select(a => (a.DerivedType, Tag: a.TypeDiscriminator?.ToString()))
                .ToList();

            yield return (t, derived, discriminator);
        }
    }
}
