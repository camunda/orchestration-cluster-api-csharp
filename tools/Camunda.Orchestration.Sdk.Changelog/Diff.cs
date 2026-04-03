namespace Camunda.Orchestration.Sdk.Changelog;

public enum Severity
{
    Breaking = 0,
    Warning = 1,
    Additive = 2,
    Info = 3,
}

public enum ChangeKind
{
    // Types
    TypeRemoved,
    TypeAdded,

    // Class properties
    PropertyRemoved,
    PropertyAdded,
    PropertyTypeChanged,
    PropertyBecameOptional,
    PropertyBecameRequired,
    PropertyJsonNameChanged,

    // Enums
    EnumRemoved,
    EnumAdded,
    EnumMemberRemoved,
    EnumMemberAdded,
    EnumMemberMarkedObsolete,
    EnumMemberJsonNameChanged,

    // Structs
    StructRemoved,
    StructAdded,
    StructInterfaceRemoved,
    StructInterfaceAdded,
    StructImplicitConversionRemoved,
    StructImplicitConversionAdded,

    // Methods
    MethodRemoved,
    MethodAdded,
    MethodReturnTypeChanged,
    MethodParameterChanged,

    // Polymorphic
    PolymorphicDiscriminatorChanged,
    PolymorphicDerivedTypeRemoved,
    PolymorphicDerivedTypeAdded,

    // Classes — inheritance
    BaseClassChanged,
    InterfaceRemoved,
    InterfaceAdded,
}

public sealed class Change
{
    public required ChangeKind Kind { get; init; }
    public required Severity Severity { get; init; }
    public required string TypeName { get; init; }
    public string? Field { get; init; }
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
    public required string Detail { get; init; }
    public TypeRole Role { get; init; }
}

public sealed class DiffResult
{
    public required string OldVersion { get; init; }
    public required string NewVersion { get; init; }
    public List<Change> Changes { get; } = [];

    public int Breaking => Changes.Count(c => c.Severity == Severity.Breaking);
    public int Warnings => Changes.Count(c => c.Severity == Severity.Warning);
    public int Additive => Changes.Count(c => c.Severity == Severity.Additive);
    public int Info => Changes.Count(c => c.Severity == Severity.Info);
    public int Total => Changes.Count;
}

/// <summary>
/// Core diff engine: compares old and new API surfaces to detect changes.
/// </summary>
public static class Differ
{
    public static DiffResult Diff(
        ApiSurface oldSurface,
        ApiSurface newSurface,
        string oldVersion,
        string newVersion)
    {
        var result = new DiffResult { OldVersion = oldVersion, NewVersion = newVersion };

        // Build unified role map from both surfaces
        var roles = BuildUnifiedRoleMap(oldSurface, newSurface);

        DiffClasses(oldSurface, newSurface, roles, result);
        DiffEnums(oldSurface, newSurface, roles, result);
        DiffStructs(oldSurface, newSurface, roles, result);
        DiffMethods(oldSurface, newSurface, result);

        return result;
    }

    private static Dictionary<string, TypeRole> BuildUnifiedRoleMap(ApiSurface old, ApiSurface @new)
    {
        var rolesOld = Classifier.BuildRoleMap(old);
        var rolesNew = Classifier.BuildRoleMap(@new);

        var merged = new Dictionary<string, TypeRole>(rolesOld, StringComparer.Ordinal);
        foreach (var (key, role) in rolesNew)
        {
            if (merged.TryGetValue(key, out var existing))
            {
                if (existing != role)
                    merged[key] = TypeRole.Unknown;
            }
            else
            {
                merged[key] = role;
            }
        }
        return merged;
    }

    private static TypeRole GetRole(Dictionary<string, TypeRole> roles, string typeName)
    {
        return roles.TryGetValue(typeName, out var role) ? role : TypeRole.Unknown;
    }

    // ── Classes ──────────────────────────────────────────────

