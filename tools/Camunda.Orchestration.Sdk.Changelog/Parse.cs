using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Camunda.Orchestration.Sdk.Changelog;

/// <summary>
/// Extracted API surface from generated C# files.
/// </summary>
public sealed class ApiSurface
{
    public Dictionary<string, ClassInfo> Classes { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, EnumInfo> Enums { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, StructInfo> Structs { get; } = new(StringComparer.Ordinal);
    public List<MethodInfo> ClientMethods { get; } = [];
}

public sealed class ClassInfo
{
    public required string Name { get; init; }
    public string? BaseClass { get; init; }
    public bool IsAbstract { get; init; }
    public bool IsSealed { get; init; }
    public List<string> Interfaces { get; init; } = [];
    public List<PropertyInfo> Properties { get; init; } = [];
    public PolymorphicInfo? Polymorphic { get; init; }
}

public sealed class PolymorphicInfo
{
    public required string Discriminator { get; init; }
    public List<DerivedTypeInfo> DerivedTypes { get; init; } = [];
}

public sealed class DerivedTypeInfo
{
    public required string TypeName { get; init; }
    public required string DiscriminatorValue { get; init; }
}

public sealed class PropertyInfo
{
    public required string Name { get; init; }
    public required string TypeExpr { get; init; }
    public required string JsonName { get; init; }
    public bool IsNullable { get; init; }
    public bool IsRequired { get; init; }
}

public sealed class EnumInfo
{
    public required string Name { get; init; }
    public List<EnumMemberInfo> Members { get; init; } = [];
}

public sealed class EnumMemberInfo
{
    public required string Name { get; init; }
    public string? JsonName { get; init; }
    public bool IsObsolete { get; init; }
}

public sealed class StructInfo
{
    public required string Name { get; init; }
    public bool IsReadOnly { get; init; }
    public bool IsRecord { get; init; }
    public List<string> Interfaces { get; init; } = [];
    public List<PropertyInfo> Properties { get; init; } = [];
    public List<ImplicitConversionInfo> ImplicitConversions { get; init; } = [];
}

public sealed class ImplicitConversionInfo
{
    public required string FromType { get; init; }
    public required string ToType { get; init; }
}

public sealed class MethodInfo
{
    public required string Name { get; init; }
    public required string ReturnType { get; init; }
    public List<ParameterInfo> Parameters { get; init; } = [];
    public string? OperationId { get; init; }
}

public sealed class ParameterInfo
{
    public required string Name { get; init; }
    public required string TypeExpr { get; init; }
    public bool HasDefault { get; init; }
}

/// <summary>
/// Parses generated C# source files using Roslyn to extract the public API surface.
/// </summary>
public static class Parser
{
    public static ApiSurface ParseFiles(IEnumerable<string> filePaths)
    {
        var surface = new ApiSurface();
        foreach (var path in filePaths)
        {
            var source = File.ReadAllText(path);
            ParseSource(source, surface);
        }
        return surface;
    }

    public static ApiSurface ParseSource(string source)
    {
        var surface = new ApiSurface();
        ParseSource(source, surface);
        return surface;
    }

    private static void ParseSource(string source, ApiSurface surface)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetCompilationUnitRoot();

        foreach (var member in GetTypesRecursive(root))
        {
            switch (member)
            {
                case ClassDeclarationSyntax classDecl:
                    ProcessClass(classDecl, surface);
                    break;
                case EnumDeclarationSyntax enumDecl:
                    ProcessEnum(enumDecl, surface);
                    break;
                case RecordDeclarationSyntax recordDecl when recordDecl.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword):
                    ProcessStruct(recordDecl, surface);
                    break;
                case StructDeclarationSyntax structDecl:
                    ProcessPlainStruct(structDecl, surface);
                    break;
            }
        }
    }

    private static IEnumerable<MemberDeclarationSyntax> GetTypesRecursive(SyntaxNode node)
    {
        foreach (var child in node.ChildNodes())
        {
            if (child is BaseTypeDeclarationSyntax typeDecl)
            {
                yield return typeDecl;
            }
            else if (child is BaseNamespaceDeclarationSyntax ns)
            {
                foreach (var nested in GetTypesRecursive(ns))
                    yield return nested;
            }
        }
    }

    private static void ProcessClass(ClassDeclarationSyntax classDecl, ApiSurface surface)
    {
        if (!IsPublic(classDecl.Modifiers))
            return;

        var name = classDecl.Identifier.Text;

        // Check if this is the partial CamundaClient class with methods
        if (name == "CamundaClient" && classDecl.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            ExtractClientMethods(classDecl, surface);
            return;
        }

        var info = new ClassInfo
        {
            Name = name,
            IsAbstract = classDecl.Modifiers.Any(SyntaxKind.AbstractKeyword),
            IsSealed = classDecl.Modifiers.Any(SyntaxKind.SealedKeyword),
            BaseClass = GetBaseClass(classDecl.BaseList),
            Interfaces = GetInterfaces(classDecl.BaseList),
            Properties = ExtractProperties(classDecl),
            Polymorphic = ExtractPolymorphicInfo(classDecl),
        };

        surface.Classes[name] = info;
    }

