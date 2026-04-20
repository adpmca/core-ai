using Diva.Core.Models;

namespace Diva.Agents.Workers;

/// <summary>
/// A worker agent that can execute a specific task on behalf of the supervisor.
/// </summary>
public interface IWorkerAgent
{
    AgentCapability GetCapability();

    Task<AgentResponse> ExecuteAsync(
        AgentRequest request,
        TenantContext tenant,
        CancellationToken ct);
}