    private static void DiffClasses(
        ApiSurface old, ApiSurface @new,
        Dictionary<string, TypeRole> roles, DiffResult result)
    {
        // Removed classes
        foreach (var (name, cls) in old.Classes)
        {
            if (!@new.Classes.ContainsKey(name))
            {
                result.Changes.Add(new Change
                {
                    Kind = ChangeKind.TypeRemoved,
                    Severity = Severity.Breaking,
                    TypeName = name,
                    Detail = $"`{name}` removed",
                    Role = GetRole(roles, name),
                });
            }
        }

        // Added classes
        foreach (var (name, _) in @new.Classes)
        {
            if (!old.Classes.ContainsKey(name))
            {
                result.Changes.Add(new Change
                {
                    Kind = ChangeKind.TypeAdded,
                    Severity = Severity.Additive,
                    TypeName = name,
                    Detail = $"`{name}` added",
                    Role = GetRole(roles, name),
                });
            }
        }

        // Changed classes
        foreach (var (name, oldCls) in old.Classes)
        {
            if (!@new.Classes.TryGetValue(name, out var newCls))
                continue;

            var role = GetRole(roles, name);

            // Base class changed
            if (oldCls.BaseClass != newCls.BaseClass)
            {
                result.Changes.Add(new Change
                {
                    Kind = ChangeKind.BaseClassChanged,
                    Severity = Severity.Breaking,
                    TypeName = name,
                    OldValue = oldCls.BaseClass ?? "(none)",
                    NewValue = newCls.BaseClass ?? "(none)",
                    Detail = $"`{name}`: base class changed from `{oldCls.BaseClass ?? "(none)"}` to `{newCls.BaseClass ?? "(none)"}`",
                    Role = role,
                });
            }

            // Interface changes
            DiffInterfaces(name, oldCls.Interfaces, newCls.Interfaces, role, result);

            // Property changes
            DiffProperties(name, oldCls.Properties, newCls.Properties, role, result);

            // Polymorphic changes
            DiffPolymorphic(name, oldCls.Polymorphic, newCls.Polymorphic, role, result);
        }
    }

    private static void DiffInterfaces(
        string typeName, List<string> oldIfaces, List<string> newIfaces,
        TypeRole role, DiffResult result)
    {
        var oldSet = new HashSet<string>(oldIfaces);
        var newSet = new HashSet<string>(newIfaces);

        foreach (var iface in oldSet.Except(newSet))
        {
            result.Changes.Add(new Change
            {
                Kind = ChangeKind.InterfaceRemoved,
                Severity = Severity.Breaking,
                TypeName = typeName,
                OldValue = iface,
                Detail = $"`{typeName}`: interface `{iface}` removed",
                Role = role,
            });
        }

        foreach (var iface in newSet.Except(oldSet))
        {
            result.Changes.Add(new Change
            {
                Kind = ChangeKind.InterfaceAdded,
                Severity = Severity.Additive,
                TypeName = typeName,
                NewValue = iface,
                Detail = $"`{typeName}`: interface `{iface}` added",
                Role = role,
            });
        }
    }

