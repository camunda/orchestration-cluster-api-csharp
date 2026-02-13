using Camunda.Orchestration.Sdk.Api;

namespace Camunda.Orchestration.Sdk.Runtime;

/// <summary>
/// Extended deployment result with typed convenience properties for direct access
/// to deployed artifacts by category (processes, decisions, forms, etc.).
/// </summary>
public sealed class ExtendedDeploymentResponse
{
    /// <summary>The underlying raw deployment response.</summary>
    public CreateDeploymentResponse Raw { get; }

    /// <summary>The unique key identifying the deployment.</summary>
    public DeploymentKey DeploymentKey => Raw.DeploymentKey;

    /// <summary>The tenant ID associated with the deployment.</summary>
    public TenantId TenantId => Raw.TenantId;

    /// <summary>All items deployed by the request.</summary>
    public List<DeploymentMetadataResult> Deployments => Raw.Deployments;

    /// <summary>Deployed process definitions.</summary>
    public List<DeploymentProcessResult> Processes { get; }

    /// <summary>Deployed decision definitions.</summary>
    public List<DeploymentDecisionResult> Decisions { get; }

    /// <summary>Deployed decision requirements.</summary>
    public List<DeploymentDecisionRequirementsResult> DecisionRequirements { get; }

    /// <summary>Deployed forms.</summary>
    public List<DeploymentFormResult> Forms { get; }

    /// <summary>Deployed resources.</summary>
    public List<DeploymentResourceResult> Resources { get; }

    /// <summary>
    /// Creates an <see cref="ExtendedDeploymentResponse"/> from a raw
    /// <see cref="CreateDeploymentResponse"/>, sorting deployed items into typed buckets.
    /// </summary>
    public ExtendedDeploymentResponse(CreateDeploymentResponse raw)
    {
        Raw = raw;
        Processes = [];
        Decisions = [];
        DecisionRequirements = [];
        Forms = [];
        Resources = [];

        if (raw.Deployments == null)
            return;

        foreach (var d in raw.Deployments)
        {
            if (d.ProcessDefinition != null)
                Processes.Add(d.ProcessDefinition);
            if (d.DecisionDefinition != null)
                Decisions.Add(d.DecisionDefinition);
            if (d.DecisionRequirements != null)
                DecisionRequirements.Add(d.DecisionRequirements);
            if (d.Form != null)
                Forms.Add(d.Form);
            if (d.Resource != null)
                Resources.Add(d.Resource);
        }
    }
}
