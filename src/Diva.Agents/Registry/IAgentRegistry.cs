using Diva.Agents.Workers;

namespace Diva.Agents.Registry;

/// <summary>
/// Resolves worker agents by capability. Combines statically registered agents
/// with dynamically loaded agents from the database.
/// </summary>
public interface IAgentRegistry
{
    /// <summary>Register a static (code-defined) agent.</summary>
    void Register(IWorkerAgent agent);

    /// <summary>Get all enabled, published agents for a tenant (static + dynamic from DB).</summary>
    Task<List<IWorkerAgent>> GetAgentsForTenantAsync(int tenantId, CancellationToken ct);

    /// <summary>Find the best-matching agent for a set of required capabilities.</summary>
    Task<IWorkerAgent?> FindBestMatchAsync(
        string[] requiredCapabilities,
        int tenantId,
        CancellationToken ct);

    /// <summary>Look up a specific agent by ID (DB lookup + static fallback).</summary>
    Task<IWorkerAgent?> GetByIdAsync(string agentId, int tenantId, CancellationToken ct);
}
