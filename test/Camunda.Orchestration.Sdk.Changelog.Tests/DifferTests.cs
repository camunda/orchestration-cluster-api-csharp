using Xunit;

namespace Camunda.Orchestration.Sdk.Changelog.Tests;

public class DifferTests
{
    [Fact]
    public void DetectsRemovedClass()
    {
        var old = Parser.ParseSource("""
            namespace Test;
            public sealed class Foo { }
            """);
        var @new = Parser.ParseSource("namespace Test;");

        var diff = Differ.Diff(old, @new, "v1", "v2");

        var change = Assert.Single(diff.Changes, c =>
            c.Kind == ChangeKind.TypeRemoved &&
            c.TypeName == "Foo");
        Assert.Equal(Severity.Breaking, change.Severity);
    }

    [Fact]
    public void DetectsAddedClass()
    {
        var old = Parser.ParseSource("namespace Test;");
        var @new = Parser.ParseSource("""
            namespace Test;
            public sealed class Bar { }
            """);

        var diff = Differ.Diff(old, @new, "v1", "v2");

        var change = Assert.Single(diff.Changes, c =>
            c.Kind == ChangeKind.TypeAdded &&
            c.TypeName == "Bar");
        Assert.Equal(Severity.Additive, change.Severity);
    }

    [Fact]
    public void DetectsRemovedProperty()
    {
        var old = Parser.ParseSource("""
            namespace Test;
            public sealed class Foo
            {
                public string Name { get; set; } = null!;
                public int Age { get; set; }
            }
            """);
        var @new = Parser.ParseSource("""
            namespace Test;
            public sealed class Foo
            {
                public string Name { get; set; } = null!;
            }
            """);

        var diff = Differ.Diff(old, @new, "v1", "v2");

        Assert.Single(diff.Changes, c =>
            c.Kind == ChangeKind.PropertyRemoved &&
            c.TypeName == "Foo" &&
            c.Field == "Age");
    }

    [Fact]
    public void DetectsPropertyTypeChange()
    {
        var old = Parser.ParseSource("""
            namespace Test;
            public sealed class Foo
            {
                public string Name { get; set; } = null!;
            }
            """);
        var @new = Parser.ParseSource("""
            namespace Test;
            public sealed class Foo
            {
                public int Name { get; set; }
            }
            """);

        var diff = Differ.Diff(old, @new, "v1", "v2");

        var change = Assert.Single(diff.Changes, c =>
            c.Kind == ChangeKind.PropertyTypeChanged &&
            c.TypeName == "Foo" &&
            c.Field == "Name");
        Assert.Equal(Severity.Breaking, change.Severity);
    }

    [Fact]
    public void DetectsPropertyBecameRequired()
    {
        var old = Parser.ParseSource("""
            namespace Test;
            public sealed class FooRequest
            {
                public string? Name { get; set; }
            }
            """);
        var @new = Parser.ParseSource("""
            namespace Test;
            public sealed class FooRequest
            {
                public string Name { get; set; } = null!;
            }
            """);

        var diff = Differ.Diff(old, @new, "v1", "v2");

        var change = Assert.Single(diff.Changes, c =>
            c.Kind == ChangeKind.PropertyBecameRequired &&
            c.TypeName == "FooRequest" &&
            c.Field == "Name");
        Assert.Equal(Severity.Breaking, change.Severity);
    }

    [Fact]
    public void DetectsPropertyBecameOptionalOnResponse()
    {
        var old = Parser.ParseSource("""
            namespace Test;
            public sealed class FooResponse
            {
                public string Name { get; set; } = null!;
            }
            """);
        var @new = Parser.ParseSource("""
            namespace Test;
            public sealed class FooResponse
            {
                public string? Name { get; set; }
            }
            """);

        var diff = Differ.Diff(old, @new, "v1", "v2");

        var change = Assert.Single(diff.Changes, c =>
            c.Kind == ChangeKind.PropertyBecameOptional &&
            c.TypeName == "FooResponse" &&
            c.Field == "Name");
        Assert.Equal(Severity.Breaking, change.Severity);
    }

