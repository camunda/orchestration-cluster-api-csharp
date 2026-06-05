namespace Camunda.Orchestration.Sdk;

/// <summary>
/// Base class for all errors raised by the DTO-driven typed variable map feature
/// (<see cref="CamundaClient.SearchVariablesAsDtoAsync{T}"/>).
/// </summary>
public class TypedVariablesException : Exception
{
    /// <summary>Create a new <see cref="TypedVariablesException"/>.</summary>
    public TypedVariablesException(string message)
        : base(message)
    {
    }

    /// <summary>Create a new <see cref="TypedVariablesException"/> with an inner cause.</summary>
    public TypedVariablesException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Raised when a declared variable name is returned at more than one scope.
///
/// <para>
/// The DTO is a flat name-to-value map, but BPMN variables are scoped (process-level
/// vs. local element scopes). When a declared variable resolves to multiple scopes the
/// SDK cannot deterministically choose one, so it raises rather than guessing. Pass
/// <c>scopeKey</c> to the search call to disambiguate.
/// </para>
/// </summary>
public sealed class VariableScopeCollisionException : TypedVariablesException
{
    /// <summary>The variable name that was found at multiple scopes.</summary>
    public string VariableName { get; }

    /// <summary>The distinct scope keys the variable was observed at, sorted ascending.</summary>
    public IReadOnlyList<string> ScopeKeys { get; }

    /// <summary>Create a new <see cref="VariableScopeCollisionException"/>.</summary>
    public VariableScopeCollisionException(string variableName, IReadOnlyList<string> scopeKeys)
        : base(
            $"Variable '{variableName}' was found at multiple scopes ({string.Join(", ", scopeKeys)}). "
            + "Pass scopeKey to the search to select a single scope.")
    {
        VariableName = variableName;
        ScopeKeys = scopeKeys;
    }
}

/// <summary>
/// Raised when a present variable value is not parseable as JSON.
///
/// <para>
/// A <em>missing</em> variable is not an error (it simply does not appear in the map);
/// a <em>present but malformed</em> value is, and is surfaced here rather than silently
/// dropped.
/// </para>
/// </summary>
public sealed class VariableDeserializationException : TypedVariablesException
{
    /// <summary>The variable name whose value could not be parsed.</summary>
    public string VariableName { get; }

    /// <summary>Create a new <see cref="VariableDeserializationException"/>.</summary>
    public VariableDeserializationException(string variableName, Exception innerException)
        : base(
            $"Variable '{variableName}' has a value that is not valid JSON and cannot be deserialized.",
            innerException)
    {
        VariableName = variableName;
    }
}

/// <summary>
/// Raised by <see cref="VariableMap{T}.Validate"/> when one or more required DTO members
/// (non-nullable members, or members marked with the <c>required</c> modifier) are absent
/// from the search result.
/// </summary>
public sealed class VariableValidationException : TypedVariablesException
{
    /// <summary>The DTO type that failed validation.</summary>
    public Type DtoType { get; }

    /// <summary>The variable names of the required members that were missing.</summary>
    public IReadOnlyList<string> MissingVariableNames { get; }

    /// <summary>Create a new <see cref="VariableValidationException"/>.</summary>
    public VariableValidationException(Type dtoType, IReadOnlyList<string> missingVariableNames)
        : base(
            $"Validation of '{dtoType.Name}' failed: required variable(s) "
            + $"[{string.Join(", ", missingVariableNames)}] were not present in the search result.")
    {
        DtoType = dtoType;
        MissingVariableNames = missingVariableNames;
    }
}
