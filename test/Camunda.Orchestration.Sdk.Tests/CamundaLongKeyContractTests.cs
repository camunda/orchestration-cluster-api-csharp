using System.Reflection;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Guards the <see cref="ICamundaLongKey"/> contract for every generated branded
/// struct backed by an integer primitive (e.g. IterationId).
///
/// Defect class: the generator brands every primitive component schema as a
/// readonly record struct. Non-string primitives implement <see cref="ICamundaLongKey"/>,
/// whose <c>Value</c> is <c>long</c> and whose JSON converter
/// (<c>CamundaLongKeyJsonConverter&lt;T&gt;</c>) reflects a static <c>AssumeExists(long)</c>
/// factory. A narrower integer primitive (int32) must therefore be widened to <c>long</c>;
/// emitting <c>int Value</c> / <c>AssumeExists(int)</c> breaks the interface (CS0738) and
/// the converter at runtime.
///
/// This test is class-scoped: it sweeps the whole SDK assembly so any future
/// integer-typed primitive schema is covered, not just IterationId.
/// </summary>
public class CamundaLongKeyContractTests
{
    private static IEnumerable<Type> LongKeyStructs() =>
        typeof(ICamundaLongKey).Assembly
            .GetTypes()
            .Where(t => t.IsValueType && typeof(ICamundaLongKey).IsAssignableFrom(t));

    [Fact]
    public void EveryLongKeyStruct_HasLongValueProperty()
    {
        foreach (var type in LongKeyStructs())
        {
            var valueProp = type.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(valueProp);
            Assert.Equal(typeof(long), valueProp!.PropertyType);
        }
    }

    [Fact]
    public void EveryLongKeyStruct_HasStaticAssumeExistsLongFactory()
    {
        foreach (var type in LongKeyStructs())
        {
            var factory = type.GetMethod(
                "AssumeExists",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: [typeof(long)],
                modifiers: null);
            Assert.NotNull(factory);
            Assert.Equal(type, factory!.ReturnType);
        }
    }
}
