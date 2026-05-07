using Microsoft.Extensions.Logging;

namespace Diva.Agents.Supervisor.Decompose;

/// <summary>
/// Default strategy: wraps the entire query as a single sub-task.
/// Preserves the exact behavior of the original DecomposeStage MVP.
/// Priority = 0 (lowest — chosen as fallback when no other strategy handles the state).
/// </summary>
public sealed class SingleTaskStrategy : IDecompositionStrategy
{
    private readonly ILogger<SingleTaskStrategy> _logger;

    public int Priority => 0;

    public SingleTaskStrategy(ILogger<SingleTaskStrategy> logger) => _logger = logger;

    public bool CanHandle(SupervisorState state) => true;

    public Task<List<SubTask>> DecomposeAsync(SupervisorState state, CancellationToken ct)
    {
        var capabilities = string.IsNullOrEmpty(state.Request.PreferredAgent)
            ? Array.Empty<string>()
            : [state.Request.PreferredAgent];

        var subTask = new SubTask(
            Description:          state.Request.Query,
            RequiredCapabilities: capabilities,
            SiteId:               state.TenantContext.CurrentSiteId,
            TenantId:             state.TenantContext.TenantId,
            Instructions:         state.SupervisorInstructions);

        _logger.LogDebug("SingleTaskStrategy: 1 sub-task, capabilities=[{Caps}]",
            string.Join(", ", capabilities));

        return Task.FromResult(new List<SubTask> { subTask });
    }
}
