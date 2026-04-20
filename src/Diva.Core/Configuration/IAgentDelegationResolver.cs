using Diva.Core.Models;

namespace Diva.Core.Configuration;

/// <summary>
/// Minimal interface for resolving delegate agents in the tool pipeline.
/// Lives in Diva.Core to avoid circular dependency between Infrastructure and Agents.
/// Implemented by <c>DelegationAgentResolver</c> in Diva.Agents.
/// </summary>
public interface IAgentDelegationResolver
{
    /// <summary>
    /// Gets agent metadata for building delegation tool definitions.
    /// Returns null if agent not found or not available for the given tenant.
    /// </summary>
    Task<DelegateAgentInfo?> GetAgentInfoAsync(string agentId, int tenantId, CancellationToken ct);

    /// <summary>
    /// Executes a delegate agent with the given request.
    /// </summary>
    Task<AgentResponse> ExecuteAgentAsync(string agentId, AgentRequest request, TenantContext tenant, CancellationToken ct);
}

/// <summary>Agent metadata needed to build a delegation tool definition.</summary>
public sealed record DelegateAgentInfo(
    string AgentId,
    string Name,
    string? Description,
    string[]? Capabilities);
