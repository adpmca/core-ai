using Microsoft.Extensions.Logging;

namespace Diva.Agents.Supervisor.Stages;

/// <summary>
/// Final stage: marks the pipeline as completed and sets delivery metadata.
/// Future: route result to Dashboard, Email, Slack, Teams based on DeliveryChannel.
/// </summary>
public sealed class DeliverStage : ISupervisorPipelineStage
{
    private readonly ILogger<DeliverStage> _logger;

    public DeliverStage(ILogger<DeliverStage> logger)
        => _logger = logger;

    public Task<SupervisorState> ExecuteAsync(SupervisorState state, CancellationToken ct)
    {
        state.Status          = SupervisorStatus.Completed;
        state.DeliveryComplete = true;

        _logger.LogInformation("Pipeline completed: {Length} chars delivered via API",
            state.IntegratedResult.Length);

        return Task.FromResult(state);
    }
}