    private static void ProcessEnum(EnumDeclarationSyntax enumDecl, ApiSurface surface)
    {
        if (!IsPublic(enumDecl.Modifiers))
            return;

        var name = enumDecl.Identifier.Text;
        var members = new List<EnumMemberInfo>();

        foreach (var member in enumDecl.Members)
        {
            var jsonName = GetAttributeArgument(member.AttributeLists, "JsonPropertyName");
            var isObsolete = HasAttribute(member.AttributeLists, "Obsolete");

            members.Add(new EnumMemberInfo
            {
                Name = member.Identifier.Text,
                JsonName = jsonName,
                IsObsolete = isObsolete,
            });
        }

        surface.Enums[name] = new EnumInfo { Name = name, Members = members };
    }

    private static void ProcessStruct(RecordDeclarationSyntax recordDecl, ApiSurface surface)
    {
        if (!IsPublic(recordDecl.Modifiers))
            return;

        var name = recordDecl.Identifier.Text;
        var info = new StructInfo
        {
            Name = name,
            IsReadOnly = recordDecl.Modifiers.Any(SyntaxKind.ReadOnlyKeyword),
            IsRecord = true,
            Interfaces = GetInterfaces(recordDecl.BaseList),
            Properties = ExtractProperties(recordDecl),
            ImplicitConversions = ExtractImplicitConversions(recordDecl, name),
        };

        surface.Structs[name] = info;
    }

    private static void ProcessPlainStruct(StructDeclarationSyntax structDecl, ApiSurface surface)
    {
        if (!IsPublic(structDecl.Modifiers))
            return;

        var name = structDecl.Identifier.Text;
        var info = new StructInfo
        {
            Name = name,
            IsReadOnly = structDecl.Modifiers.Any(SyntaxKind.ReadOnlyKeyword),
            IsRecord = false,
            Interfaces = GetInterfaces(structDecl.BaseList),
            Properties = ExtractProperties(structDecl),
            ImplicitConversions = ExtractImplicitConversions(structDecl, name),
        };

        surface.Structs[name] = info;
    }

    private static void ExtractClientMethods(TypeDeclarationSyntax classDecl, ApiSurface surface)
    {
        foreach (var method in classDecl.Members.OfType<MethodDeclarationSyntax>())
        {
            if (!IsPublic(method.Modifiers))
                continue;

            var returnType = method.ReturnType.ToString();
            var parameters = new List<ParameterInfo>();

            foreach (var param in method.ParameterList.Parameters)
            {
                // Skip CancellationToken — it's infrastructure, not API surface
                var typeStr = param.Type?.ToString() ?? "object";
                if (typeStr == "CancellationToken")
                    continue;

                parameters.Add(new ParameterInfo
                {
                    Name = param.Identifier.Text,
                    TypeExpr = typeStr,
                    HasDefault = param.Default != null,
                });
            }

            // Extract operationId from <remarks>
            var operationId = ExtractOperationId(method);

            surface.ClientMethods.Add(new MethodInfo
            {
                Name = method.Identifier.Text,
                ReturnType = returnType,
                Parameters = parameters,
                OperationId = operationId,
            });
        }
    }

    private static string? ExtractOperationId(MethodDeclarationSyntax method)
    {
        var trivia = method.GetLeadingTrivia();
        foreach (var t in trivia)
        {
            if (t.HasStructure && t.GetStructure() is DocumentationCommentTriviaSyntax doc)
            {
                foreach (var xml in doc.Content.OfType<XmlElementSyntax>())
                {
                    if (xml.StartTag.Name.ToString() == "remarks")
                    {
                        var text = xml.Content.ToString().Trim();
                        // Pattern: "Operation: operationName"
                        const string prefix = "Operation: ";
                        if (text.StartsWith(prefix, StringComparison.Ordinal))
                            return text[prefix.Length..].Trim();
                    }
                }
            }
        }
        return null;
    }

    private static List<PropertyInfo> ExtractProperties(TypeDeclarationSyntax typeDecl)
    {
        var props = new List<PropertyInfo>();
        foreach (var prop in typeDecl.Members.OfType<PropertyDeclarationSyntax>())
        {
            if (!IsPublic(prop.Modifiers))
                continue;

            var typeStr = prop.Type.ToString();
            var jsonName = GetAttributeArgument(prop.AttributeLists, "JsonPropertyName") ?? prop.Identifier.Text;

            // Nullable if type ends with ? or if initializer is = null
            var isNullable = typeStr.EndsWith('?');

            // Required: non-nullable value types are always required;
            // non-nullable reference types with = null! initializer are required
            var isRequired = !isNullable;

            props.Add(new PropertyInfo
            {
                Name = prop.Identifier.Text,
                TypeExpr = typeStr,
                JsonName = jsonName,
                IsNullable = isNullable,
                IsRequired = isRequired,
            });
        }
        return props;
    }

