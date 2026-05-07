# Migration Guide: v9 → v10

This guide covers breaking changes and new features when upgrading from `Camunda.Orchestration.Sdk` v9 (Camunda 8.9) to v10 (Camunda 8.10).

> **Note:** v10 is currently in alpha (`10.0.0-alpha.N` on NuGet). The changes listed here may evolve before the stable v10.0.0 release.

## Package update

```xml
<!-- Before -->
<PackageReference Include="Camunda.Orchestration.Sdk" Version="9.*" />

<!-- After -->
<PackageReference Include="Camunda.Orchestration.Sdk" Version="10.*" />
```

Or via the CLI:

```bash
dotnet add package Camunda.Orchestration.Sdk --version "10.*-*"
```

## Breaking changes

### Type renames (bundler dedup)

The v10 generator uses an upgraded spec bundler that correctly preserves upstream schema names instead of inventing inline names. Several types that were previously generated as standalone classes are now replaced by their canonical upstream equivalents.

If your code references any of the old type names, update them to the new names:

| Removed type (v9) | Replacement (v10) |
|---|---|
| `CreateMappingRuleResponse` | `MappingRuleCreateResult` |
| `GetUserResponse` | `UserResult` |
| `SearchClientsForGroupRequest` | `GroupClientSearchQueryRequest` |
| `SearchClientsForGroupResponse` | `GroupClientSearchResult` |
| `SearchClientsForRoleRequest` | `RoleClientSearchQueryRequest` |
| `SearchClientsForRoleResponse` | `RoleClientSearchResult` |
| `SearchClientsForTenantRequest` | `TenantClientSearchQueryRequest` |
| `SearchClientsForTenantResponse` | `TenantClientSearchResult` |
| `SearchMappingRuleResponse` | `MappingRuleSearchQueryResult` |
| `SearchMappingRulesForGroupResponse` | `GroupMappingRuleSearchResult` |
| `SearchMappingRulesForRoleResponse` | `RoleMappingRuleSearchResult` |
| `SearchMappingRulesForTenantResponse` | `TenantMappingRuleSearchResult` |
| `SearchRolesForGroupResponse` | `GroupRoleSearchResult` |
| `SearchRolesForTenantResponse` | `TenantRoleSearchResult` |
| `SearchUsersForGroupRequest` | `GroupUserSearchQueryRequest` |
| `SearchUsersForGroupResponse` | `GroupUserSearchResult` |
| `SearchUsersForRoleRequest` | `RoleUserSearchQueryRequest` |
| `SearchUsersForRoleResponse` | `RoleUserSearchResult` |
| `SearchUsersForTenantRequest` | `TenantUserSearchQueryRequest` |
| `SearchUsersForTenantResponse` | `TenantUserSearchResult` |
| `SearchUsersResponse` | `UserSearchResult` |
| `SearchUserTaskEffectiveVariablesRequest` | `UserTaskEffectiveVariableSearchQueryRequest` |
| `SearchUserTaskVariablesRequest` | `UserTaskVariableSearchQueryRequest` |
| `SearchVariablesRequest` | `VariableSearchQuery` |
| `UpdateMappingRuleResponse` | `MappingRuleUpdateResult` |
| `UpdateUserResponse` | `UserUpdateResult` |

The replacement types are structurally identical — only the names change. A find-and-replace across your codebase is sufficient.

### Method signature changes

The following methods have updated parameter and/or return types to match the type renames above:

| Method | Changed parameter / return type |
|---|---|
| `CreateMappingRuleAsync` | Returns `MappingRuleCreateResult` (was `CreateMappingRuleResponse`) |
| `UpdateMappingRuleAsync` | Returns `MappingRuleUpdateResult` (was `UpdateMappingRuleResponse`) |
| `SearchMappingRuleAsync` | Returns `MappingRuleSearchQueryResult` (was `SearchMappingRuleResponse`) |
| `SearchClientsForGroupAsync` | Takes `GroupClientSearchQueryRequest`, returns `GroupClientSearchResult` |
| `SearchClientsForRoleAsync` | Takes `RoleClientSearchQueryRequest`, returns `RoleClientSearchResult` |
| `SearchClientsForTenantAsync` | Takes `TenantClientSearchQueryRequest`, returns `TenantClientSearchResult` |
| `SearchMappingRulesForGroupAsync` | Returns `GroupMappingRuleSearchResult` |
| `SearchMappingRulesForRoleAsync` | Returns `RoleMappingRuleSearchResult` |
| `SearchMappingRulesForTenantAsync` | Returns `TenantMappingRuleSearchResult` |
| `SearchRolesForGroupAsync` | Returns `GroupRoleSearchResult` |
| `SearchRolesForTenantAsync` | Returns `TenantRoleSearchResult` |
| `SearchUsersForGroupAsync` | Takes `GroupUserSearchQueryRequest`, returns `GroupUserSearchResult` |
| `SearchUsersForRoleAsync` | Takes `RoleUserSearchQueryRequest`, returns `RoleUserSearchResult` |
| `SearchUsersForTenantAsync` | Takes `TenantUserSearchQueryRequest`, returns `TenantUserSearchResult` |
| `SearchUserTaskEffectiveVariablesAsync` | Takes `UserTaskEffectiveVariableSearchQueryRequest` (was `SearchUserTaskEffectiveVariablesRequest`) |
| `SearchUserTaskVariablesAsync` | Takes `UserTaskVariableSearchQueryRequest` (was `SearchUserTaskVariablesRequest`) |
| `SearchVariablesAsync` | Takes `VariableSearchQuery` (was `SearchVariablesRequest`) |
| `GetUserAsync` | Returns `UserResult` (was `GetUserResponse`) |
| `SearchUsersAsync` | Returns `UserSearchResult` (was `SearchUsersResponse`) |
| `UpdateUserAsync` | Returns `UserUpdateResult` (was `UpdateUserResponse`) |

### Eventual consistency parameter types

`GetResourceAsync` and `GetResourceContentAsync` now accept an optional `ConsistencyOptions` parameter, matching the pattern used by all other eventually-consistent endpoints. If you pass these methods by reference or use them in delegates, you may need to update the signature:

```csharp
// Before (v9)
var resource = await client.GetResourceAsync(resourceKey);

// After (v10) — the call is unchanged, but the optional parameter exists
var resource = await client.GetResourceAsync(resourceKey);
// Or, with eventual consistency:
var resource = await client.GetResourceAsync(resourceKey,
    new ConsistencyOptions<ResourceResult> { WaitUpToMs = 5000 });
```

## New features

### Resource search

v10 adds a `SearchResourcesAsync` method with full search, filter, and sort support:

```csharp
var result = await client.SearchResourcesAsync(new ResourceSearchQuery
{
    Filter = new ResourceFilter { /* ... */ },
    Sort = [new ResourceSearchQuerySortRequest { Field = "..." }],
});
```

Supporting types: `ResourceSearchQuery`, `ResourceFilter`, `ResourceSearchQueryResult`, `ResourceSearchQuerySortRequest`.

### New filter properties

The following filter properties are now available on existing search filters:

- **`ElementIdFilterProperty`** — filter by element ID on flow node instance searches (`AdvancedElementIdFilter`, `ElementIdExactMatch`).
- **`ProcessDefinitionIdFilterProperty`** — filter by process definition ID (`AdvancedProcessDefinitionIdFilter`, `ProcessDefinitionIdExactMatch`).
- **`MessageSubscriptionTypeFilterProperty`** — filter message subscriptions by type (`AdvancedMessageSubscriptionTypeFilter`, `MessageSubscriptionTypeExactMatch`).

### New enum

- **`MessageSubscriptionTypeEnum`** — discriminates message subscription types.

## No changes

The following areas are **unchanged** between v9 and v10:

- **Target framework**: .NET 8.0+
- **Runtime behavior**: Auth, retry, backpressure, job workers, eventual consistency polling
- **Configuration**: All `CAMUNDA_*` environment variables and `CamundaOptions` properties
- **Branded types**: All existing `ICamundaKey` types (`ProcessDefinitionKey`, `UserTaskKey`, etc.) retain the same API. v10 adds new branded types: `GroupId`, `RoleId`, `ClientId`, `MappingRuleId`, `AgentInstanceKey`, `ClusterVariableName`
- **Enum handling**: `TolerantEnumConverter` continues to handle unknown enum values gracefully
