using Diva.Agents.Workers;

namespace Diva.Agents.Registry;

/// <summary>
/// Read-only view of the agent registry. All pipeline stages and read-only consumers
/// depend on this interface. Implemented by both DynamicAgentRegistry (full registry)
/// and the planned ScopedAgentRegistry (Phase 19) which has no Register() operation.
/// </summary>
public interface IReadableAgentRegistry
{
    /// <summary>Get all enabled, published agents for a tenant (static + dynamic from DB).</summary>
    Task<List<IWorkerAgent>> GetAgentsForTenantAsync(int tenantId, CancellationToken ct);

    /// <summary>Find the best-matching agent for a set of required capabilities.</summary>
    Task<IWorkerAgent?> FindBestMatchAsync(
        string[] requiredCapabilities,
        int tenantId,
        CancellationToken ct);

    /// <summary>Look up a specific agent by ID.</summary>
    Task<IWorkerAgent?> GetByIdAsync(string agentId, int tenantId, CancellationToken ct);
}
