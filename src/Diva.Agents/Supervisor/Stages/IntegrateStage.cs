using Microsoft.Extensions.Logging;

namespace Diva.Agents.Supervisor.Stages;

/// <summary>
/// Combines worker results into a single integrated response.
/// Single agent: passes through the result directly.
/// Multiple agents: concatenates with section headers.
/// </summary>
public sealed class IntegrateStage : ISupervisorPipelineStage
{
    private readonly ILogger<IntegrateStage> _logger;

    public IntegrateStage(ILogger<IntegrateStage> logger)
        => _logger = logger;

    public Task<SupervisorState> ExecuteAsync(SupervisorState state, CancellationToken ct)
    {
        var successful = state.WorkerResults.Where(r => r.Success).ToList();

        state.IntegratedResult = successful.Count switch
        {
            0 => state.ErrorMessage ?? "No results.",
            1 => successful[0].Content,
            _ => string.Join("\n\n---\n\n", successful.Select(r =>
                    $"**{r.AgentName ?? "Agent"}**\n{r.Content}"))
        };

        _logger.LogDebug("Integrated {Count} result(s) into {Length} chars",
            successful.Count, state.IntegratedResult.Length);

        return Task.FromResult(state);
    }
}