    private static void DiffProperties(
        string typeName, List<PropertyInfo> oldProps, List<PropertyInfo> newProps,
        TypeRole role, DiffResult result)
    {
        var oldMap = oldProps.ToDictionary(p => p.Name, StringComparer.Ordinal);
        var newMap = newProps.ToDictionary(p => p.Name, StringComparer.Ordinal);

        // Removed
        foreach (var (name, prop) in oldMap)
        {
            if (!newMap.ContainsKey(name))
            {
                var severity = role == TypeRole.Request && prop.IsNullable
                    ? Severity.Warning
                    : Severity.Breaking;

                result.Changes.Add(new Change
                {
                    Kind = ChangeKind.PropertyRemoved,
                    Severity = severity,
                    TypeName = typeName,
                    Field = name,
                    OldValue = prop.TypeExpr,
                    Detail = $"`{typeName}.{name}` removed (was `{prop.TypeExpr}`)",
                    Role = role,
                });
            }
        }

        // Added
        foreach (var (name, prop) in newMap)
        {
            if (!oldMap.ContainsKey(name))
            {
                // Adding a required field to a request type is breaking
                var severity = role == TypeRole.Request && prop.IsRequired
                    ? Severity.Breaking
                    : Severity.Additive;

                result.Changes.Add(new Change
                {
                    Kind = ChangeKind.PropertyAdded,
                    Severity = severity,
                    TypeName = typeName,
                    Field = name,
                    NewValue = prop.TypeExpr,
                    Detail = $"`{typeName}.{name}` added (`{prop.TypeExpr}`)",
                    Role = role,
                });
            }
        }

        // Changed
        foreach (var (name, oldProp) in oldMap)
        {
            if (!newMap.TryGetValue(name, out var newProp))
                continue;

            // Type changed (ignoring nullable suffix for optionality check)
            var oldBase = oldProp.TypeExpr.TrimEnd('?');
            var newBase = newProp.TypeExpr.TrimEnd('?');
            if (oldBase != newBase)
            {
                result.Changes.Add(new Change
                {
                    Kind = ChangeKind.PropertyTypeChanged,
                    Severity = Severity.Breaking,
                    TypeName = typeName,
                    Field = name,
                    OldValue = oldProp.TypeExpr,
                    NewValue = newProp.TypeExpr,
                    Detail = $"`{typeName}.{name}`: type changed `{oldProp.TypeExpr}` → `{newProp.TypeExpr}`",
                    Role = role,
                });
            }
            else if (oldProp.IsNullable != newProp.IsNullable)
            {
                // Optionality changed, same base type
                if (oldProp.IsNullable && !newProp.IsNullable)
                {
                    // Became required
                    var severity = role == TypeRole.Request ? Severity.Breaking : Severity.Info;
                    result.Changes.Add(new Change
                    {
                        Kind = ChangeKind.PropertyBecameRequired,
                        Severity = severity,
                        TypeName = typeName,
                        Field = name,
                        OldValue = oldProp.TypeExpr,
                        NewValue = newProp.TypeExpr,
                        Detail = $"`{typeName}.{name}`: became required (`{oldProp.TypeExpr}` → `{newProp.TypeExpr}`)",
                        Role = role,
                    });
                }
                else
                {
                    // Became optional — breaking for response types because existing
                    // code won't have null checks and can NPE at runtime or fail to compile
                    var severity = role == TypeRole.Response ? Severity.Breaking : Severity.Info;
                    result.Changes.Add(new Change
                    {
                        Kind = ChangeKind.PropertyBecameOptional,
                        Severity = severity,
                        TypeName = typeName,
                        Field = name,
                        OldValue = oldProp.TypeExpr,
                        NewValue = newProp.TypeExpr,
                        Detail = $"`{typeName}.{name}`: became optional (`{oldProp.TypeExpr}` → `{newProp.TypeExpr}`)",
                        Role = role,
                    });
                }
            }

            // JSON name changed (wire-level break)
            if (oldProp.JsonName != newProp.JsonName)
            {
                result.Changes.Add(new Change
                {
                    Kind = ChangeKind.PropertyJsonNameChanged,
                    Severity = Severity.Breaking,
                    TypeName = typeName,
                    Field = name,
                    OldValue = oldProp.JsonName,
                    NewValue = newProp.JsonName,
                    Detail = $"`{typeName}.{name}`: JSON name changed `{oldProp.JsonName}` → `{newProp.JsonName}`",
                    Role = role,
                });
            }
        }
    }

