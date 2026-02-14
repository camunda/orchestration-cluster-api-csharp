---
uid: Camunda.Orchestration.Sdk.CamundaClient.CreateProcessInstanceAsync(Camunda.Orchestration.Sdk.Api.ProcessInstanceCreationInstruction,System.Threading.CancellationToken)
example:
- *content
---


[!code-csharp[](../examples/ProcessInstanceExamples.cs#CreateProcessInstance)]


---
uid: Camunda.Orchestration.Sdk.CamundaClient.CancelProcessInstanceAsync(Camunda.Orchestration.Sdk.Api.ProcessInstanceKey,Camunda.Orchestration.Sdk.Api.CancelProcessInstanceRequest,System.Threading.CancellationToken)
example:
- *content
---


[!code-csharp[](../examples/ProcessInstanceExamples.cs#CancelProcessInstance)]


---
uid: Camunda.Orchestration.Sdk.CamundaClient.SearchProcessInstancesAsync(Camunda.Orchestration.Sdk.Api.SearchProcessInstancesRequest,Camunda.Orchestration.Sdk.Runtime.ConsistencyOptions{Camunda.Orchestration.Sdk.Api.SearchProcessInstancesResponse},System.Threading.CancellationToken)
example:
- *content
---


[!code-csharp[](../examples/ProcessInstanceExamples.cs#SearchProcessInstances)]


---
uid: Camunda.Orchestration.Sdk.CamundaClient.MigrateProcessInstanceAsync(Camunda.Orchestration.Sdk.Api.ProcessInstanceKey,Camunda.Orchestration.Sdk.Api.MigrateProcessInstanceRequest,System.Threading.CancellationToken)
example:
- *content
---


[!code-csharp[](../examples/ProcessInstanceExamples.cs#MigrateProcessInstance)]


---
uid: Camunda.Orchestration.Sdk.CamundaClient.ModifyProcessInstanceAsync(Camunda.Orchestration.Sdk.Api.ProcessInstanceKey,Camunda.Orchestration.Sdk.Api.ModifyProcessInstanceRequest,System.Threading.CancellationToken)
example:
- *content
---


[!code-csharp[](../examples/ProcessInstanceExamples.cs#ModifyProcessInstance)]


---
uid: Camunda.Orchestration.Sdk.CamundaClient.GetTopologyAsync(System.Threading.CancellationToken)
example:
- *content
---


[!code-csharp[](../examples/ClientExamples.cs#GetTopology)]


---
uid: Camunda.Orchestration.Sdk.CamundaClient.GetAuthenticationAsync(System.Threading.CancellationToken)
example:
- *content
---


[!code-csharp[](../examples/ClientExamples.cs#GetAuthentication)]


---
uid: Camunda.Orchestration.Sdk.CamundaClient.ActivateJobsAsync(Camunda.Orchestration.Sdk.Api.JobActivationRequest,System.Threading.CancellationToken)
example:
- *content
---


[!code-csharp[](../examples/JobExamples.cs#ActivateJobs)]


---
uid: Camunda.Orchestration.Sdk.CamundaClient.CompleteJobAsync(Camunda.Orchestration.Sdk.Api.JobKey,Camunda.Orchestration.Sdk.Api.JobCompletionRequest,System.Threading.CancellationToken)
example:
- *content
---


[!code-csharp[](../examples/JobExamples.cs#CompleteJob)]


---
uid: Camunda.Orchestration.Sdk.CamundaClient.FailJobAsync(Camunda.Orchestration.Sdk.Api.JobKey,Camunda.Orchestration.Sdk.Api.JobFailRequest,System.Threading.CancellationToken)
example:
- *content
---


[!code-csharp[](../examples/JobExamples.cs#FailJob)]


---
uid: Camunda.Orchestration.Sdk.CamundaClient.ThrowJobErrorAsync(Camunda.Orchestration.Sdk.Api.JobKey,Camunda.Orchestration.Sdk.Api.JobErrorRequest,System.Threading.CancellationToken)
example:
- *content
---


[!code-csharp[](../examples/JobExamples.cs#ThrowJobError)]


---
uid: Camunda.Orchestration.Sdk.CamundaClient.CreateJobWorker(Camunda.Orchestration.Sdk.Runtime.JobWorkerConfig,System.Func{Camunda.Orchestration.Sdk.Runtime.ActivatedJob,System.Threading.CancellationToken,System.Threading.Tasks.Task})
example:
- *content
---


[!code-csharp[](../examples/JobExamples.cs#JobWorker)]


---
uid: Camunda.Orchestration.Sdk.CamundaClient.SearchJobsAsync(Camunda.Orchestration.Sdk.Api.JobSearchQuery,Camunda.Orchestration.Sdk.Runtime.ConsistencyOptions{Camunda.Orchestration.Sdk.Api.SearchJobsResponse},System.Threading.CancellationToken)
example:
- *content
---


[!code-csharp[](../examples/JobExamples.cs#SearchJobs)]


---
uid: Camunda.Orchestration.Sdk.CamundaClient.DeployResourcesFromFilesAsync(System.String[],System.String,System.Threading.CancellationToken)
example:
- *content
---


[!code-csharp[](../examples/DeploymentExamples.cs#DeployResourcesFromFiles)]


---
uid: Camunda.Orchestration.Sdk.CamundaClient.DeleteResourceAsync(Camunda.Orchestration.Sdk.Api.ResourceKey,Camunda.Orchestration.Sdk.Api.DeleteResourceRequest,System.Threading.CancellationToken)
example:
- *content
---


[!code-csharp[](../examples/DeploymentExamples.cs#DeleteResource)]


---
uid: Camunda.Orchestration.Sdk.CamundaClient.CorrelateMessageAsync(Camunda.Orchestration.Sdk.Api.MessageCorrelationRequest,System.Threading.CancellationToken)
example:
- *content
---


[!code-csharp[](../examples/MessageExamples.cs#CorrelateMessage)]


---
uid: Camunda.Orchestration.Sdk.CamundaClient.PublishMessageAsync(Camunda.Orchestration.Sdk.Api.MessagePublicationRequest,System.Threading.CancellationToken)
example:
- *content
---


[!code-csharp[](../examples/MessageExamples.cs#PublishMessage)]


---
uid: Camunda.Orchestration.Sdk.CamundaClient.BroadcastSignalAsync(Camunda.Orchestration.Sdk.Api.SignalBroadcastRequest,System.Threading.CancellationToken)
example:
- *content
---


[!code-csharp[](../examples/MessageExamples.cs#BroadcastSignal)]


---
uid: Camunda.Orchestration.Sdk.CamundaClient.EvaluateDecisionAsync(Camunda.Orchestration.Sdk.Api.DecisionEvaluationInstruction,System.Threading.CancellationToken)
example:
- *content
---


[!code-csharp[](../examples/DecisionExamples.cs#EvaluateDecision)]


---
uid: Camunda.Orchestration.Sdk.CamundaClient.SearchDecisionDefinitionsAsync(Camunda.Orchestration.Sdk.Api.DecisionDefinitionSearchQuery,Camunda.Orchestration.Sdk.Runtime.ConsistencyOptions{Camunda.Orchestration.Sdk.Api.DecisionDefinitionSearchQueryResult},System.Threading.CancellationToken)
example:
- *content
---


[!code-csharp[](../examples/DecisionExamples.cs#SearchDecisionDefinitions)]


---
uid: Camunda.Orchestration.Sdk.CamundaClient.CompleteUserTaskAsync(Camunda.Orchestration.Sdk.Api.UserTaskKey,Camunda.Orchestration.Sdk.Api.UserTaskCompletionRequest,System.Threading.CancellationToken)
example:
- *content
---


[!code-csharp[](../examples/UserTaskExamples.cs#CompleteUserTask)]


---
uid: Camunda.Orchestration.Sdk.CamundaClient.AssignUserTaskAsync(Camunda.Orchestration.Sdk.Api.UserTaskKey,Camunda.Orchestration.Sdk.Api.UserTaskAssignmentRequest,System.Threading.CancellationToken)
example:
- *content
---


[!code-csharp[](../examples/UserTaskExamples.cs#AssignUserTask)]


---
uid: Camunda.Orchestration.Sdk.CamundaClient.UnassignUserTaskAsync(Camunda.Orchestration.Sdk.Api.UserTaskKey,System.Threading.CancellationToken)
example:
- *content
---


[!code-csharp[](../examples/UserTaskExamples.cs#UnassignUserTask)]


---
uid: Camunda.Orchestration.Sdk.CamundaClient.SearchUserTasksAsync(Camunda.Orchestration.Sdk.Api.SearchUserTasksRequest,Camunda.Orchestration.Sdk.Runtime.ConsistencyOptions{Camunda.Orchestration.Sdk.Api.SearchUserTasksResponse},System.Threading.CancellationToken)
example:
- *content
---


[!code-csharp[](../examples/UserTaskExamples.cs#SearchUserTasks)]


---
uid: Camunda.Orchestration.Sdk.CamundaClient.UpdateUserTaskAsync(Camunda.Orchestration.Sdk.Api.UserTaskKey,Camunda.Orchestration.Sdk.Api.UserTaskUpdateRequest,System.Threading.CancellationToken)
example:
- *content
---


[!code-csharp[](../examples/UserTaskExamples.cs#UpdateUserTask)]


---
uid: Camunda.Orchestration.Sdk.CamundaClient.GetIncidentAsync(Camunda.Orchestration.Sdk.Api.IncidentKey,Camunda.Orchestration.Sdk.Runtime.ConsistencyOptions{Camunda.Orchestration.Sdk.Api.GetIncidentResponse},System.Threading.CancellationToken)
example:
- *content
---


[!code-csharp[](../examples/IncidentExamples.cs#GetIncident)]


---
uid: Camunda.Orchestration.Sdk.CamundaClient.ResolveIncidentAsync(Camunda.Orchestration.Sdk.Api.IncidentKey,Camunda.Orchestration.Sdk.Api.IncidentResolutionRequest,System.Threading.CancellationToken)
example:
- *content
---


[!code-csharp[](../examples/IncidentExamples.cs#ResolveIncident)]


---
uid: Camunda.Orchestration.Sdk.CamundaClient.SearchIncidentsAsync(Camunda.Orchestration.Sdk.Api.IncidentSearchQuery,Camunda.Orchestration.Sdk.Runtime.ConsistencyOptions{Camunda.Orchestration.Sdk.Api.SearchIncidentsResponse},System.Threading.CancellationToken)
example:
- *content
---


[!code-csharp[](../examples/IncidentExamples.cs#SearchIncidents)]

