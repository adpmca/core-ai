using Diva.Core.Models;

namespace Diva.Core.Configuration;

/// <summary>
/// Discovers MCP tools configured for a given agent.
/// Uses the McpClientCache internally — fast on cache hit, falls back to live connection if miss.
/// Returns an empty list (never throws) if MCP servers are unreachable.
/// </summary>
public interface IAgentToolDiscoveryService
{
    /// <summary>
    /// Returns the MCP tools available to the agent.
    /// Connects to configured MCP servers (via cache) and lists their tools.
    /// Returns empty if the agent has no bindings, the agent is not found, or all connections fail.
    /// </summary>
    Task<IReadOnlyList<McpToolDetail>> DiscoverToolsAsync(
        string agentId, int tenantId, CancellationToken ct);
}
