using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;

namespace Camunda.Orchestration.Sdk.Generator;

/// <summary>
/// Generates C# model classes and client methods from a bundled OpenAPI spec.
/// Mirrors the JS SDK's generate-class-methods.ts pipeline.
/// </summary>
internal static class CSharpClientGenerator
{
    /// <summary>
    /// Generate C# source files from the bundled OpenAPI spec.
    /// </summary>
    public static void Generate(string specPath, string metadataPath, string outputDir)
    {
        Console.WriteLine($"[generator] Reading spec from {specPath}");
        Console.WriteLine($"[generator] Reading metadata from {metadataPath}");

        using var stream = File.OpenRead(specPath);
        var reader = new OpenApiStreamReader();
        var doc = reader.Read(stream, out var diagnostic);

        if (diagnostic.Errors.Count > 0)
        {
            foreach (var error in diagnostic.Errors)
                Console.Error.WriteLine($"[generator] OpenAPI error: {error.Message}");
        }

        var metadata = SpecMetadata.Load(metadataPath);
        Console.WriteLine($"[generator] Metadata: {metadata.Operations.Count} operations, {metadata.SemanticKeys.Count} semantic keys");

        Directory.CreateDirectory(outputDir);

        // Load operation → example map (optional — generation succeeds without it)
        var examplesDir = Path.GetFullPath(Path.Combine(outputDir, "..", "..", "..", "examples"));
        var operationExamples = LoadOperationExamples(examplesDir);
        if (operationExamples.Count > 0)
            Console.WriteLine($"[generator] Loaded {operationExamples.Count} operation examples");

        // Collect operations first — this also discovers inline schemas
        var inlineSchemas = new Dictionary<string, OpenApiSchema>();
        var operations = CollectOperations(doc, metadata, inlineSchemas);

        // Build set of schema names used as request bodies (ITenantIdSettable only applies to these)
        var requestSchemaNames = BuildRequestSchemaNames(doc, operations);

        // Generate models (component schemas + inline schemas)
        var modelsCode = GenerateModels(doc, metadata, inlineSchemas, requestSchemaNames);
        File.WriteAllText(Path.Combine(outputDir, "Models.Generated.cs"), modelsCode);
        Console.WriteLine($"[generator] Generated Models.Generated.cs ({doc.Components?.Schemas?.Count ?? 0} component schemas, {inlineSchemas.Count} inline schemas)");

        // Generate client methods
        var clientCode = GenerateClientMethods(operations, operationExamples);
        File.WriteAllText(Path.Combine(outputDir, "CamundaClient.Generated.cs"), clientCode);
        Console.WriteLine($"[generator] Generated CamundaClient.Generated.cs ({operations.Count} operations)");

        // Generate spec hash constant — fail fast if missing or invalid
        if (string.IsNullOrWhiteSpace(metadata.SpecHash) ||
            !Regex.IsMatch(metadata.SpecHash, @"^sha256:[0-9a-fA-F]{64}$"))
        {
            throw new InvalidOperationException(
                $"[generator] specHash is missing or invalid (expected sha256:<64 hex chars>): \"{metadata.SpecHash}\"");
        }

        var specHashCode = GenerateSpecHash(metadata.SpecHash);
        File.WriteAllText(Path.Combine(outputDir, "SpecHash.Generated.cs"), specHashCode);
        Console.WriteLine($"[generator] Generated SpecHash.Generated.cs (specHash: {metadata.SpecHash})");
    }

    private static List<OperationMeta> CollectOperations(OpenApiDocument doc, SpecMetadata metadata, Dictionary<string, OpenApiSchema> inlineSchemas)
    {
        var ops = new List<OperationMeta>();
        var (oneOfParents, _, _) = BuildOneOfMaps(doc);

        foreach (var (path, pathItem) in doc.Paths)
        {
            foreach (var (verb, op) in pathItem.Operations)
            {
                if (string.IsNullOrEmpty(op.OperationId))
                    continue;

                var opId = SanitizeOperationId(op.OperationId);
                var pathParams = op.Parameters
                    .Where(p => p.In == ParameterLocation.Path)
                    .Select(p => new ParamMeta(p.Name, MapType(p.Schema), true))
                    .ToList();
                var queryParams = op.Parameters
                    .Where(p => p.In == ParameterLocation.Query)
                    .Select(p => new ParamMeta(p.Name, MapType(p.Schema), p.Required))
                    .ToList();

                var hasBody = op.RequestBody?.Content?.Any(c =>
                    (c.Key.Contains("json", StringComparison.OrdinalIgnoreCase) && c.Value.Schema != null) ||
                    c.Key.Contains("multipart", StringComparison.OrdinalIgnoreCase)) == true;

                var bodySchemaRef = hasBody ? GetBodySchemaRef(op) : null;
                var responseSchemaRef = GetResponseSchemaRef(op);

                // Use pre-computed metadata for behavioral flags
                var metaEntry = metadata.GetOperation(op.OperationId);
                var isEventual = metaEntry?.EventuallyConsistent ?? false;
                var isMultipart = op.RequestBody?.Content?.Any(c =>
                    c.Key.Contains("multipart", StringComparison.OrdinalIgnoreCase)) == true;

                var bodyTypeName = bodySchemaRef ?? (hasBody ? opId + "Request" : null);
                var responseTypeName = responseSchemaRef ?? "object";

                // Collect inline request body schema (no $ref → needs a generated class)
                if (hasBody && bodySchemaRef == null && !isMultipart && bodyTypeName != null)
                {
                    var inlineBodySchema = GetInlineBodySchema(op);
                    if (inlineBodySchema != null)
                    {
                        // For inline oneOf bodies, try to find a matching component oneOf schema
                        if (inlineBodySchema.OneOf is { Count: > 0 })
                        {
                            var matchingComponent = FindMatchingOneOfComponent(doc, inlineBodySchema, oneOfParents);
                            if (matchingComponent != null)
                            {
                                bodyTypeName = matchingComponent;
                                // Don't add inline schema — use the component schema instead
                            }
                            else if (!ComponentSchemaExists(doc, bodyTypeName))
                            {
                                inlineSchemas.TryAdd(bodyTypeName, inlineBodySchema);
                            }
                        }
                        else if (!ComponentSchemaExists(doc, bodyTypeName))
                        {
                            inlineSchemas.TryAdd(bodyTypeName, inlineBodySchema);
                        }
                    }
                }

                // Collect inline response schema (no $ref → needs a generated class)
                if (responseSchemaRef != null && !ComponentSchemaExists(doc, responseSchemaRef))
                {
                    var inlineRespSchema = GetInlineResponseSchema(op);
                    if (inlineRespSchema != null)
                        inlineSchemas.TryAdd(responseSchemaRef, inlineRespSchema);
                }

                ops.Add(new OperationMeta
                {
                    OperationId = opId,
                    OriginalOperationId = op.OperationId,
                    Verb = verb,
                    Path = path,
                    PathParams = pathParams,
                    QueryParams = queryParams,
                    HasBody = hasBody,
                    IsMultipart = isMultipart,
                    BodyTypeName = bodyTypeName,
                    ResponseTypeName = responseTypeName,
                    IsVoidResponse = responseSchemaRef == null && GetSuccessStatusCode(op) is 204 or 202,
                    IsEventuallyConsistent = isEventual,
                    HasOptionalTenantIdInBody = metaEntry?.OptionalTenantIdInBody ?? false,
                    Summary = op.Summary,
                    Description = op.Description,
                    Tags = op.Tags?.Select(t => t.Name).ToList() ?? [],
                    IsExemptFromBackpressure = IsExempt(opId),
                });
            }
        }

        ops.Sort((a, b) => string.Compare(a.OperationId, b.OperationId, StringComparison.Ordinal));
        return ops;
    }

