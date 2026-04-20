using Diva.Core.Models;

namespace Diva.Agents.Supervisor;

/// <summary>
/// Entry point for the supervisor pipeline. Routes queries to appropriate worker agents.
/// </summary>
public interface ISupervisorAgent
{
    Task<AgentResponse> InvokeAsync(
        AgentRequest request,
        TenantContext tenant,
        CancellationToken ct);
}