    [Fact]
    public void DetectsEnumMemberRemoved()
    {
        var old = Parser.ParseSource("""
            using System.Text.Json.Serialization;
            namespace Test;
            [JsonConverter(typeof(JsonStringEnumConverter))]
            public enum StatusEnum
            {
                [JsonPropertyName("ACTIVE")] ACTIVE,
                [JsonPropertyName("COMPLETED")] COMPLETED,
            }
            """);
        var @new = Parser.ParseSource("""
            using System.Text.Json.Serialization;
            namespace Test;
            [JsonConverter(typeof(JsonStringEnumConverter))]
            public enum StatusEnum
            {
                [JsonPropertyName("ACTIVE")] ACTIVE,
            }
            """);

        var diff = Differ.Diff(old, @new, "v1", "v2");

        var change = Assert.Single(diff.Changes, c =>
            c.Kind == ChangeKind.EnumMemberRemoved &&
            c.TypeName == "StatusEnum" &&
            c.Field == "COMPLETED");
        Assert.Equal(Severity.Breaking, change.Severity);
    }

    [Fact]
    public void DetectsEnumMemberAdded()
    {
        var old = Parser.ParseSource("""
            using System.Text.Json.Serialization;
            namespace Test;
            [JsonConverter(typeof(JsonStringEnumConverter))]
            public enum StatusEnum
            {
                [JsonPropertyName("ACTIVE")] ACTIVE,
            }
            """);
        var @new = Parser.ParseSource("""
            using System.Text.Json.Serialization;
            namespace Test;
            [JsonConverter(typeof(JsonStringEnumConverter))]
            public enum StatusEnum
            {
                [JsonPropertyName("ACTIVE")] ACTIVE,
                [JsonPropertyName("COMPLETED")] COMPLETED,
            }
            """);

        var diff = Differ.Diff(old, @new, "v1", "v2");

        var change = Assert.Single(diff.Changes, c =>
            c.Kind == ChangeKind.EnumMemberAdded &&
            c.TypeName == "StatusEnum" &&
            c.Field == "COMPLETED");
        Assert.Equal(Severity.Additive, change.Severity);
    }

    [Fact]
    public void DetectsMethodRemoved()
    {
        var old = Parser.ParseSource("""
            namespace Test;
            public partial class CamundaClient
            {
                /// <remarks>Operation: activate</remarks>
                public async Task ActivateAsync(string id, CancellationToken ct = default) { }

                /// <remarks>Operation: delete</remarks>
                public async Task DeleteAsync(string id, CancellationToken ct = default) { }
            }
            """);
        var @new = Parser.ParseSource("""
            namespace Test;
            public partial class CamundaClient
            {
                /// <remarks>Operation: activate</remarks>
                public async Task ActivateAsync(string id, CancellationToken ct = default) { }
            }
            """);

        var diff = Differ.Diff(old, @new, "v1", "v2");

        var change = Assert.Single(diff.Changes, c =>
            c.Kind == ChangeKind.MethodRemoved &&
            c.Field == "DeleteAsync");
        Assert.Equal(Severity.Breaking, change.Severity);
    }

    [Fact]
    public void DetectsMethodSignatureChange()
    {
        var old = Parser.ParseSource("""
            namespace Test;
            public partial class CamundaClient
            {
                /// <remarks>Operation: get</remarks>
                public async Task<FooResult> GetFooAsync(string id, CancellationToken ct = default) => null!;
            }
            public sealed class FooResult { }
            public sealed class FooRequest { }
            """);
        var @new = Parser.ParseSource("""
            namespace Test;
            public partial class CamundaClient
            {
                /// <remarks>Operation: get</remarks>
                public async Task<FooResult> GetFooAsync(string id, FooRequest body, CancellationToken ct = default) => null!;
            }
            public sealed class FooResult { }
            public sealed class FooRequest { }
            """);

        var diff = Differ.Diff(old, @new, "v1", "v2");

        var change = Assert.Single(diff.Changes, c =>
            c.Kind == ChangeKind.MethodParameterChanged &&
            c.Field == "GetFooAsync");
        Assert.Equal(Severity.Breaking, change.Severity);
    }

    [Fact]
    public void DetectsPolymorphicDerivedTypeRemoved()
    {
        var old = Parser.ParseSource("""
            using System.Text.Json.Serialization;
            namespace Test;
            [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
            [JsonDerivedType(typeof(Alpha), "alpha")]
            [JsonDerivedType(typeof(Beta), "beta")]
            public abstract class BaseType { }
            public sealed class Alpha : BaseType { }
            public sealed class Beta : BaseType { }
            """);
        var @new = Parser.ParseSource("""
            using System.Text.Json.Serialization;
            namespace Test;
            [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
            [JsonDerivedType(typeof(Alpha), "alpha")]
            public abstract class BaseType { }
            public sealed class Alpha : BaseType { }
            """);

        var diff = Differ.Diff(old, @new, "v1", "v2");

        Assert.Contains(diff.Changes, c =>
            c.Kind == ChangeKind.PolymorphicDerivedTypeRemoved &&
            c.TypeName == "BaseType" &&
            c.Field == "Beta" &&
            c.Severity == Severity.Breaking);
    }

