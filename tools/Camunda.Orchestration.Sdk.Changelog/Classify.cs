namespace Camunda.Orchestration.Sdk.Changelog;

/// <summary>
/// Whether a type is used in request or response position.
/// This drives severity rules: adding required fields to a request is breaking,
/// removing fields from a response is breaking, etc.
/// </summary>
public enum TypeRole
{
    Unknown,
    Request,
    Response,
}

/// <summary>
/// Classifies types as request or response based on naming conventions
/// and structural inference from client method signatures.
/// </summary>
public static class Classifier
{
    private static readonly string[] RequestSuffixes =
    [
        "SearchQuery", "Instruction", "Request", "Filter", "Query", "Input",
    ];

    private static readonly string[] ResponseSuffixes =
    [
        "SearchQueryResult", "SearchResult", "CreatedResult", "Result", "Response", "Error",
    ];

    /// <summary>
    /// Build a role map for all types using both structural inference (from methods)
    /// and suffix-based heuristics.
    /// </summary>
    public static Dictionary<string, TypeRole> BuildRoleMap(ApiSurface surface)
    {
        var roles = new Dictionary<string, TypeRole>(StringComparer.Ordinal);

        // Phase 1: structural inference from client methods
        foreach (var method in surface.ClientMethods)
        {
            // Return type → response
            var returnType = UnwrapTaskType(method.ReturnType);
            if (returnType != "Task" && returnType != "void")
            {
                MarkRole(roles, returnType, TypeRole.Response);
            }

            // Parameter types → request
            foreach (var param in method.Parameters)
            {
                MarkRole(roles, param.TypeExpr, TypeRole.Request);
            }
        }

        // Phase 2: transitively mark property types
        var allTypes = GatherAllTypeNames(surface);
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var typeName in allTypes)
            {
                if (!roles.TryGetValue(typeName, out var role))
                    continue;

                var propTypes = GetPropertyTypeNames(surface, typeName);
                foreach (var pt in propTypes)
                {
                    if (roles.TryGetValue(pt, out var existing))
                    {
                        if (existing != role && existing != TypeRole.Unknown)
                        {
                            // Used in both roles — mark unknown
                            if (roles[pt] != TypeRole.Unknown)
                            {
                                roles[pt] = TypeRole.Unknown;
                                changed = true;
                            }
                        }
                    }
                    else
                    {
                        roles[pt] = role;
                        changed = true;
                    }
                }
            }
        }

        // Phase 3: suffix-based heuristics for remaining types
        foreach (var typeName in allTypes)
        {
            if (roles.ContainsKey(typeName))
                continue;
            roles[typeName] = ClassifyBySuffix(typeName);
        }

        return roles;
    }

    /// <summary>
    /// Classify a single type by suffix heuristic.
    /// </summary>
    public static TypeRole ClassifyBySuffix(string typeName)
    {
        // Check response first (longer suffixes first to avoid false matches)
        foreach (var suffix in ResponseSuffixes)
        {
            if (typeName.EndsWith(suffix, StringComparison.Ordinal))
                return TypeRole.Response;
        }

        foreach (var suffix in RequestSuffixes)
        {
            if (typeName.EndsWith(suffix, StringComparison.Ordinal))
                return TypeRole.Request;
        }

        // Substring patterns
        if (typeName.Contains("Filter") || typeName.Contains("SortRequest"))
            return TypeRole.Request;
        if (typeName.Contains("Result") || typeName.Contains("Response"))
            return TypeRole.Response;

        return TypeRole.Unknown;
    }

    private static void MarkRole(Dictionary<string, TypeRole> roles, string typeExpr, TypeRole role)
    {
        var typeName = ExtractTypeName(typeExpr);
        if (string.IsNullOrEmpty(typeName))
            return;

        if (roles.TryGetValue(typeName, out var existing))
        {
            if (existing != role)
                roles[typeName] = TypeRole.Unknown;
        }
        else
        {
            roles[typeName] = role;
        }
    }

    private static HashSet<string> GatherAllTypeNames(ApiSurface surface)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var k in surface.Classes.Keys)
            names.Add(k);
        foreach (var k in surface.Enums.Keys)
            names.Add(k);
        foreach (var k in surface.Structs.Keys)
            names.Add(k);
        return names;
    }

    private static List<string> GetPropertyTypeNames(ApiSurface surface, string typeName)
    {
        var result = new List<string>();

        if (surface.Classes.TryGetValue(typeName, out var cls))
        {
            foreach (var p in cls.Properties)
                AddExtractedType(result, p.TypeExpr);
        }
        else if (surface.Structs.TryGetValue(typeName, out var st))
        {
            foreach (var p in st.Properties)
                AddExtractedType(result, p.TypeExpr);
        }

        return result;
    }

    private static void AddExtractedType(List<string> result, string typeExpr)
    {
        var name = ExtractTypeName(typeExpr);
        if (!string.IsNullOrEmpty(name))
            result.Add(name);
    }

    /// <summary>
    /// Extract the core type name from a type expression.
    /// E.g., "List&lt;Foo&gt;?" → "Foo", "Task&lt;Bar&gt;" → "Bar", "string" → "string"
    /// </summary>
    public static string ExtractTypeName(string typeExpr)
    {
        var s = typeExpr.TrimEnd('?');

        // Unwrap Task<T>
        s = UnwrapGeneric(s, "Task");

        // Unwrap List<T>
        s = UnwrapGeneric(s, "List");

        // Unwrap Nullable<T>
        s = UnwrapGeneric(s, "Nullable");

        return s.TrimEnd('?');
    }

    private static string UnwrapTaskType(string returnType)
    {
        var s = returnType.TrimEnd('?');
        if (s.StartsWith("Task<", StringComparison.Ordinal) && s.EndsWith('>'))
            return s[5..^1];
        return s;
    }

    private static string UnwrapGeneric(string s, string genericName)
    {
        var prefix = genericName + "<";
        if (s.StartsWith(prefix, StringComparison.Ordinal) && s.EndsWith('>'))
            return s[prefix.Length..^1];
        return s;
    }
}