    private static void DiffPolymorphic(
        string typeName, PolymorphicInfo? oldPoly, PolymorphicInfo? newPoly,
        TypeRole role, DiffResult result)
    {
        if (oldPoly == null && newPoly == null)
            return;

        if (oldPoly == null && newPoly != null)
        {
            result.Changes.Add(new Change
            {
                Kind = ChangeKind.PolymorphicDiscriminatorChanged,
                Severity = Severity.Breaking,
                TypeName = typeName,
                NewValue = newPoly.Discriminator,
                Detail = $"`{typeName}`: became polymorphic (discriminator `{newPoly.Discriminator}`)",
                Role = role,
            });
            return;
        }

        if (oldPoly != null && newPoly == null)
        {
            result.Changes.Add(new Change
            {
                Kind = ChangeKind.PolymorphicDiscriminatorChanged,
                Severity = Severity.Breaking,
                TypeName = typeName,
                OldValue = oldPoly.Discriminator,
                Detail = $"`{typeName}`: no longer polymorphic (was discriminator `{oldPoly.Discriminator}`)",
                Role = role,
            });
            return;
        }

        // Both non-null
        if (oldPoly!.Discriminator != newPoly!.Discriminator)
        {
            result.Changes.Add(new Change
            {
                Kind = ChangeKind.PolymorphicDiscriminatorChanged,
                Severity = Severity.Breaking,
                TypeName = typeName,
                OldValue = oldPoly.Discriminator,
                NewValue = newPoly.Discriminator,
                Detail = $"`{typeName}`: discriminator changed `{oldPoly.Discriminator}` → `{newPoly.Discriminator}`",
                Role = role,
            });
        }

        var oldDerived = oldPoly.DerivedTypes.ToDictionary(d => d.TypeName, StringComparer.Ordinal);
        var newDerived = newPoly.DerivedTypes.ToDictionary(d => d.TypeName, StringComparer.Ordinal);

        foreach (var (dtName, _) in oldDerived)
        {
            if (!newDerived.ContainsKey(dtName))
            {
                result.Changes.Add(new Change
                {
                    Kind = ChangeKind.PolymorphicDerivedTypeRemoved,
                    Severity = Severity.Breaking,
                    TypeName = typeName,
                    Field = dtName,
                    Detail = $"`{typeName}`: derived type `{dtName}` removed",
                    Role = role,
                });
            }
        }

        foreach (var (dtName, _) in newDerived)
        {
            if (!oldDerived.ContainsKey(dtName))
            {
                result.Changes.Add(new Change
                {
                    Kind = ChangeKind.PolymorphicDerivedTypeAdded,
                    Severity = Severity.Additive,
                    TypeName = typeName,
                    Field = dtName,
                    Detail = $"`{typeName}`: derived type `{dtName}` added",
                    Role = role,
                });
            }
        }
    }

    // ── Enums ────────────────────────────────────────────────

    private static void DiffEnums(
        ApiSurface old, ApiSurface @new,
        Dictionary<string, TypeRole> roles, DiffResult result)
    {
        foreach (var (name, _) in old.Enums)
        {
            if (!@new.Enums.ContainsKey(name))
            {
                result.Changes.Add(new Change
                {
                    Kind = ChangeKind.EnumRemoved,
                    Severity = Severity.Breaking,
                    TypeName = name,
                    Detail = $"enum `{name}` removed",
                    Role = GetRole(roles, name),
                });
            }
        }

        foreach (var (name, _) in @new.Enums)
        {
            if (!old.Enums.ContainsKey(name))
            {
                result.Changes.Add(new Change
                {
                    Kind = ChangeKind.EnumAdded,
                    Severity = Severity.Additive,
                    TypeName = name,
                    Detail = $"enum `{name}` added",
                    Role = GetRole(roles, name),
                });
            }
        }

        foreach (var (name, oldEnum) in old.Enums)
        {
            if (!@new.Enums.TryGetValue(name, out var newEnum))
                continue;

            var role = GetRole(roles, name);
            DiffEnumMembers(name, oldEnum, newEnum, role, result);
        }
    }

