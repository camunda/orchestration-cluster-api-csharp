using System.Text.Json;

namespace Camunda.Orchestration.Sdk;

public partial class CamundaClient
{
    /// <summary>
    /// Fetch the variables declared by a DTO type for a process instance, mapping them onto a
    /// strongly-typed result.
    ///
    /// <para>
    /// The query is derived from the DTO's members (honouring <c>[JsonPropertyName]</c>): only
    /// the declared variable names are fetched via a <c>name $in [...]</c> filter, so memory is
    /// bounded by the DTO shape rather than the total number of variables on the process
    /// instance. Results are paged to exhaustion over the filtered set, collapsed by name, and
    /// parsed into a <see cref="VariableMap{T}"/>.
    /// </para>
    ///
    /// <para>
    /// Access modes on the returned map:
    /// <list type="bullet">
    /// <item><description>
    /// Lenient — <see cref="VariableMap{T}.Get(string)"/> /
    /// <see cref="VariableMap{T}.Get{TValue}(string)"/> tolerate absent variables.
    /// </description></item>
    /// <item><description>
    /// Strict — <see cref="VariableMap{T}.Validate"/> constructs the DTO and throws if a
    /// required member is absent.
    /// </description></item>
    /// </list>
    /// </para>
    ///
    /// <example>
    /// <code>
    /// public record OrderVars(string OrderId, decimal? Amount);
    ///
    /// var vars = await client.SearchVariablesAsDtoAsync&lt;OrderVars&gt;(processInstanceKey);
    /// var amount = vars.Get&lt;decimal&gt;("amount");   // lenient
    /// var typed = vars.Validate();                    // strict: throws if OrderId missing
    /// </code>
    /// </example>
    /// </summary>
    /// <typeparam name="T">The DTO type declaring the variables to fetch.</typeparam>
    /// <param name="processInstanceKey">The process instance whose variables to search.</param>
    /// <param name="scopeKey">
    /// Optional scope key to disambiguate variables that exist at multiple scopes. When omitted
    /// and a declared variable resolves to more than one scope, a
    /// <see cref="VariableScopeCollisionException"/> is thrown.
    /// </param>
    /// <param name="tenantId">Optional tenant ID filter.</param>
    /// <param name="pageSize">The page size used while paging the filtered result set.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="VariableMap{T}"/> over the declared variables.</returns>
    /// <exception cref="ArgumentOutOfRangeException">When <paramref name="pageSize"/> is less than 1.</exception>
    /// <exception cref="VariableScopeCollisionException">
    /// When a declared variable is found at more than one scope and no <paramref name="scopeKey"/>
    /// was provided.
    /// </exception>
    /// <exception cref="VariableDeserializationException">
    /// When a present variable value is not valid JSON.
    /// </exception>
    public async Task<VariableMap<T>> SearchVariablesAsDtoAsync<T>(
        ProcessInstanceKey processInstanceKey,
        ScopeKey? scopeKey = null,
        TenantId? tenantId = null,
        int pageSize = 100,
        CancellationToken ct = default)
        where T : class
    {
        if (pageSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, "pageSize must be >= 1.");
        }

        var fields = TypedVariableSearch.ExtractFields(typeof(T), _jsonOptions);
        if (fields.Count == 0)
        {
            return new VariableMap<T>(
                new Dictionary<string, JsonElement>(StringComparer.Ordinal),
                _jsonOptions);
        }

        var queryNames = fields.Select(field => field.VariableName).ToList();
        var collector = new TypedVariableSearch.VariableCollector(queryNames);

        EndCursor? after = null;
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var query = BuildVariableQuery(
                queryNames,
                processInstanceKey,
                scopeKey,
                tenantId,
                pageSize,
                after);

            var result = await SearchVariablesAsync(query, truncateValues: false, ct: ct)
                .ConfigureAwait(false);

            collector.Ingest(result.Items);

            var endCursor = result.Page.EndCursor;
            if (endCursor is null
                || result.Items.Count == 0
                || !seenCursors.Add(endCursor.Value.Value))
            {
                break;
            }

            after = endCursor;
        }

        return new VariableMap<T>(collector.Build(), _jsonOptions);
    }

    private static VariableSearchQuery BuildVariableQuery(
        List<string> queryNames,
        ProcessInstanceKey processInstanceKey,
        ScopeKey? scopeKey,
        TenantId? tenantId,
        int pageSize,
        EndCursor? after)
    {
        var filter = new VariableFilter
        {
            // queryNames is already materialized and never mutated here, so it is safe to
            // share the same list reference across page queries.
            Name = new StringFilterProperty { In = queryNames },
            ProcessInstanceKey = new ProcessInstanceKeyFilterProperty { Eq = processInstanceKey },
        };

        if (scopeKey is { } scope)
        {
            filter.ScopeKey = new ScopeKeyFilterProperty { Eq = scope };
        }

        if (tenantId is { } tenant)
        {
            filter.TenantId = tenant;
        }

        SearchQueryPageRequest page = after is { } cursor
            ? new CursorForwardPagination { After = cursor, Limit = pageSize }
            : new LimitPagination { Limit = pageSize };

        return new VariableSearchQuery
        {
            Filter = filter,
            Page = page,
        };
    }
}
