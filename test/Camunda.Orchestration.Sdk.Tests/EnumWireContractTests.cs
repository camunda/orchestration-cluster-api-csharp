using System.Reflection;
using System.Text.Json;
using Camunda.Orchestration.Sdk.Tests.Internals;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Sweep test: for every public enum in the SDK, every declared member must
/// round-trip through serialize → deserialize using the runtime JSON options.
/// This is a self-consistency assertion — it does NOT prescribe the wire
/// format (which differs between .NET 8 and .NET 10 for enums with
/// <see cref="System.Text.Json.Serialization.JsonPropertyNameAttribute"/> due
/// to the .NET 9 change in <c>JsonStringEnumConverter</c>). The contract we
/// lock here is "what the SDK writes, the SDK can read back to the same value".
///
/// Also asserts no two enum members emit the same wire string (which would
/// make round-trip ambiguous).
/// </summary>
public class EnumWireContractTests
{
    private static readonly JsonSerializerOptions s_options = SdkJsonOptions.Create();

    public static IEnumerable<object[]> PublicEnumTypes()
    {
        var asm = typeof(CamundaClient).Assembly;
        foreach (var t in asm.GetTypes())
        {
            if (!t.IsPublic)
                continue;
            if (!t.IsEnum)
                continue;
            if (t.Namespace == null)
                continue;
            if (!t.Namespace.StartsWith("Camunda.Orchestration.Sdk", StringComparison.Ordinal))
                continue;
            yield return new object[] { t };
        }
    }

    [Theory]
    [MemberData(nameof(PublicEnumTypes))]
    public void EveryDeclaredValue_RoundTripsToItself(Type enumType)
    {
        var values = Enum.GetValues(enumType);
        Assert.NotEmpty(values);

        foreach (var value in values)
        {
            string json;
            try
            {
                json = JsonSerializer.Serialize(value, enumType, s_options);
            }
            catch (Exception ex)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Serializing {enumType.FullName}.{value} threw {ex.GetType().Name}: {ex.Message}");
            }

            object? readBack;
            try
            {
                readBack = JsonSerializer.Deserialize(json, enumType, s_options);
            }
            catch (Exception ex)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Deserializing {json} for {enumType.FullName}.{value} threw {ex.GetType().Name}: {ex.Message}");
            }

            Assert.Equal(value, readBack);
        }
    }

    [Theory]
    [MemberData(nameof(PublicEnumTypes))]
    public void NoTwoMembers_EmitTheSameWireString(Type enumType)
    {
        var values = Enum.GetValues(enumType);
        var seen = new Dictionary<string, object>(StringComparer.Ordinal);

        foreach (var value in values)
        {
            var json = JsonSerializer.Serialize(value, enumType, s_options);
            if (seen.TryGetValue(json, out var existing))
            {
                throw new Xunit.Sdk.XunitException(
                    $"{enumType.FullName}: members {existing} and {value} both serialize to {json} — round-trip would be ambiguous");
            }
            seen[json] = value!;
        }
    }

    [Fact]
    public void EnumTypeCount_IsNonTrivial()
    {
        var count = PublicEnumTypes().Count();
        Assert.True(count > 20, $"Expected >20 public enum types, found {count}");
    }
}