    [Fact]
    public void DetectsImplicitConversionRemoved()
    {
        var old = Parser.ParseSource("""
            namespace Test;
            public readonly record struct ScopeKey
            {
                public string Value { get; }
                private ScopeKey(string value) => Value = value;
                public static implicit operator ScopeKey(FooKey key) => new ScopeKey(key.Value);
                public static implicit operator ScopeKey(BarKey key) => new ScopeKey(key.Value);
            }
            public readonly record struct FooKey { public string Value { get; } }
            public readonly record struct BarKey { public string Value { get; } }
            """);
        var @new = Parser.ParseSource("""
            namespace Test;
            public readonly record struct ScopeKey
            {
                public string Value { get; }
                private ScopeKey(string value) => Value = value;
                public static implicit operator ScopeKey(FooKey key) => new ScopeKey(key.Value);
            }
            public readonly record struct FooKey { public string Value { get; } }
            """);

        var diff = Differ.Diff(old, @new, "v1", "v2");

        Assert.Contains(diff.Changes, c =>
            c.Kind == ChangeKind.StructImplicitConversionRemoved &&
            c.TypeName == "ScopeKey" &&
            c.OldValue == "BarKey" &&
            c.Severity == Severity.Breaking);
    }

    [Fact]
    public void NoChangesWhenIdentical()
    {
        var source = """
            namespace Test;
            public sealed class Foo { public string Name { get; set; } = null!; }
            """;
        var old = Parser.ParseSource(source);
        var @new = Parser.ParseSource(source);

        var diff = Differ.Diff(old, @new, "v1", "v2");

        Assert.Empty(diff.Changes);
        Assert.Equal(0, diff.Breaking);
    }

    [Fact]
    public void AddingRequiredFieldToRequestIsBreaking()
    {
        var old = Parser.ParseSource("""
            namespace Test;
            public sealed class CreateRequest
            {
                public string Name { get; set; } = null!;
            }
            """);
        var @new = Parser.ParseSource("""
            namespace Test;
            public sealed class CreateRequest
            {
                public string Name { get; set; } = null!;
                public int RequiredId { get; set; }
            }
            """);

        var diff = Differ.Diff(old, @new, "v1", "v2");

        var change = Assert.Single(diff.Changes, c =>
            c.Kind == ChangeKind.PropertyAdded &&
            c.Field == "RequiredId");
        Assert.Equal(Severity.Breaking, change.Severity);
    }

    [Fact]
    public void AddingOptionalFieldToRequestIsAdditive()
    {
        var old = Parser.ParseSource("""
            namespace Test;
            public sealed class CreateRequest
            {
                public string Name { get; set; } = null!;
            }
            """);
        var @new = Parser.ParseSource("""
            namespace Test;
            public sealed class CreateRequest
            {
                public string Name { get; set; } = null!;
                public int? OptionalId { get; set; }
            }
            """);

        var diff = Differ.Diff(old, @new, "v1", "v2");

        var change = Assert.Single(diff.Changes, c =>
            c.Kind == ChangeKind.PropertyAdded &&
            c.Field == "OptionalId");
        Assert.Equal(Severity.Additive, change.Severity);
    }

    [Fact]
    public void DetectsJsonPropertyNameChange()
    {
        var old = Parser.ParseSource("""
            namespace Test;
            public sealed class Foo
            {
                [System.Text.Json.Serialization.JsonPropertyName("old_name")]
                public string Name { get; set; } = null!;
            }
            """);
        var @new = Parser.ParseSource("""
            namespace Test;
            public sealed class Foo
            {
                [System.Text.Json.Serialization.JsonPropertyName("new_name")]
                public string Name { get; set; } = null!;
            }
            """);

        var diff = Differ.Diff(old, @new, "v1", "v2");

        var change = Assert.Single(diff.Changes, c =>
            c.Kind == ChangeKind.PropertyJsonNameChanged &&
            c.OldValue == "old_name" &&
            c.NewValue == "new_name");
        Assert.Equal(Severity.Breaking, change.Severity);
    }
}
