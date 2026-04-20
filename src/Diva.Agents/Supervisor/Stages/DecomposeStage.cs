using Microsoft.Extensions.Logging;

namespace Diva.Agents.Supervisor.Stages;

/// <summary>
/// Decomposes the incoming request into sub-tasks.
///
/// MVP: creates a single sub-task for the full query, using PreferredAgent capabilities if specified.
/// Future: LLM-based decomposition for multi-step queries.
/// </summary>
public sealed class DecomposeStage : ISupervisorPipelineStage
{
    private readonly ILogger<DecomposeStage> _logger;

    public DecomposeStage(ILogger<DecomposeStage> logger)
        => _logger = logger;

    public Task<SupervisorState> ExecuteAsync(SupervisorState state, CancellationToken ct)
    {
        // If PreferredAgent is set, target that specific agent type
        var capabilities = string.IsNullOrEmpty(state.Request.PreferredAgent)
            ? Array.Empty<string>()
            : [state.Request.PreferredAgent];

        var subTask = new SubTask(
            Description:          state.Request.Query,
            RequiredCapabilities: capabilities,
            SiteId:               state.TenantContext.CurrentSiteId,
            TenantId:             state.TenantContext.TenantId,
            Instructions:         state.SupervisorInstructions);

        state.SubTasks = [subTask];

        _logger.LogDebug("Decomposed query into {Count} sub-task(s), capabilities=[{Caps}]",
            state.SubTasks.Count,
            string.Join(", ", capabilities));

        return Task.FromResult(state);
    }
}