    private static string? GetBodySchemaRef(OpenApiOperation op)
    {
        var content = op.RequestBody?.Content;
        if (content == null)
            return null;

        foreach (var (_, mediaType) in content)
        {
            if (mediaType.Schema?.Reference != null)
                return SanitizeTypeName(mediaType.Schema.Reference.Id);
            // For multipart, we generate a special type
        }
        return null;
    }

    private static string? GetResponseSchemaRef(OpenApiOperation op)
    {
        foreach (var (code, response) in op.Responses)
        {
            if (!code.StartsWith('2'))
                continue;
            if (response.Content == null || response.Content.Count == 0)
                return null;

            foreach (var (_, mediaType) in response.Content)
            {
                if (mediaType.Schema?.Reference != null)
                    return SanitizeTypeName(mediaType.Schema.Reference.Id);
                if (mediaType.Schema?.Type == "object" || mediaType.Schema?.Properties?.Count > 0)
                    return SanitizeOperationId(op.OperationId) + "Response";
            }
        }
        return null;
    }

    private static OpenApiSchema? GetInlineBodySchema(OpenApiOperation op)
    {
        var content = op.RequestBody?.Content;
        if (content == null)
            return null;
        foreach (var (ct, mediaType) in content)
        {
            if (!ct.Contains("json", StringComparison.OrdinalIgnoreCase))
                continue;
            if (mediaType.Schema?.Reference == null)
                return mediaType.Schema;
        }
        return null;
    }

    private static OpenApiSchema? GetInlineResponseSchema(OpenApiOperation op)
    {
        foreach (var (code, response) in op.Responses)
        {
            if (!code.StartsWith('2'))
                continue;
            if (response.Content == null)
                continue;
            foreach (var (_, mediaType) in response.Content)
            {
                if (mediaType.Schema?.Reference == null &&
                    (mediaType.Schema?.Type == "object" || mediaType.Schema?.Properties?.Count > 0))
                    return mediaType.Schema;
            }
        }
        return null;
    }

    private static int? GetSuccessStatusCode(OpenApiOperation op)
    {
        foreach (var (code, _) in op.Responses)
        {
            if (int.TryParse(code, out var val) && val is >= 200 and < 300)
                return val;
        }
        return null;
    }

    private static bool IsExempt(string opId) =>
        opId is "CompleteJob" or "FailJob" or "ThrowJobError" or "CompleteUserTask";

    /// <summary>
    /// Check if a type name corresponds to an existing component schema
    /// (accounting for SanitizeTypeName transformation).
    /// </summary>
    private static bool ComponentSchemaExists(OpenApiDocument doc, string sanitizedTypeName)
    {
        if (doc.Components?.Schemas == null)
            return false;
        return doc.Components.Schemas.Keys.Any(k => SanitizeTypeName(k) == sanitizedTypeName);
    }

    /// <summary>
    /// Build maps of oneOf parent→variant relationships for component schemas.
    /// Only includes schemas where ALL oneOf entries are $refs to non-primitive types.
    /// Also collects discriminator info (property name + value mapping) for schemas that declare one.
    /// </summary>
    private static (Dictionary<string, List<string>> oneOfParents, Dictionary<string, string> variantToParent, Dictionary<string, (string PropertyName, Dictionary<string, string> ValueToVariant)> discriminators) BuildOneOfMaps(OpenApiDocument doc)
    {
        var oneOfParents = new Dictionary<string, List<string>>();
        var variantToParent = new Dictionary<string, string>();
        var discriminators = new Dictionary<string, (string PropertyName, Dictionary<string, string> ValueToVariant)>();

        if (doc.Components?.Schemas == null)
            return (oneOfParents, variantToParent, discriminators);

        foreach (var (name, schema) in doc.Components.Schemas)
        {
            if (schema.OneOf is not { Count: > 0 })
                continue;

            // Check: all variants must be $refs to schemas that generate as classes (not primitives)
            var allRefsToClasses = schema.OneOf.All(v =>
                v.Reference != null &&
                doc.Components.Schemas.TryGetValue(v.Reference.Id, out var refSchema) &&
                !IsPrimitiveSchemaResolved(refSchema, doc));

            if (!allRefsToClasses)
                continue;

            var parentName = SanitizeTypeName(name);
            var variants = schema.OneOf
                .Where(v => v.Reference != null)
                .Select(v => SanitizeTypeName(v.Reference.Id))
                .ToList();

            oneOfParents[parentName] = variants;
            foreach (var v in variants)
                variantToParent.TryAdd(v, parentName);

            // Collect discriminator mapping if present
            if (schema.Discriminator is { PropertyName: { Length: > 0 } } disc && disc.Mapping?.Count > 0)
            {
                var valueToVariant = new Dictionary<string, string>();
                foreach (var (discValue, refPath) in disc.Mapping)
                {
                    // Mapping values are "#/components/schemas/TypeName" — extract the type name
                    var schemaName = refPath.Contains('/') ? refPath.Substring(refPath.LastIndexOf('/') + 1) : refPath;
                    valueToVariant[discValue] = SanitizeTypeName(schemaName);
                }
                discriminators[parentName] = (disc.PropertyName, valueToVariant);
            }
        }

        return (oneOfParents, variantToParent, discriminators);
    }

    /// <summary>
    /// For an inline oneOf body schema, find a matching component oneOf schema
    /// by comparing the required fields of inline variants to component variants.
    /// </summary>
    private static string? FindMatchingOneOfComponent(
        OpenApiDocument doc,
        OpenApiSchema inlineSchema,
        Dictionary<string, List<string>> oneOfParents)
    {
        if (inlineSchema.OneOf is not { Count: > 0 })
            return null;
        if (doc.Components?.Schemas == null)
            return null;

        // Collect required field sets from inline variants
        var inlineRequiredSets = inlineSchema.OneOf
            .Select(v => new HashSet<string>(v.Required ?? (ISet<string>)new HashSet<string>()))
            .Where(s => s.Count > 0)
            .ToList();

        if (inlineRequiredSets.Count != inlineSchema.OneOf.Count || inlineRequiredSets.Count == 0)
            return null;

        // Search component oneOf schemas for matching required fields
        foreach (var (parentName, variantNames) in oneOfParents)
        {
            if (variantNames.Count != inlineRequiredSets.Count)
                continue;

            // Get the required fields from each component variant
            var componentRequiredSets = new List<HashSet<string>>();
            bool allResolved = true;
            foreach (var variantName in variantNames)
            {
                // Find the original schema name
                var entry = doc.Components.Schemas.FirstOrDefault(kv => SanitizeTypeName(kv.Key) == variantName);
                if (entry.Value == null)
                { allResolved = false; break; }
                componentRequiredSets.Add(new HashSet<string>(entry.Value.Required ?? (ISet<string>)new HashSet<string>()));
            }
            if (!allResolved)
                continue;

            // Check if the required field sets match (in any order)
            var matched = new bool[inlineRequiredSets.Count];
            bool allMatched = true;
            foreach (var componentSet in componentRequiredSets)
            {
                bool found = false;
                for (int i = 0; i < inlineRequiredSets.Count; i++)
                {
                    if (!matched[i] && componentSet.SetEquals(inlineRequiredSets[i]))
                    {
                        matched[i] = true;
                        found = true;
                        break;
                    }
                }
                if (!found)
                { allMatched = false; break; }
            }

            if (allMatched)
                return parentName;
        }

        return null;
    }

