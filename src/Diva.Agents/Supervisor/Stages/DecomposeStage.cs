using Diva.Agents.Supervisor.Decompose;
using Microsoft.Extensions.Logging;

namespace Diva.Agents.Supervisor.Stages;

/// <summary>
/// Decomposes the incoming request into sub-tasks by delegating to the highest-priority
/// applicable IDecompositionStrategy. Open for extension via new strategy registrations;
/// closed for modification (OCP).
///
/// Pipeline order: AgentContextStage (loads agents) → DecomposeStage → CapabilityMatchStage.
/// </summary>
public sealed class DecomposeStage : ISupervisorPipelineStage
{
    private readonly DecompositionStrategySelector _selector;
    private readonly ILogger<DecomposeStage> _logger;

    public DecomposeStage(DecompositionStrategySelector selector, ILogger<DecomposeStage> logger)
    {
        _selector = selector;
        _logger   = logger;
    }

    public async Task<SupervisorState> ExecuteAsync(SupervisorState state, CancellationToken ct)
    {
        var strategy = _selector.Select(state);

        _logger.LogInformation(
            "DecomposeStage: strategy={Strategy} availableAgents={AgentCount} preferredAgent={PreferredAgent}",
            strategy.GetType().Name,
            state.AvailableAgents.Count,
            string.IsNullOrEmpty(state.Request.PreferredAgent) ? "(none)" : state.Request.PreferredAgent);

        state.SubTasks = await strategy.DecomposeAsync(state, ct);

        _logger.LogInformation("DecomposeStage: produced {Count} sub-task(s)", state.SubTasks.Count);

        return state;
    }
}
