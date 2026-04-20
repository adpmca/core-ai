namespace Diva.Agents.Supervisor;

/// <summary>
/// A single stage in the supervisor pipeline.
/// Each stage receives and returns the mutable SupervisorState.
/// </summary>
public interface ISupervisorPipelineStage
{
    Task<SupervisorState> ExecuteAsync(SupervisorState state, CancellationToken ct);
}