    /// <summary>
    /// For a oneOf schema with mixed primitive/class variants (FilterProperty pattern),
    /// finds and returns the resolved schema of the non-primitive variant.
    /// Returns null if the pattern doesn't match (e.g. all variants are primitive).
    /// </summary>
    private static OpenApiSchema? FindNonPrimitiveOneOfVariant(OpenApiSchema schema, OpenApiDocument doc)
    {
        if (schema.OneOf is not { Count: 2 })
            return null;

        foreach (var variant in schema.OneOf)
        {
            // Resolve $ref if present
            var resolved = variant.Reference != null && doc.Components?.Schemas != null
                && doc.Components.Schemas.TryGetValue(variant.Reference.Id, out var refSchema)
                ? refSchema : variant;

            // Skip primitive variants (inline primitives or allOf-wrapped primitives/enums)
            if (IsPrimitiveSchemaResolved(resolved, doc))
                continue;

            // This is the non-primitive (class) variant
            return resolved;
        }

        return null;
    }

    /// <summary>
    /// Build a set of schema type names that are used as request bodies.
    /// Includes oneOf variant names when a parent is used as a body type.
    /// </summary>
    private static HashSet<string> BuildRequestSchemaNames(
        OpenApiDocument doc, List<OperationMeta> operations)
    {
        var (oneOfParents, _, _) = BuildOneOfMaps(doc);
        var names = new HashSet<string>();

        foreach (var op in operations)
        {
            if (op.BodyTypeName == null)
                continue;
            names.Add(op.BodyTypeName);

            // If the body type is a oneOf parent, include all its variants
            if (oneOfParents.TryGetValue(op.BodyTypeName, out var variants))
            {
                foreach (var v in variants)
                    names.Add(v);
            }
        }

        return names;
    }

    private static string GenerateModels(OpenApiDocument doc, SpecMetadata metadata, Dictionary<string, OpenApiSchema> inlineSchemas, HashSet<string> requestSchemaNames)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("// Generated from OpenAPI spec. DO NOT EDIT.");
        sb.AppendLine();
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System.Text.Json.Serialization;");
        sb.AppendLine();
        sb.AppendLine("namespace Camunda.Orchestration.Sdk;");
        sb.AppendLine();

        if (doc.Components?.Schemas == null)
            return sb.ToString();

        var (oneOfParents, variantToParent, discriminators) = BuildOneOfMaps(doc);

        foreach (var (name, schema) in doc.Components.Schemas.OrderBy(kv => kv.Key))
        {
            var typeName = SanitizeTypeName(name);

            // Skip array-type component schemas — they are resolved inline in MapType
            if (schema.Type == "array")
                continue;

            // Union of semantic keys — generate with implicit conversion operators and cross-type equality
            if (metadata.IsSemanticKeyUnion(name))
            {
                var branches = metadata.GetSemanticKeyUnionBranches(name)!;
                GenerateSemanticKeyUnionStruct(sb, typeName, schema, doc, branches);
                continue;
            }

            // Skip schemas that are semantic keys or primitive scalars — generate as domain structs
            if (metadata.IsSemanticKey(name) || IsPrimitiveSchemaResolved(schema, doc))
            {
                GenerateDomainStruct(sb, typeName, schema, doc);
                continue;
            }

            if (schema.Enum?.Count > 0 && schema.Type == "string")
            {
                // Generate enum
                sb.AppendLine($"/// <summary>");
                AppendXmlDocLines(sb, schema.Description ?? name, "");
                sb.AppendLine($"/// </summary>");
                sb.AppendLine($"[JsonConverter(typeof(JsonStringEnumConverter))]");
                sb.AppendLine($"public enum {typeName}");
                sb.AppendLine("{");
                foreach (var e in schema.Enum.OfType<Microsoft.OpenApi.Any.OpenApiString>())
                {
                    var enumName = ToPascalCase(e.Value);
                    var deprecatedVersion = metadata.GetDeprecatedEnumMemberVersion(name, e.Value);
                    if (deprecatedVersion != null)
                    {
                        sb.AppendLine($"    [Obsolete(\"Deprecated since {deprecatedVersion}\")]");
                    }
                    sb.AppendLine($"    [JsonPropertyName(\"{e.Value}\")]");
                    sb.AppendLine($"    {enumName},");
                }
                sb.AppendLine("}");
                sb.AppendLine();
                continue;
            }

            // oneOf parent schemas → abstract class with [JsonDerivedType] attributes
            if (oneOfParents.TryGetValue(typeName, out var variants))
            {
                sb.AppendLine($"/// <summary>");
                AppendXmlDocLines(sb, schema.Description ?? name, "");
                sb.AppendLine($"/// </summary>");
                sb.AppendLine($"/// <remarks>");
                sb.AppendLine($"/// Use one of the following concrete types:");
                sb.AppendLine($"/// <list type=\"bullet\">");
                foreach (var variant in variants)
                    sb.AppendLine($"/// <item><description><see cref=\"{variant}\"/></description></item>");
                sb.AppendLine($"/// </list>");
                sb.AppendLine($"/// </remarks>");
                foreach (var variant in variants)
                    sb.AppendLine($"/// <seealso cref=\"{variant}\"/>");
                if (discriminators.TryGetValue(typeName, out var disc))
                    sb.AppendLine($"[JsonPolymorphic(TypeDiscriminatorPropertyName = \"{disc.PropertyName}\")]");
                foreach (var variant in variants)
                {
                    if (disc.ValueToVariant != null)
                    {
                        var matchingEntry = disc.ValueToVariant.FirstOrDefault(kv => kv.Value == variant);
                        if (matchingEntry.Key != null)
                        {
                            sb.AppendLine($"[JsonDerivedType(typeof({variant}), \"{matchingEntry.Key}\")]");
                            continue;
                        }
                    }
                    sb.AppendLine($"[JsonDerivedType(typeof({variant}))]");
                }
                sb.AppendLine($"public abstract class {typeName} {{ }}");
                sb.AppendLine();
                continue;
            }

            // oneOf with mixed primitive/class variants (FilterProperty pattern)
            // → generate class using properties from the non-primitive (advanced filter) variant
            if (schema.OneOf is { Count: > 0 } && !oneOfParents.ContainsKey(typeName))
            {
                var classVariantSchema = FindNonPrimitiveOneOfVariant(schema, doc);
                if (classVariantSchema != null)
                {
                    sb.AppendLine($"/// <summary>");
                    AppendXmlDocLines(sb, schema.Description ?? name, "");
                    sb.AppendLine($"/// </summary>");
                    sb.AppendLine($"public sealed class {typeName}");
                    sb.AppendLine("{");

                    var filterRequired = classVariantSchema.Required ?? new HashSet<string>();
                    var filterProperties = new Dictionary<string, OpenApiSchema>();

                    // Collect properties from the variant, including allOf composition
                    if (classVariantSchema.Properties != null)
                    {
                        foreach (var (propName, propSchema) in classVariantSchema.Properties)
                            filterProperties.TryAdd(propName, propSchema);
                    }
                    foreach (var allOf in classVariantSchema.AllOf ?? [])
                    {
                        var resolved = allOf.Reference != null && doc.Components?.Schemas != null
                            && doc.Components.Schemas.TryGetValue(allOf.Reference.Id, out var refSchema)
                            ? refSchema : allOf;
                        if (resolved.Properties != null)
                        {
                            foreach (var (propName, propSchema) in resolved.Properties)
                                filterProperties.TryAdd(propName, propSchema);
                        }
                        if (resolved.Required != null)
                        {
                            foreach (var r in resolved.Required)
                                filterRequired.Add(r);
                        }
                    }

                    foreach (var (propName, propSchema) in filterProperties)
                    {
                        var csharpType = MapType(propSchema, doc);
                        var csharpPropName = ToPascalCase(propName);
                        var isRequired = filterRequired.Contains(propName);
                        var isNullable = propSchema.Nullable;

                        if ((!isRequired || isNullable) && !csharpType.EndsWith('?'))
                            csharpType += "?";

                        if (!string.IsNullOrEmpty(propSchema.Description))
                        {
                            sb.AppendLine($"    /// <summary>");
                            AppendXmlDocLines(sb, propSchema.Description);
                            sb.AppendLine($"    /// </summary>");
                        }

                        sb.AppendLine($"    [JsonPropertyName(\"{propName}\")]");
                        var initializer = IsReferenceType(csharpType, doc) ? " = null!;" : "";
                        sb.AppendLine($"    public {csharpType} {csharpPropName} {{ get; set; }}{initializer}");
                        sb.AppendLine();
                    }

                    sb.AppendLine("}");
                    sb.AppendLine();
                    continue;
                }
            }

            // Determine if this class is a variant of a oneOf parent
            var baseClass = variantToParent.TryGetValue(typeName, out var parent) ? $" : {parent}" : "";

            // Check if this class has an optional tenantId property AND is used as a request body
            var tenantIdInfo = requestSchemaNames.Contains(typeName) ? GetOptionalTenantIdInfo(schema) : null;
            var interfaces = tenantIdInfo != null
                ? (baseClass != "" ? ", global::Camunda.Orchestration.Sdk.ITenantIdSettable" : " : global::Camunda.Orchestration.Sdk.ITenantIdSettable")
                : "";

            // Generate class
            sb.AppendLine($"/// <summary>");
            AppendXmlDocLines(sb, schema.Description ?? name, "");
            sb.AppendLine($"/// </summary>");
            sb.AppendLine($"public sealed class {typeName}{baseClass}{interfaces}");
            sb.AppendLine("{");

            var required = schema.Required ?? new HashSet<string>();
            var properties = schema.Properties ?? new Dictionary<string, OpenApiSchema>();

            // Handle allOf composition
            foreach (var allOf in schema.AllOf ?? [])
            {
                if (allOf.Properties != null)
                {
                    foreach (var (propName, propSchema) in allOf.Properties)
                    {
                        if (!properties.ContainsKey(propName))
                            properties[propName] = propSchema;
                    }
                }
                if (allOf.Required != null)
                {
                    foreach (var r in allOf.Required)
                        required.Add(r);
                }
            }

            // If this type is a variant of a polymorphic parent, skip the discriminator
            // property — it is managed by [JsonPolymorphic] on the base class and
            // re-declaring it on the derived type causes a conflict on .NET 10+.
            string? discriminatorPropToSkip = null;
            if (parent != null && discriminators.TryGetValue(parent, out var parentDisc))
                discriminatorPropToSkip = parentDisc.PropertyName;

            foreach (var (propName, propSchema) in properties)
            {
                if (propName == discriminatorPropToSkip)
                    continue;

                var csharpType = MapType(propSchema, doc);
                var csharpPropName = ToPascalCase(propName);
                var isRequired = required.Contains(propName);
                var isNullable = propSchema.Nullable;

                if ((!isRequired || isNullable) && !csharpType.EndsWith('?'))
                    csharpType += "?";

                if (!string.IsNullOrEmpty(propSchema.Description))
                {
                    sb.AppendLine($"    /// <summary>");
                    AppendXmlDocLines(sb, propSchema.Description);
                    sb.AppendLine($"    /// </summary>");
                }

                sb.AppendLine($"    [JsonPropertyName(\"{propName}\")]");
                var initializer = IsReferenceType(csharpType, doc) ? " = null!;" : "";
                sb.AppendLine($"    public {csharpType} {csharpPropName} {{ get; set; }}{initializer}");
                sb.AppendLine();
            }

            if (tenantIdInfo != null)
                EmitSetDefaultTenantIdMethod(sb, tenantIdInfo.Value);

            sb.AppendLine("}");
            sb.AppendLine();
        }

