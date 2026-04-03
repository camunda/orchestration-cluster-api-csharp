using Xunit;

namespace Camunda.Orchestration.Sdk.Changelog.Tests;

public class ParserTests
{
    [Fact]
    public void ParsesPublicSealedClass()
    {
        var source = """
            namespace Test;
            public sealed class Foo
            {
                [System.Text.Json.Serialization.JsonPropertyName("bar")]
                public string Bar { get; set; } = null!;

                [System.Text.Json.Serialization.JsonPropertyName("baz")]
                public int? Baz { get; set; }
            }
            """;

        var surface = Parser.ParseSource(source);

        Assert.True(surface.Classes.ContainsKey("Foo"));
        var cls = surface.Classes["Foo"];
        Assert.True(cls.IsSealed);
        Assert.False(cls.IsAbstract);
        Assert.Equal(2, cls.Properties.Count);

        var bar = cls.Properties.First(p => p.Name == "Bar");
        Assert.Equal("bar", bar.JsonName);
        Assert.Equal("string", bar.TypeExpr);
        Assert.False(bar.IsNullable);
        Assert.True(bar.IsRequired);

        var baz = cls.Properties.First(p => p.Name == "Baz");
        Assert.Equal("baz", baz.JsonName);
        Assert.Equal("int?", baz.TypeExpr);
        Assert.True(baz.IsNullable);
        Assert.False(baz.IsRequired);
    }

    [Fact]
    public void ParsesEnumWithJsonPropertyName()
    {
        var source = """
            using System.Text.Json.Serialization;
            namespace Test;

            [JsonConverter(typeof(JsonStringEnumConverter))]
            public enum StatusEnum
            {
                [JsonPropertyName("ACTIVE")]
                ACTIVE,
                [JsonPropertyName("COMPLETED")]
                COMPLETED,
                [JsonPropertyName("CANCELED")]
                CANCELED,
            }
            """;

        var surface = Parser.ParseSource(source);

        Assert.True(surface.Enums.ContainsKey("StatusEnum"));
        var e = surface.Enums["StatusEnum"];
        Assert.Equal(3, e.Members.Count);
        Assert.Equal("ACTIVE", e.Members[0].Name);
        Assert.Equal("ACTIVE", e.Members[0].JsonName);
        Assert.Equal("COMPLETED", e.Members[1].Name);
        Assert.Equal("CANCELED", e.Members[2].Name);
    }

    [Fact]
    public void ParsesReadonlyRecordStruct()
    {
        var source = """
            namespace Test;
            public readonly record struct JobKey
            {
                public string Value { get; }
                private JobKey(string value) => Value = value;
                public static JobKey AssumeExists(string value) => new JobKey(value);
                public override string ToString() => Value;
            }
            """;

        var surface = Parser.ParseSource(source);

        Assert.True(surface.Structs.ContainsKey("JobKey"));
        var s = surface.Structs["JobKey"];
        Assert.True(s.IsReadOnly);
        Assert.True(s.IsRecord);
        Assert.Contains(s.Properties, p => p.Name == "Value");
    }

    [Fact]
    public void ParsesPolymorphicClass()
    {
        var source = """
            using System.Text.Json.Serialization;
            namespace Test;

            [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
            [JsonDerivedType(typeof(FooVariant), "foo")]
            [JsonDerivedType(typeof(BarVariant), "bar")]
            public abstract class BaseType { }

            public sealed class FooVariant : BaseType { }
            public sealed class BarVariant : BaseType { }
            """;

        var surface = Parser.ParseSource(source);

        Assert.True(surface.Classes.ContainsKey("BaseType"));
        var cls = surface.Classes["BaseType"];
        Assert.True(cls.IsAbstract);
        Assert.NotNull(cls.Polymorphic);
        Assert.Equal("type", cls.Polymorphic!.Discriminator);
        Assert.Equal(2, cls.Polymorphic.DerivedTypes.Count);
        Assert.Equal("FooVariant", cls.Polymorphic.DerivedTypes[0].TypeName);
        Assert.Equal("foo", cls.Polymorphic.DerivedTypes[0].DiscriminatorValue);
        Assert.Equal("BarVariant", cls.Polymorphic.DerivedTypes[1].TypeName);
    }

    [Fact]
    public void ParsesImplicitConversions()
    {
        var source = """
            namespace Test;
            public readonly record struct ScopeKey
            {
                public string Value { get; }
                private ScopeKey(string value) => Value = value;

                public static implicit operator ScopeKey(ProcessInstanceKey key) => new ScopeKey(key.Value);
                public static implicit operator ScopeKey(ElementInstanceKey key) => new ScopeKey(key.Value);

                public override string ToString() => Value;
            }

            public readonly record struct ProcessInstanceKey
            {
                public string Value { get; }
            }

            public readonly record struct ElementInstanceKey
            {
                public string Value { get; }
            }
            """;

        var surface = Parser.ParseSource(source);

        var sk = surface.Structs["ScopeKey"];
        Assert.Equal(2, sk.ImplicitConversions.Count);
        Assert.Contains(sk.ImplicitConversions, c => c.FromType == "ProcessInstanceKey");
        Assert.Contains(sk.ImplicitConversions, c => c.FromType == "ElementInstanceKey");
    }

    [Fact]
    public void ParsesClientMethods()
    {
        var source = """
            namespace Test;
            public partial class CamundaClient
            {
                /// <summary>Activate jobs</summary>
                /// <remarks>Operation: activateJobs</remarks>
                public async Task<JobActivationResult> ActivateJobsAsync(JobActivationRequest body, CancellationToken ct = default)
                {
                    return null!;
                }

                /// <summary>Delete process</summary>
                /// <remarks>Operation: deleteProcess</remarks>
                public async Task DeleteProcessAsync(string processId, CancellationToken ct = default)
                {
                }
            }
            """;

        var surface = Parser.ParseSource(source);

        Assert.Equal(2, surface.ClientMethods.Count);

        var activate = surface.ClientMethods.First(m => m.Name == "ActivateJobsAsync");
        Assert.Equal("Task<JobActivationResult>", activate.ReturnType);
        Assert.Equal("activateJobs", activate.OperationId);
        Assert.Single(activate.Parameters); // CancellationToken excluded
        Assert.Equal("JobActivationRequest", activate.Parameters[0].TypeExpr);

        var delete = surface.ClientMethods.First(m => m.Name == "DeleteProcessAsync");
        Assert.Equal("Task", delete.ReturnType);
        Assert.Single(delete.Parameters);
        Assert.Equal("string", delete.Parameters[0].TypeExpr);
    }

    [Fact]
    public void IgnoresNonPublicMembers()
    {
        var source = """
            namespace Test;
            internal class InternalClass { }
            public sealed class PublicClass
            {
                private string Secret { get; set; } = "";
                public string Visible { get; set; } = "";
            }
            """;

        var surface = Parser.ParseSource(source);

        Assert.False(surface.Classes.ContainsKey("InternalClass"));
        Assert.True(surface.Classes.ContainsKey("PublicClass"));
        Assert.Single(surface.Classes["PublicClass"].Properties);
        Assert.Equal("Visible", surface.Classes["PublicClass"].Properties[0].Name);
    }
}
