using Diva.Core.Configuration;
using Diva.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Diva.Agents.Supervisor.Stages;

/// <summary>
/// Executes each agent from the dispatch plan. Runs sub-tasks in parallel when there are multiple.
/// Collects WorkerResults on the state (used by IntegrateStage and future VerifyStage).
/// </summary>
public sealed class DispatchStage : ISupervisorPipelineStage
{
    private readonly ILogger<DispatchStage> _logger;
    private readonly AgentOptions _agentOptions;

    public DispatchStage(ILogger<DispatchStage> logger, IOptions<AgentOptions> agentOptions)
    {
        _logger       = logger;
        _agentOptions = agentOptions.Value;
    }

    public async Task<SupervisorState> ExecuteAsync(SupervisorState state, CancellationToken ct)
    {
        var results    = new List<AgentResponse>();
        var lockObj    = new object();
        var timeoutSec = _agentOptions.SubAgentTimeoutSeconds;

        await Parallel.ForEachAsync(state.DispatchPlan, ct, async (plan, innerCt) =>
        {
            var (task, agent) = plan;
            var agentId       = agent.GetCapability().AgentId;

            _logger.LogInformation("Dispatching to agent {AgentId}: {Query}",
                agentId, task.Description);

            // Pass a fresh request with no SessionId — supervisor owns the session.
            // ParentSessionId links the worker's trace session back to the supervisor.
            var subRequest = new AgentRequest
            {
                Query           = task.Description,
                TriggerType     = state.Request.TriggerType,
                Metadata        = state.Request.Metadata,
                Instructions    = task.Instructions,
                ParentSessionId = state.SessionId,
            };

            AgentResponse result;
            try
            {
                using var subCts = timeoutSec > 0
                    ? CancellationTokenSource.CreateLinkedTokenSource(innerCt)
                    : null;
                subCts?.CancelAfter(TimeSpan.FromSeconds(timeoutSec));
                var effectiveCt = subCts?.Token ?? innerCt;

                result = await agent.ExecuteAsync(subRequest, state.TenantContext, effectiveCt);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Sub-agent timed out — treat as individual failure, let other agents continue
                _logger.LogWarning(
                    "Sub-agent {AgentId} timed out after {TimeoutSec}s — recording as failure",
                    agentId, timeoutSec);
                result = new AgentResponse
                {
                    Success   = false,
                    Content   = $"Sub-agent '{agentId}' timed out after {timeoutSec}s.",
                    AgentName = agentId,
                };
            }

            _logger.LogInformation("Agent {AgentId} completed: success={Success}, tools={Tools}",
                agentId, result.Success, string.Join(", ", result.ToolsUsed));

            lock (lockObj)
                results.Add(result);
        });

        state.WorkerResults = results;

        // Accumulate all tool evidence from worker results for the VerifyStage
        state.ToolEvidence = string.Join("\n\n", results
            .Where(r => !string.IsNullOrEmpty(r.ToolEvidence))
            .Select(r => $"[Agent: {r.AgentName}]\n{r.ToolEvidence}"));

        return state;
    }
}