    private static void DiffEnumMembers(
        string enumName, EnumInfo oldEnum, EnumInfo newEnum,
        TypeRole role, DiffResult result)
    {
        var oldMembers = oldEnum.Members.ToDictionary(m => m.Name, StringComparer.Ordinal);
        var newMembers = newEnum.Members.ToDictionary(m => m.Name, StringComparer.Ordinal);

        foreach (var (name, member) in oldMembers)
        {
            if (!newMembers.ContainsKey(name))
            {
                result.Changes.Add(new Change
                {
                    Kind = ChangeKind.EnumMemberRemoved,
                    Severity = Severity.Breaking,
                    TypeName = enumName,
                    Field = name,
                    OldValue = member.JsonName,
                    Detail = $"`{enumName}` lost member `{name}` (JSON: `{member.JsonName}`)",
                    Role = role,
                });
            }
        }

        foreach (var (name, member) in newMembers)
        {
            if (!oldMembers.ContainsKey(name))
            {
                result.Changes.Add(new Change
                {
                    Kind = ChangeKind.EnumMemberAdded,
                    Severity = Severity.Additive,
                    TypeName = enumName,
                    Field = name,
                    NewValue = member.JsonName,
                    Detail = $"`{enumName}` gained member `{name}` (JSON: `{member.JsonName}`)",
                    Role = role,
                });
            }
        }

        // Check for changes in existing members
        foreach (var (name, oldMember) in oldMembers)
        {
            if (!newMembers.TryGetValue(name, out var newMember))
                continue;

            // JSON name changed
            if (oldMember.JsonName != newMember.JsonName)
            {
                result.Changes.Add(new Change
                {
                    Kind = ChangeKind.EnumMemberJsonNameChanged,
                    Severity = Severity.Breaking,
                    TypeName = enumName,
                    Field = name,
                    OldValue = oldMember.JsonName,
                    NewValue = newMember.JsonName,
                    Detail = $"`{enumName}.{name}`: JSON name changed `{oldMember.JsonName}` → `{newMember.JsonName}`",
                    Role = role,
                });
            }

            // Became obsolete
            if (!oldMember.IsObsolete && newMember.IsObsolete)
            {
                result.Changes.Add(new Change
                {
                    Kind = ChangeKind.EnumMemberMarkedObsolete,
                    Severity = Severity.Info,
                    TypeName = enumName,
                    Field = name,
                    Detail = $"`{enumName}.{name}`: marked `[Obsolete]`",
                    Role = role,
                });
            }
        }
    }

    // ── Structs ──────────────────────────────────────────────

    private static void DiffStructs(
        ApiSurface old, ApiSurface @new,
        Dictionary<string, TypeRole> roles, DiffResult result)
    {
        foreach (var (name, _) in old.Structs)
        {
            if (!@new.Structs.ContainsKey(name))
            {
                result.Changes.Add(new Change
                {
                    Kind = ChangeKind.StructRemoved,
                    Severity = Severity.Breaking,
                    TypeName = name,
                    Detail = $"struct `{name}` removed",
                    Role = GetRole(roles, name),
                });
            }
        }

        foreach (var (name, _) in @new.Structs)
        {
            if (!old.Structs.ContainsKey(name))
            {
                result.Changes.Add(new Change
                {
                    Kind = ChangeKind.StructAdded,
                    Severity = Severity.Additive,
                    TypeName = name,
                    Detail = $"struct `{name}` added",
                    Role = GetRole(roles, name),
                });
            }
        }

        foreach (var (name, oldStruct) in old.Structs)
        {
            if (!@new.Structs.TryGetValue(name, out var newStruct))
                continue;

            var role = GetRole(roles, name);

            // Property changes
            DiffProperties(name, oldStruct.Properties, newStruct.Properties, role, result);

            // Interface changes on structs
            var oldIfaces = new HashSet<string>(oldStruct.Interfaces);
            var newIfaces = new HashSet<string>(newStruct.Interfaces);

            foreach (var iface in oldIfaces.Except(newIfaces))
            {
                result.Changes.Add(new Change
                {
                    Kind = ChangeKind.StructInterfaceRemoved,
                    Severity = Severity.Breaking,
                    TypeName = name,
                    OldValue = iface,
                    Detail = $"`{name}`: interface `{iface}` removed",
                    Role = role,
                });
            }

            foreach (var iface in newIfaces.Except(oldIfaces))
            {
                result.Changes.Add(new Change
                {
                    Kind = ChangeKind.StructInterfaceAdded,
                    Severity = Severity.Additive,
                    TypeName = name,
                    NewValue = iface,
                    Detail = $"`{name}`: interface `{iface}` added",
                    Role = role,
                });
            }

            // Implicit conversion changes
            var oldConvs = oldStruct.ImplicitConversions.Select(c => c.FromType).ToHashSet(StringComparer.Ordinal);
            var newConvs = newStruct.ImplicitConversions.Select(c => c.FromType).ToHashSet(StringComparer.Ordinal);

            foreach (var from in oldConvs.Except(newConvs))
            {
                result.Changes.Add(new Change
                {
                    Kind = ChangeKind.StructImplicitConversionRemoved,
                    Severity = Severity.Breaking,
                    TypeName = name,
                    OldValue = from,
                    Detail = $"`{name}`: implicit conversion from `{from}` removed",
                    Role = role,
                });
            }

            foreach (var from in newConvs.Except(oldConvs))
            {
                result.Changes.Add(new Change
                {
                    Kind = ChangeKind.StructImplicitConversionAdded,
                    Severity = Severity.Additive,
                    TypeName = name,
                    NewValue = from,
                    Detail = $"`{name}`: implicit conversion from `{from}` added",
                    Role = role,
                });
            }
        }
    }

