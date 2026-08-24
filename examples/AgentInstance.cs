// Compilable usage examples for agent instance operations.
// These examples are type-checked during build to guard against API regressions.
using Camunda.Orchestration.Sdk;

public static class AgentInstanceExamples
{
    #region GetAgentInstance
    // <GetAgentInstance>
    public static async Task GetAgentInstanceExample(AgentInstanceKey agentInstanceKey)
    {
        using var client = CamundaClient.Create();

        var result = await client.GetAgentInstanceAsync(agentInstanceKey);
        Console.WriteLine($"Agent instance: {result.AgentInstanceKey}, status: {result.Status}");
    }
    // </GetAgentInstance>
    #endregion GetAgentInstance

    #region SearchAgentInstances

    // <SearchAgentInstances>
    public static async Task SearchAgentInstancesExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchAgentInstancesAsync(new AgentInstanceSearchQuery());

        foreach (var instance in result.Items)
        {
            Console.WriteLine($"Agent instance: {instance.AgentInstanceKey}, status: {instance.Status}");
        }
    }
    // </SearchAgentInstances>
    #endregion SearchAgentInstances

    #region CreateAgentInstance

    // <CreateAgentInstance>
    public static async Task CreateAgentInstanceExample(
        ElementInstanceKey elementInstanceKey,
        JobKey jobKey,
        string jobLease)
    {
        using var client = CamundaClient.Create();

        // Every new agent instance must start with a CONFIGURATION history item
        // establishing model, provider, and systemPrompt (and, if needed, limits).
        var result = await client.CreateAgentInstanceAsync(new AgentInstanceCreationRequest
        {
            ElementInstanceKey = elementInstanceKey,
            JobKey = jobKey,
            JobLease = jobLease,
            History = new List<AgentInstanceHistoryItem>
            {
                new AgentInstanceHistoryItem
                {
                    HistoryItemId = "config-1",
                    LoopIteration = LoopIterationId.AssumeExists(0),
                    Role = AgentInstanceHistoryRoleEnum.CONFIGURATION,
                    Content = new List<AgentInstanceMessageContent>(),
                    ProducedAt = DateTimeOffset.UtcNow,
                    Model = "gpt-4o",
                    Provider = "openai",
                    SystemPrompt = new List<AgentInstanceMessageContent>
                    {
                        new AgentInstanceTextContent { Text = "You are a helpful assistant." },
                    },
                    Limits = new AgentInstanceLimits
                    {
                        MaxModelCalls = 20,
                        MaxToolCalls = 20,
                        MaxTokens = 100_000,
                    },
                },
            },
        });

        Console.WriteLine($"Created agent instance: {result.AgentInstanceKey}");
    }
    // </CreateAgentInstance>
    #endregion CreateAgentInstance

    #region UpdateAgentInstance

    // <UpdateAgentInstance>
    public static async Task UpdateAgentInstanceExample(
        AgentInstanceKey agentInstanceKey,
        ElementInstanceKey elementInstanceKey,
        JobKey jobKey,
        string jobLease)
    {
        using var client = CamundaClient.Create();

        // Additional conversation history (for example, the ASSISTANT response and its
        // metrics for the LLM call that just completed) is appended via the same
        // optional history batch used at creation time.
        await client.UpdateAgentInstanceAsync(
            agentInstanceKey,
            new AgentInstanceUpdateRequest
            {
                ElementInstanceKey = elementInstanceKey,
                Status = AgentInstanceUpdateStatusEnum.THINKING,
                JobKey = jobKey,
                JobLease = jobLease,
                History = new List<AgentInstanceHistoryItem>
                {
                    new AgentInstanceHistoryItem
                    {
                        HistoryItemId = "assistant-1",
                        LoopIteration = LoopIterationId.AssumeExists(0),
                        Role = AgentInstanceHistoryRoleEnum.ASSISTANT,
                        Content = new List<AgentInstanceMessageContent>
                        {
                            new AgentInstanceTextContent { Text = "How can I help you today?" },
                        },
                        ProducedAt = DateTimeOffset.UtcNow,
                        Metrics = new AgentInstanceHistoryItemMetrics
                        {
                            InputTokens = 150,
                            OutputTokens = 50,
                        },
                    },
                },
            });

        Console.WriteLine($"Updated agent instance: {agentInstanceKey}");
    }
    // </UpdateAgentInstance>
    #endregion UpdateAgentInstance

    #region SearchAgentInstanceHistory

    // <SearchAgentInstanceHistory>
    public static async Task SearchAgentInstanceHistoryExample(AgentInstanceKey agentInstanceKey)
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchAgentInstanceHistoryAsync(
            agentInstanceKey,
            new AgentInstanceHistorySearchQuery
            {
                Sort = new List<AgentInstanceHistorySearchQuerySortRequest>
                {
                    new AgentInstanceHistorySearchQuerySortRequest
                    {
                        Field = AgentInstanceHistorySearchQuerySortRequestField.ProducedAt,
                        Order = SortOrderEnum.ASC,
                    },
                },
                Page = new LimitPagination { Limit = 20 },
            });

        foreach (var item in result.Items)
        {
            Console.WriteLine($"{item.HistoryItemKey} ({item.Role})");
        }
    }
    // </SearchAgentInstanceHistory>
    #endregion SearchAgentInstanceHistory
}
