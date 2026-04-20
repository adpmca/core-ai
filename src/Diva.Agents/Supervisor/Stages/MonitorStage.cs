using Microsoft.Extensions.Logging;

namespace Diva.Agents.Supervisor.Stages;

/// <summary>
/// Monitors worker results for failures, partial completions, or retry conditions.
/// MVP: marks the pipeline as failed if all workers failed, continues otherwise.
/// </summary>
public sealed class MonitorStage : ISupervisorPipelineStage
{
    private readonly ILogger<MonitorStage> _logger;

    public MonitorStage(ILogger<MonitorStage> logger)
        => _logger = logger;

    public Task<SupervisorState> ExecuteAsync(SupervisorState state, CancellationToken ct)
    {
        var failed    = state.WorkerResults.Count(r => !r.Success);
        var succeeded = state.WorkerResults.Count(r => r.Success);

        if (failed > 0)
            _logger.LogWarning("{Failed}/{Total} worker(s) failed", failed, state.WorkerResults.Count);

        if (succeeded == 0 && state.WorkerResults.Count > 0)
        {
            state.Status       = SupervisorStatus.Failed;
            state.ErrorMessage = state.WorkerResults.FirstOrDefault()?.ErrorMessage
                ?? "All worker agents failed.";
        }

        return Task.FromResult(state);
    }
}