    // ── Methods ──────────────────────────────────────────────

    private static void DiffMethods(ApiSurface old, ApiSurface @new, DiffResult result)
    {
        var oldMethods = old.ClientMethods.ToDictionary(m => m.Name, StringComparer.Ordinal);
        var newMethods = @new.ClientMethods.ToDictionary(m => m.Name, StringComparer.Ordinal);

        foreach (var (name, method) in oldMethods)
        {
            if (!newMethods.ContainsKey(name))
            {
                result.Changes.Add(new Change
                {
                    Kind = ChangeKind.MethodRemoved,
                    Severity = Severity.Breaking,
                    TypeName = "CamundaClient",
                    Field = name,
                    Detail = $"`CamundaClient.{name}()` removed",
                    Role = TypeRole.Unknown,
                });
            }
        }

        foreach (var (name, method) in newMethods)
        {
            if (!oldMethods.ContainsKey(name))
            {
                result.Changes.Add(new Change
                {
                    Kind = ChangeKind.MethodAdded,
                    Severity = Severity.Additive,
                    TypeName = "CamundaClient",
                    Field = name,
                    Detail = $"`CamundaClient.{name}()` added",
                    Role = TypeRole.Unknown,
                });
            }
        }

        foreach (var (name, oldMethod) in oldMethods)
        {
            if (!newMethods.TryGetValue(name, out var newMethod))
                continue;

            // Return type changed
            if (oldMethod.ReturnType != newMethod.ReturnType)
            {
                result.Changes.Add(new Change
                {
                    Kind = ChangeKind.MethodReturnTypeChanged,
                    Severity = Severity.Breaking,
                    TypeName = "CamundaClient",
                    Field = name,
                    OldValue = oldMethod.ReturnType,
                    NewValue = newMethod.ReturnType,
                    Detail = $"`CamundaClient.{name}()`: return type changed `{oldMethod.ReturnType}` → `{newMethod.ReturnType}`",
                    Role = TypeRole.Unknown,
                });
            }

            // Parameter changes
            var oldSig = string.Join(", ", oldMethod.Parameters.Select(p => $"{p.TypeExpr} {p.Name}"));
            var newSig = string.Join(", ", newMethod.Parameters.Select(p => $"{p.TypeExpr} {p.Name}"));
            if (oldSig != newSig)
            {
                result.Changes.Add(new Change
                {
                    Kind = ChangeKind.MethodParameterChanged,
                    Severity = Severity.Breaking,
                    TypeName = "CamundaClient",
                    Field = name,
                    OldValue = $"({oldSig})",
                    NewValue = $"({newSig})",
                    Detail = $"`CamundaClient.{name}()`: parameters changed `({oldSig})` → `({newSig})`",
                    Role = TypeRole.Unknown,
                });
            }
        }
    }
}
