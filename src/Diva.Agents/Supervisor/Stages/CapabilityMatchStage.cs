using Diva.Agents.Registry;
using Microsoft.Extensions.Logging;

namespace Diva.Agents.Supervisor.Stages;

/// <summary>
/// Matches each sub-task to the best available worker agent using capability scoring.
/// Sets DispatchPlan on the state.
/// </summary>
public sealed class CapabilityMatchStage : ISupervisorPipelineStage
{
    private readonly IAgentRegistry _registry;
    private readonly ILogger<CapabilityMatchStage> _logger;

    public CapabilityMatchStage(IAgentRegistry registry, ILogger<CapabilityMatchStage> logger)
    {
        _registry = registry;
        _logger   = logger;
    }

    public async Task<SupervisorState> ExecuteAsync(SupervisorState state, CancellationToken ct)
    {
        var plan = new List<(SubTask, Diva.Agents.Workers.IWorkerAgent)>();

        foreach (var task in state.SubTasks)
        {
            var agent = await _registry.FindBestMatchAsync(
                task.RequiredCapabilities,
                state.TenantContext.TenantId,
                ct);

            if (agent is null)
            {
                _logger.LogWarning("No agent found for capabilities [{Caps}] — skipping sub-task",
                    string.Join(", ", task.RequiredCapabilities));
                continue;
            }

            _logger.LogDebug("Matched task to agent {AgentId} (capabilities={Caps})",
                agent.GetCapability().AgentId,
                string.Join(", ", agent.GetCapability().Capabilities));

            plan.Add((task, agent));
        }

        if (plan.Count == 0)
        {
            state.Status       = SupervisorStatus.Failed;
            state.ErrorMessage = "No capable agents found for this request. Create or publish an agent first.";
            return state;
        }

        state.DispatchPlan = plan;
        return state;
    }
}