    private static List<ImplicitConversionInfo> ExtractImplicitConversions(TypeDeclarationSyntax typeDecl, string typeName)
    {
        var conversions = new List<ImplicitConversionInfo>();
        foreach (var op in typeDecl.Members.OfType<ConversionOperatorDeclarationSyntax>())
        {
            if (!op.ImplicitOrExplicitKeyword.IsKind(SyntaxKind.ImplicitKeyword))
                continue;
            if (!IsPublicAndStatic(op.Modifiers))
                continue;

            var toType = op.Type.ToString();
            // The parameter type is the "from" type
            var fromType = op.ParameterList.Parameters.FirstOrDefault()?.Type?.ToString();
            if (fromType != null)
            {
                conversions.Add(new ImplicitConversionInfo { FromType = fromType, ToType = toType });
            }
        }
        return conversions;
    }

    private static PolymorphicInfo? ExtractPolymorphicInfo(ClassDeclarationSyntax classDecl)
    {
        var discriminator = GetNamedAttributeArgument(classDecl.AttributeLists, "JsonPolymorphic", "TypeDiscriminatorPropertyName");
        if (discriminator == null)
            return null;

        var derivedTypes = new List<DerivedTypeInfo>();
        foreach (var attrList in classDecl.AttributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                if (attr.Name.ToString() != "JsonDerivedType")
                    continue;

                var args = attr.ArgumentList?.Arguments.ToList();
                if (args == null || args.Count < 2)
                    continue;

                // First arg: typeof(TypeName)
                var typeArg = args[0].Expression;
                string? typeName = null;
                if (typeArg is TypeOfExpressionSyntax typeOf)
                    typeName = typeOf.Type.ToString();

                // Second arg: "discriminatorValue"
                var valueArg = args[1].Expression;
                string? discValue = null;
                if (valueArg is LiteralExpressionSyntax literal)
                    discValue = literal.Token.ValueText;

                if (typeName != null && discValue != null)
                {
                    derivedTypes.Add(new DerivedTypeInfo { TypeName = typeName, DiscriminatorValue = discValue });
                }
            }
        }

        return new PolymorphicInfo { Discriminator = discriminator, DerivedTypes = derivedTypes };
    }

    private static string? GetBaseClass(BaseListSyntax? baseList)
    {
        if (baseList == null)
            return null;
        foreach (var baseType in baseList.Types)
        {
            var name = baseType.Type.ToString();
            // Skip known interfaces (start with I followed by uppercase)
            if (name.StartsWith('I') && name.Length > 1 && char.IsUpper(name[1]))
                continue;
            // Skip global:: qualified interface names
            if (name.Contains("ICamundaKey") || name.Contains("ITenantIdSettable"))
                continue;
            return name;
        }
        return null;
    }

    private static List<string> GetInterfaces(BaseListSyntax? baseList)
    {
        if (baseList == null)
            return [];
        var interfaces = new List<string>();
        foreach (var baseType in baseList.Types)
        {
            var name = baseType.Type.ToString();
            if (name.StartsWith('I') && name.Length > 1 && char.IsUpper(name[1]))
                interfaces.Add(name);
            else if (name.Contains("ICamundaKey") || name.Contains("ITenantIdSettable"))
                interfaces.Add(name);
        }
        return interfaces;
    }

    private static string? GetAttributeArgument(SyntaxList<AttributeListSyntax> attrLists, string attrName)
    {
        foreach (var attrList in attrLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                var name = attr.Name.ToString();
                if (name != attrName && !name.EndsWith("." + attrName, StringComparison.Ordinal))
                    continue;
                var arg = attr.ArgumentList?.Arguments.FirstOrDefault();
                if (arg?.Expression is LiteralExpressionSyntax literal)
                    return literal.Token.ValueText;
            }
        }
        return null;
    }

    private static string? GetNamedAttributeArgument(SyntaxList<AttributeListSyntax> attrLists, string attrName, string argName)
    {
        foreach (var attrList in attrLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                var name = attr.Name.ToString();
                if (name != attrName && !name.EndsWith("." + attrName, StringComparison.Ordinal))
                    continue;
                if (attr.ArgumentList == null)
                    continue;

                foreach (var arg in attr.ArgumentList.Arguments)
                {
                    if (arg.NameEquals?.Name.ToString() == argName &&
                        arg.Expression is LiteralExpressionSyntax literal)
                    {
                        return literal.Token.ValueText;
                    }
                }
            }
        }
        return null;
    }

    private static bool HasAttribute(SyntaxList<AttributeListSyntax> attrLists, string attrName)
    {
        return attrLists.SelectMany(al => al.Attributes).Any(a =>
        {
            var name = a.Name.ToString();
            return name == attrName || name.EndsWith("." + attrName, StringComparison.Ordinal);
        });
    }

    private static bool IsPublic(SyntaxTokenList modifiers)
    {
        return modifiers.Any(SyntaxKind.PublicKeyword);
    }

    private static bool IsPublicAndStatic(SyntaxTokenList modifiers)
    {
        return modifiers.Any(SyntaxKind.PublicKeyword) && modifiers.Any(SyntaxKind.StaticKeyword);
    }
}