        // Generate classes for inline schemas (request/response bodies without $ref)
        foreach (var (typeName, schema) in inlineSchemas.OrderBy(kv => kv.Key))
        {
            GenerateClass(sb, typeName, schema, doc, requestSchemaNames.Contains(typeName));
        }

        return sb.ToString();
    }

    private static void GenerateClass(StringBuilder sb, string typeName, OpenApiSchema schema, OpenApiDocument? doc = null, bool isRequestSchema = false)
    {
        var tenantIdInfo = isRequestSchema ? GetOptionalTenantIdInfo(schema) : null;
        var interfaces = tenantIdInfo != null ? " : global::Camunda.Orchestration.Sdk.ITenantIdSettable" : "";

        sb.AppendLine($"/// <summary>");
        AppendXmlDocLines(sb, schema.Description ?? typeName, "");
        sb.AppendLine($"/// </summary>");
        sb.AppendLine($"public sealed class {typeName}{interfaces}");
        sb.AppendLine("{");

        var required = schema.Required ?? new HashSet<string>();
        var properties = schema.Properties ?? new Dictionary<string, OpenApiSchema>();

        // Handle allOf composition
        foreach (var allOf in schema.AllOf ?? [])
        {
            if (allOf.Properties != null)
            {
                foreach (var (propName, propSchema) in allOf.Properties)
                {
                    if (!properties.ContainsKey(propName))
                        properties[propName] = propSchema;
                }
            }
            if (allOf.Required != null)
            {
                foreach (var r in allOf.Required)
                    required.Add(r);
            }
        }

        foreach (var (propName, propSchema) in properties)
        {
            var csharpType = MapType(propSchema, doc);
            var csharpPropName = ToPascalCase(propName);
            var isRequired = required.Contains(propName);
            var isNullable = propSchema.Nullable;

            if ((!isRequired || isNullable) && !csharpType.EndsWith('?'))
                csharpType += "?";

            if (!string.IsNullOrEmpty(propSchema.Description))
            {
                sb.AppendLine($"    /// <summary>");
                AppendXmlDocLines(sb, propSchema.Description);
                sb.AppendLine($"    /// </summary>");
            }

            sb.AppendLine($"    [JsonPropertyName(\"{propName}\")]");
            var initializer = IsReferenceType(csharpType, doc) ? " = null!;" : "";
            sb.AppendLine($"    public {csharpType} {csharpPropName} {{ get; set; }}{initializer}");
            sb.AppendLine();
        }

        if (tenantIdInfo != null)
            EmitSetDefaultTenantIdMethod(sb, tenantIdInfo.Value);

        sb.AppendLine("}");
        sb.AppendLine();
    }

    /// <summary>
    /// Generates a strongly-typed readonly record struct for a primitive schema
    /// (nominal/branded type pattern — C# equivalent of TypeScript's branded types).
    /// </summary>
    private static void GenerateDomainStruct(StringBuilder sb, string typeName, OpenApiSchema schema, OpenApiDocument doc)
    {
        var underlyingType = GetUnderlyingPrimitiveType(schema, doc);
        var isString = underlyingType == "string";
        var interfaceName = isString ? "global::Camunda.Orchestration.Sdk.ICamundaKey" : "global::Camunda.Orchestration.Sdk.ICamundaLongKey";
        var (pattern, minLength, maxLength) = isString ? GetConstraints(schema, doc) : (null, null, null);

        sb.AppendLine($"/// <summary>");
        AppendXmlDocLines(sb, schema.Description ?? $"Strongly-typed wrapper for {typeName}.", "");
        sb.AppendLine($"/// </summary>");
        sb.AppendLine($"public readonly record struct {typeName} : {interfaceName}");
        sb.AppendLine("{");

        // Value property
        sb.AppendLine($"    /// <summary>The underlying {underlyingType} value.</summary>");
        sb.AppendLine($"    public {underlyingType} Value {{ get; }}");
        sb.AppendLine();

        // Private constructor
        sb.AppendLine($"    private {typeName}({underlyingType} value) => Value = value;");
        sb.AppendLine();

        // AssumeExists factory
        sb.AppendLine($"    /// <summary>");
        sb.AppendLine($"    /// Creates a <see cref=\"{typeName}\"/> from a raw {underlyingType} value.");
        sb.AppendLine($"    /// Use this when side-loading values not received from an API call.");
        sb.AppendLine($"    /// </summary>");
        sb.AppendLine($"    public static {typeName} AssumeExists({underlyingType} value)");
        sb.AppendLine("    {");

        if (isString)
        {
            // Build AssertConstraints call
            var constraintArgs = new List<string> { "value", $"\"{typeName}\"" };
            if (pattern != null)
                constraintArgs.Add($"pattern: @\"{pattern.Replace("\"", "\"\"")}\"");
            if (minLength != null)
                constraintArgs.Add($"minLength: {minLength}");
            if (maxLength != null)
                constraintArgs.Add($"maxLength: {maxLength}");

            sb.AppendLine($"        global::Camunda.Orchestration.Sdk.CamundaKeyValidation.AssertConstraints({string.Join(", ", constraintArgs)});");
        }

        sb.AppendLine($"        return new {typeName}(value);");
        sb.AppendLine("    }");
        sb.AppendLine();

        // IsValid static method
        if (isString)
        {
            var checkArgs = new List<string> { "value" };
            if (pattern != null)
                checkArgs.Add($"pattern: @\"{pattern.Replace("\"", "\"\"")}\"");
            if (minLength != null)
                checkArgs.Add($"minLength: {minLength}");
            if (maxLength != null)
                checkArgs.Add($"maxLength: {maxLength}");

            sb.AppendLine($"    /// <summary>Returns true if the value satisfies this type's constraints.</summary>");
            sb.AppendLine($"    public static bool IsValid(string value) =>");
            sb.AppendLine($"        global::Camunda.Orchestration.Sdk.CamundaKeyValidation.CheckConstraints({string.Join(", ", checkArgs)});");
            sb.AppendLine();
        }

        // ToString override
        sb.AppendLine($"    /// <inheritdoc />");
        sb.AppendLine($"    public override string ToString() => Value.ToString()!;");

        sb.AppendLine("}");
        sb.AppendLine();
    }

    /// <summary>
    /// Generates a strongly-typed readonly record struct for a schema that is a union of
    /// semantic key types (e.g. ScopeKey = ProcessInstanceKey | ElementInstanceKey).
    /// Emits implicit conversion operators from each branch type and cross-type == / != overloads
    /// so that branch values are directly assignable and comparable without casting.
    /// </summary>
    private static void GenerateSemanticKeyUnionStruct(StringBuilder sb, string typeName,
        OpenApiSchema schema, OpenApiDocument doc, List<string> branchTypeNames)
    {
        var (pattern, minLength, maxLength) = GetConstraints(schema, doc);

        sb.AppendLine($"/// <summary>");
        AppendXmlDocLines(sb, schema.Description ?? $"Strongly-typed union wrapper for {typeName}.", "");
        sb.AppendLine($"/// </summary>");
        sb.AppendLine($"public readonly record struct {typeName} : global::Camunda.Orchestration.Sdk.ICamundaKey");
        sb.AppendLine("{");

        sb.AppendLine($"    /// <summary>The underlying string value.</summary>");
        sb.AppendLine($"    public string Value {{ get; }}");
        sb.AppendLine();

        sb.AppendLine($"    private {typeName}(string value) => Value = value;");
        sb.AppendLine();

        // AssumeExists factory
        sb.AppendLine($"    /// <summary>");
        sb.AppendLine($"    /// Creates a <see cref=\"{typeName}\"/> from a raw string value.");
        sb.AppendLine($"    /// Use this when side-loading values not received from an API call.");
        sb.AppendLine($"    /// </summary>");
        sb.AppendLine($"    public static {typeName} AssumeExists(string value)");
        sb.AppendLine("    {");
        var constraintArgs = new List<string> { "value", $"\"{typeName}\"" };
        if (pattern != null)
            constraintArgs.Add($"pattern: @\"{pattern.Replace("\"", "\"\"")}\"");
        if (minLength != null)
            constraintArgs.Add($"minLength: {minLength}");
        if (maxLength != null)
            constraintArgs.Add($"maxLength: {maxLength}");
        sb.AppendLine($"        global::Camunda.Orchestration.Sdk.CamundaKeyValidation.AssertConstraints({string.Join(", ", constraintArgs)});");
        sb.AppendLine($"        return new {typeName}(value);");
        sb.AppendLine("    }");
        sb.AppendLine();

        // IsValid
        var checkArgs = new List<string> { "value" };
        if (pattern != null)
            checkArgs.Add($"pattern: @\"{pattern.Replace("\"", "\"\"")}\"");
        if (minLength != null)
            checkArgs.Add($"minLength: {minLength}");
        if (maxLength != null)
            checkArgs.Add($"maxLength: {maxLength}");
        sb.AppendLine($"    /// <summary>Returns true if the value satisfies this type's constraints.</summary>");
        sb.AppendLine($"    public static bool IsValid(string value) =>");
        sb.AppendLine($"        global::Camunda.Orchestration.Sdk.CamundaKeyValidation.CheckConstraints({string.Join(", ", checkArgs)});");
        sb.AppendLine();

        // Implicit conversion operators from each branch type
        foreach (var branch in branchTypeNames)
        {
            sb.AppendLine($"    /// <summary>Implicitly converts a <see cref=\"{branch}\"/> to <see cref=\"{typeName}\"/>.</summary>");
            sb.AppendLine($"    public static implicit operator {typeName}({branch} key) => new {typeName}(key.Value);");
            sb.AppendLine();
        }

        // Cross-type == / != overloads for each branch type
        foreach (var branch in branchTypeNames)
        {
            sb.AppendLine($"    /// <summary>Compares a <see cref=\"{typeName}\"/> and a <see cref=\"{branch}\"/> by value.</summary>");
            sb.AppendLine($"    public static bool operator ==({typeName} left, {branch} right) => left.Value == right.Value;");
            sb.AppendLine($"    /// <summary>Compares a <see cref=\"{typeName}\"/> and a <see cref=\"{branch}\"/> by value.</summary>");
            sb.AppendLine($"    public static bool operator !=({typeName} left, {branch} right) => left.Value != right.Value;");
            sb.AppendLine();
        }

        // ToString override
        sb.AppendLine($"    /// <inheritdoc />");
        sb.AppendLine($"    public override string ToString() => Value.ToString()!;");

        sb.AppendLine("}");
        sb.AppendLine();
    }

    /// <summary>
    /// Resolves the underlying primitive C# type for a schema, traversing allOf composition.
    /// </summary>
    private static string GetUnderlyingPrimitiveType(OpenApiSchema schema, OpenApiDocument doc)
    {
        if (schema.Type != null)
            return MapPrimitiveType(schema);

        foreach (var allOf in schema.AllOf ?? [])
        {
            var resolved = allOf.Reference != null && doc.Components?.Schemas != null
                && doc.Components.Schemas.TryGetValue(allOf.Reference.Id, out var refSchema)
                ? refSchema : allOf;
            if (resolved.Type != null)
                return MapPrimitiveType(resolved);
        }

        return "string"; // fallback
    }

    /// <remarks>
    /// The caller must validate <paramref name="specHash"/> before invoking this method.
    /// </remarks>
    private static string GenerateSpecHash(string specHash)
    {
        // Defence-in-depth: ensure the value is safe for a C# string literal.
        var escaped = specHash.Replace("\\", "\\\\").Replace("\"", "\\\"");

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("// Generated from OpenAPI spec. DO NOT EDIT.");
        sb.AppendLine();
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace Camunda.Orchestration.Sdk;");
        sb.AppendLine();
        sb.AppendLine("public partial class CamundaClient");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// SHA-256 digest of the OpenAPI spec this SDK was generated from.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine($"    public const string SpecHash = \"{escaped}\";");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string GenerateClientMethods(List<OperationMeta> operations, Dictionary<string, List<string>> operationExamples)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("// Generated from OpenAPI spec. DO NOT EDIT.");
        sb.AppendLine();
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("namespace Camunda.Orchestration.Sdk;");
        sb.AppendLine();
        sb.AppendLine("public partial class CamundaClient");
        sb.AppendLine("{");

        foreach (var op in operations)
        {
            GenerateMethod(sb, op, operationExamples);
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void GenerateMethod(StringBuilder sb, OperationMeta op, Dictionary<string, List<string>> operationExamples)
    {
        // XML doc
        sb.AppendLine($"    /// <summary>");
        if (!string.IsNullOrEmpty(op.Summary))
            AppendXmlDocLines(sb, op.Summary);
        if (!string.IsNullOrEmpty(op.Description))
            AppendXmlDocLines(sb, op.Description);
        sb.AppendLine($"    /// </summary>");
        sb.AppendLine($"    /// <remarks>Operation: {op.OriginalOperationId}</remarks>");

        // Inject code examples from operation-map.json
        if (operationExamples.TryGetValue(op.OriginalOperationId, out var examples))
        {
            foreach (var exampleCode in examples)
            {
                sb.AppendLine($"    /// <example>");
                sb.AppendLine($"    /// <code>");
                foreach (var line in exampleCode.Split('\n'))
                {
                    var trimmed = line.TrimEnd('\r');
                    sb.AppendLine($"    /// {System.Security.SecurityElement.Escape(trimmed)}");
                }
                sb.AppendLine($"    /// </code>");
                sb.AppendLine($"    /// </example>");
            }
        }

        // Build parameters
        var methodParams = new List<string>();
        foreach (var p in op.PathParams)
            methodParams.Add($"{p.Type} {ToCamelCase(p.Name)}");
        if (op.HasBody && !op.IsMultipart && op.BodyTypeName != null)
            methodParams.Add($"{op.BodyTypeName} body");
        if (op.IsMultipart)
            methodParams.Add("MultipartFormDataContent content");
        foreach (var q in op.QueryParams)
        {
            var type = q.Required ? q.Type : q.Type + "?";
            methodParams.Add($"{type} {ToCamelCase(q.Name)}{(q.Required ? "" : " = null")}");
        }
        if (op.IsEventuallyConsistent)
            methodParams.Add($"ConsistencyOptions<{(op.IsVoidResponse ? "object" : op.ResponseTypeName)}>? consistency = null");
        methodParams.Add("CancellationToken ct = default");

        var returnType = op.IsVoidResponse ? "Task" : $"Task<{op.ResponseTypeName}>";
        var paramsStr = string.Join(", ", methodParams);

        sb.AppendLine($"    public async {returnType} {op.OperationId}Async({paramsStr})");
        sb.AppendLine("    {");

        // Build path with parameter substitution
        var pathExpr = op.Path;
        foreach (var p in op.PathParams)
        {
            pathExpr = pathExpr.Replace($"{{{p.Name}}}", $"{{Uri.EscapeDataString({ToCamelCase(p.Name)}.ToString()!)}}");
        }

        // Add query string
        if (op.QueryParams.Count > 0)
        {
            sb.AppendLine("        var queryParts = new List<string>();");
            foreach (var q in op.QueryParams)
            {
                var varName = ToCamelCase(q.Name);
                if (q.Required)
                    sb.AppendLine($"        queryParts.Add($\"{q.Name}={{Uri.EscapeDataString({varName}.ToString()!)}}\");");
                else
                    sb.AppendLine($"        if ({varName} != null) queryParts.Add($\"{q.Name}={{Uri.EscapeDataString({varName}.ToString()!)}}\");");
            }
            sb.AppendLine($"        var path = queryParts.Count > 0 ? $\"{pathExpr}?{{string.Join(\"&\", queryParts)}}\" : $\"{pathExpr}\";");
        }
        else
        {
            sb.AppendLine($"        var path = $\"{pathExpr}\";");
        }

        // Inject default tenant ID enrichment for operations with optional tenantId in body
        if (op.HasOptionalTenantIdInBody && op.HasBody && !op.IsMultipart)
        {
            sb.AppendLine($"        if (body is ITenantIdSettable __t) __t.SetDefaultTenantId(_config.DefaultTenantId);");
        }

        // Inject default tenant ID into multipart content when applicable
        if (op.HasOptionalTenantIdInBody && op.IsMultipart)
        {
            sb.AppendLine($"        if (_config.DefaultTenantId != null)");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            var hasTenant = content.Any(p => p.Headers.ContentDisposition?.Name?.Trim('\"') == \"tenantId\");");
            sb.AppendLine($"            if (!hasTenant)");
            sb.AppendLine($"                content.Add(new StringContent(_config.DefaultTenantId), \"tenantId\");");
            sb.AppendLine($"        }}");
        }

        var httpMethod = op.Verb switch
        {
            OperationType.Get => "HttpMethod.Get",
            OperationType.Post => "HttpMethod.Post",
            OperationType.Put => "HttpMethod.Put",
            OperationType.Delete => "HttpMethod.Delete",
            OperationType.Patch => "HttpMethod.Patch",
            _ => "HttpMethod.Get",
        };

        // Generate the call
        var bodyArg = op.IsMultipart ? "null" : (op.HasBody ? "body" : "null");

        if (op.IsEventuallyConsistent)
        {
            sb.AppendLine("        if (consistency != null && consistency.WaitUpToMs > 0)");
            sb.AppendLine("        {");
            if (op.IsVoidResponse)
            {
                if (op.IsMultipart)
                {
                    sb.AppendLine($"            await EventualPoller.PollAsync(\"{op.OriginalOperationId}\", {(op.Verb == OperationType.Get ? "true" : "false")},");
                    sb.AppendLine($"                async () => {{ await InvokeWithRetryAsync(() => SendMultipartAsync<object>(path, content, ct), \"{op.OriginalOperationId}\", {op.IsExemptFromBackpressure.ToString().ToLowerInvariant()}, ct); return new object(); }},");
                    sb.AppendLine($"                consistency!, _logger, ct);");
                }
                else
                {
                    sb.AppendLine($"            await EventualPoller.PollAsync(\"{op.OriginalOperationId}\", {(op.Verb == OperationType.Get ? "true" : "false")},");
                    sb.AppendLine($"                async () => {{ await SendVoidAsync({httpMethod}, path, {bodyArg}, ct); return new object(); }},");
                    sb.AppendLine($"                consistency!, _logger, ct);");
                }
                sb.AppendLine("            return;");
            }
            else
            {
                if (op.IsMultipart)
                {
                    sb.AppendLine($"            return await EventualPoller.PollAsync(\"{op.OriginalOperationId}\", {(op.Verb == OperationType.Get ? "true" : "false")},");
                    sb.AppendLine($"                () => InvokeWithRetryAsync(() => SendMultipartAsync<{op.ResponseTypeName}>(path, content, ct), \"{op.OriginalOperationId}\", {op.IsExemptFromBackpressure.ToString().ToLowerInvariant()}, ct),");
                    sb.AppendLine($"                consistency!, _logger, ct);");
                }
                else
                {
                    sb.AppendLine($"            return await EventualPoller.PollAsync(\"{op.OriginalOperationId}\", {(op.Verb == OperationType.Get ? "true" : "false")},");
                    sb.AppendLine($"                () => InvokeWithRetryAsync(() => SendAsync<{op.ResponseTypeName}>({httpMethod}, path, {bodyArg}, ct), \"{op.OriginalOperationId}\", {op.IsExemptFromBackpressure.ToString().ToLowerInvariant()}, ct),");
                    sb.AppendLine($"                consistency!, _logger, ct);");
                }
            }
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        if (op.IsMultipart)
        {
            var returnKw = op.IsVoidResponse ? "" : "return ";
            sb.AppendLine($"        {returnKw}await InvokeWithRetryAsync(() => SendMultipartAsync<{op.ResponseTypeName}>(path, content, ct), \"{op.OriginalOperationId}\", {op.IsExemptFromBackpressure.ToString().ToLowerInvariant()}, ct);");
        }
        else if (op.IsVoidResponse)
        {
            sb.AppendLine($"        await InvokeWithRetryAsync(async () => {{ await SendVoidAsync({httpMethod}, path, {bodyArg}, ct); return 0; }}, \"{op.OriginalOperationId}\", {op.IsExemptFromBackpressure.ToString().ToLowerInvariant()}, ct);");
        }
        else
        {
            var returnKw = op.IsEventuallyConsistent ? "return " : "return ";
            sb.AppendLine($"        {returnKw}await InvokeWithRetryAsync(() => SendAsync<{op.ResponseTypeName}>({httpMethod}, path, {bodyArg}, ct), \"{op.OriginalOperationId}\", {op.IsExemptFromBackpressure.ToString().ToLowerInvariant()}, ct);");
        }

        sb.AppendLine("    }");
        sb.AppendLine();
    }

    /// <summary>
    /// Returns true if a C# type string represents a reference type that needs a null-forgiving
    /// initializer (= null!;) to avoid CS8618 warnings.
    /// </summary>
    private static bool IsReferenceType(string csharpType, OpenApiDocument? doc)
    {
        if (csharpType.EndsWith('?'))
            return false;
        // Known value types
        if (csharpType is "int" or "long" or "double" or "float" or "bool"
            or "DateTimeOffset" or "DateOnly" or "byte" or "short")
            return false;
        // Known reference types
        if (csharpType is "string" or "object" or "byte[]"
            || csharpType.StartsWith("List<", StringComparison.Ordinal)
            || csharpType.StartsWith("Dictionary<", StringComparison.Ordinal))
            return true;
        // Check if it's an enum or domain struct (value type) in the spec
        if (doc?.Components?.Schemas != null)
        {
            foreach (var (name, schema) in doc.Components.Schemas)
            {
                if (SanitizeTypeName(name) != csharpType)
                    continue;
                // Enums are value types
                if (schema.Enum?.Count > 0 && schema.Type == "string")
                    return false;
                // Domain key structs (primitive wrappers) are value types
                if (IsPrimitiveSchemaResolved(schema, doc))
                    return false;
                // Everything else is a class (reference type)
                return true;
            }
        }
        // Unknown type — assume reference type to be safe
        return true;
    }

    /// <summary>
    /// Returns true if the schema is a primitive scalar (string, integer, number, boolean)
    /// with no object properties — i.e. it should map to a strongly-typed wrapper, not a class.
    /// </summary>
    private static bool IsPrimitiveSchema(OpenApiSchema schema) =>
        schema.Type is "string" or "integer" or "number" or "boolean"
        && (schema.Properties == null || schema.Properties.Count == 0)
        && (schema.Enum == null || schema.Enum.Count == 0);

    /// <summary>
    /// Returns true if the schema (or its allOf base) is a primitive scalar,
    /// resolving through allOf composition (e.g. AuditLogKey → allOf[LongKey] → string).
    /// </summary>
    private static bool IsPrimitiveSchemaResolved(OpenApiSchema schema, OpenApiDocument doc)
    {
        if (IsPrimitiveSchema(schema))
            return true;
        // Check allOf: if all fragments resolve to primitives, it's a primitive wrapper
        if (schema.AllOf is { Count: > 0 } && (schema.Properties == null || schema.Properties.Count == 0))
        {
            foreach (var fragment in schema.AllOf)
            {
                var resolved = fragment.Reference != null && doc.Components?.Schemas != null
                    && doc.Components.Schemas.TryGetValue(fragment.Reference.Id, out var refSchema)
                    ? refSchema : fragment;
                if (!IsPrimitiveSchema(resolved))
                    return false;
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// Extracts constraints (pattern, minLength, maxLength) from a schema,
    /// resolving through allOf composition.
    /// </summary>
    private static (string? pattern, int? minLength, int? maxLength) GetConstraints(OpenApiSchema schema, OpenApiDocument doc)
    {
        string? pattern = schema.Pattern;
        int? minLength = schema.MinLength > 0 ? schema.MinLength : null;
        int? maxLength = schema.MaxLength > 0 ? schema.MaxLength : null;

        foreach (var allOf in schema.AllOf ?? [])
        {
            var resolved = allOf.Reference != null && doc.Components?.Schemas != null
                && doc.Components.Schemas.TryGetValue(allOf.Reference.Id, out var refSchema)
                ? refSchema : allOf;
            pattern ??= resolved.Pattern;
            minLength ??= resolved.MinLength > 0 ? resolved.MinLength : null;
            maxLength ??= resolved.MaxLength > 0 ? resolved.MaxLength : null;
        }
        return (pattern, minLength, maxLength);
    }

    // Type mapping
    internal static string MapType(OpenApiSchema? schema, OpenApiDocument? doc = null)
    {
        if (schema == null)
            return "object";
        // $ref to a named schema — resolve array-type schemas inline as List<ItemType>
        if (schema.Reference != null)
        {
            if (doc != null
                && doc.Components?.Schemas != null
                && doc.Components.Schemas.TryGetValue(schema.Reference.Id, out var resolved)
                && resolved.Type == "array")
            {
                return $"List<{MapType(resolved.Items, doc)}>";
            }

            return SanitizeTypeName(schema.Reference.Id);
        }

        // allOf with a single $ref (common pattern: allOf: [{$ref: "..."}] + description overlay)
        if (schema.AllOf is { Count: > 0 } && schema.Type == null)
        {
            var refFragment = schema.AllOf.FirstOrDefault(a => a.Reference != null);
            if (refFragment != null)
                return SanitizeTypeName(refFragment.Reference.Id);
        }

        return MapPrimitiveType(schema, doc);
    }

    private static string MapPrimitiveType(OpenApiSchema schema, OpenApiDocument? doc = null)
    {
        return schema.Type switch
        {
            "string" when schema.Format == "date-time" => "DateTimeOffset",
            "string" when schema.Format == "date" => "DateOnly",
            "string" when schema.Format == "binary" => "byte[]",
            "string" => "string",
            "integer" when schema.Format == "int64" => "long",
            "integer" => "int",
            "number" when schema.Format == "double" => "double",
            "number" when schema.Format == "float" => "float",
            "number" => "double",
            "boolean" => "bool",
            "array" => $"List<{MapType(schema.Items, doc)}>",
            "object" when schema.AdditionalProperties != null => $"Dictionary<string, {MapType(schema.AdditionalProperties, doc)}>",
            "object" when doc != null && schema.Properties?.Count > 0 => FindMatchingComponentSchema(doc, schema) ?? "object",
            "object" => "object",
            _ => "object",
        };
    }

    /// <summary>
    /// When an inline object schema has properties, try to find a component schema
    /// with the same set of property names. This handles cases where the spec uses
    /// inline definitions instead of $ref for schemas that match existing components.
    /// </summary>
    private static string? FindMatchingComponentSchema(OpenApiDocument doc, OpenApiSchema inlineSchema)
    {
        var inlineProps = inlineSchema.Properties?.Keys.ToHashSet();
        if (inlineProps == null || inlineProps.Count == 0)
            return null;

        var schemas = doc.Components?.Schemas;
        if (schemas == null)
            return null;

        foreach (var (name, componentSchema) in schemas)
        {
            var componentProps = componentSchema.Properties?.Keys.ToHashSet();
            if (componentProps != null && componentProps.Count > 0 && componentProps.SetEquals(inlineProps))
                return SanitizeTypeName(name);
        }
        return null;
    }

    internal static string SanitizeTypeName(string name)
    {
        // Handle names like "ProcessInstance" → "ProcessInstance"
        // Handle names with special chars
        return name
            .Replace("XML", "Xml")
            .Replace("-", "")
            .Replace(".", "")
            .Replace("$", "")
            .Replace(" ", "");
    }

    /// <summary>
    /// Returns the C# type of the optional tenantId property if present, or null if not.
    /// Used to determine the SetDefaultTenantId implementation.
    /// </summary>
    private static (string CSharpType, bool IsBrandedKey)? GetOptionalTenantIdInfo(OpenApiSchema schema)
    {
        // Collect all properties including allOf composition
        var properties = new Dictionary<string, OpenApiSchema>(
            schema.Properties ?? new Dictionary<string, OpenApiSchema>());
        var required = new HashSet<string>(
            schema.Required ?? new HashSet<string>());
        foreach (var allOf in schema.AllOf ?? [])
        {
            if (allOf.Properties != null)
                foreach (var (k, v) in allOf.Properties)
                    properties.TryAdd(k, v);
            if (allOf.Required != null)
                foreach (var r in allOf.Required)
                    required.Add(r);
        }

        if (!properties.TryGetValue("tenantId", out var tenantProp))
            return null;
        if (required.Contains("tenantId"))
            return null;

        var mappedType = MapType(tenantProp);
        // Only support plain string or branded TenantId key — not complex filter types
        if (mappedType != "string" && mappedType != "TenantId")
            return null;
        var isBranded = mappedType == "TenantId";
        return (mappedType, isBranded);
    }

    /// <summary>
    /// Emits the SetDefaultTenantId method implementation for ITenantIdSettable.
    /// </summary>
    private static void EmitSetDefaultTenantIdMethod(StringBuilder sb, (string CSharpType, bool IsBrandedKey) info)
    {
        sb.AppendLine("    /// <inheritdoc />");
        if (info.IsBrandedKey)
        {
            sb.AppendLine("    public void SetDefaultTenantId(string tenantId) { TenantId ??= global::Camunda.Orchestration.Sdk.TenantId.AssumeExists(tenantId); }");
        }
        else
        {
            sb.AppendLine("    public void SetDefaultTenantId(string tenantId) { TenantId ??= tenantId; }");
        }
        sb.AppendLine();
    }

    internal static string SanitizeOperationId(string id)
    {
        var result = ToPascalCase(id.Replace("XML", "Xml"));
        return result;
    }

    internal static string ToPascalCase(string s)
    {
        if (string.IsNullOrEmpty(s))
            return s;
        // Strip leading $ for identifiers like $eq, $like
        if (s.StartsWith('$'))
            s = s[1..];
        if (string.IsNullOrEmpty(s))
            return s;
        // Handle separators: dots, underscores, hyphens
        if (s.Contains('_') || s.Contains('-') || s.Contains('.'))
        {
            var parts = s.Split(['_', '-', '.'], StringSplitOptions.RemoveEmptyEntries);
            return string.Concat(parts.Select(p =>
                p.Length == 0 ? "" : char.ToUpperInvariant(p[0]) + p[1..]));
        }
        // Handle camelCase → PascalCase
        if (char.IsLower(s[0]))
            return char.ToUpperInvariant(s[0]) + s[1..];
        return s;
    }

    internal static string ToCamelCase(string s)
    {
        var pascal = ToPascalCase(s);
        if (string.IsNullOrEmpty(pascal))
            return pascal;
        return char.ToLowerInvariant(pascal[0]) + pascal[1..];
    }

    private static string EscapeXml(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    /// <summary>
    /// Formats a potentially multi-line string as XML doc comment lines,
    /// each prefixed with the given indent + "/// ".
    /// </summary>
    private static void AppendXmlDocLines(StringBuilder sb, string text, string indent = "    ")
    {
        var escaped = EscapeXml(text);
        foreach (var line in escaped.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            sb.AppendLine($"{indent}/// {trimmed}");
        }
    }

    /// <summary>
    /// Load operation-map.json and resolve each region reference to its code block.
    /// Returns a map of operationId → list of extracted code strings.
    /// </summary>
    private static Dictionary<string, List<string>> LoadOperationExamples(string examplesDir)
    {
        var result = new Dictionary<string, List<string>>();
        var mapPath = Path.Combine(examplesDir, "operation-map.json");
        if (!File.Exists(mapPath))
            return result;

        var json = File.ReadAllText(mapPath);
        var map = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
        if (map == null)
            return result;

        foreach (var (opId, refs) in map)
        {
            var codes = new List<string>();
            foreach (var refEl in refs.EnumerateArray())
            {
                var file = refEl.GetProperty("file").GetString();
                var region = refEl.GetProperty("region").GetString();
                if (file == null || region == null)
                    continue;
                var code = ExtractRegion(Path.Combine(examplesDir, file), region);
                if (code != null)
                    codes.Add(code);
            }
            if (codes.Count > 0)
                result[opId] = codes;
        }

        return result;
    }

    /// <summary>
    /// Extract code between // &lt;RegionName&gt; and // &lt;/RegionName&gt; markers.
    /// </summary>
    private static string? ExtractRegion(string filePath, string regionName)
    {
        if (!File.Exists(filePath))
            return null;

        var lines = File.ReadAllLines(filePath);
        var capturing = false;
        var captured = new List<string>();
        var startTag = $"// <{regionName}>";
        var endTag = $"// </{regionName}>";

        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith(startTag, StringComparison.Ordinal))
            {
                capturing = true;
                continue;
            }
            if (line.TrimStart().StartsWith(endTag, StringComparison.Ordinal))
                break;
            if (capturing)
                captured.Add(line);
        }

        if (captured.Count == 0)
            return null;

        // Dedent: find minimum indentation of non-empty lines
        var nonEmpty = captured.Where(l => l.Trim().Length > 0).ToList();
        if (nonEmpty.Count > 0)
        {
            var minIndent = nonEmpty.Min(l => l.Length - l.TrimStart().Length);
            captured = captured.Select(l => l.Length >= minIndent ? l[minIndent..] : l).ToList();
        }

        // Trim leading/trailing blank lines
        while (captured.Count > 0 && string.IsNullOrWhiteSpace(captured[0]))
            captured.RemoveAt(0);
        while (captured.Count > 0 && string.IsNullOrWhiteSpace(captured[^1]))
            captured.RemoveAt(captured.Count - 1);

        return string.Join("\n", captured);
    }
}

internal sealed class OperationMeta
{
    public required string OperationId { get; init; }
    public required string OriginalOperationId { get; init; }
    public required OperationType Verb { get; init; }
    public required string Path { get; init; }
    public required List<ParamMeta> PathParams { get; init; }
    public required List<ParamMeta> QueryParams { get; init; }
    public required bool HasBody { get; init; }
    public required bool IsMultipart { get; init; }
    public string? BodyTypeName { get; init; }
    public required string ResponseTypeName { get; init; }
    public required bool IsVoidResponse { get; init; }
    public required bool IsEventuallyConsistent { get; init; }
    public string? Summary { get; init; }
    public string? Description { get; init; }
    public required List<string> Tags { get; init; }
    public required bool IsExemptFromBackpressure { get; init; }
    public required bool HasOptionalTenantIdInBody { get; init; }
}

internal readonly record struct ParamMeta(string Name, string Type, bool Required);
