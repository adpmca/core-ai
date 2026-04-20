using System.Text.Json;
using System.Text.Json.Nodes;
using Diva.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Diva.Infrastructure.LiteLLM;

/// <summary>
/// Builds <see cref="AgentDelegationTool"/> instances from an agent's <c>DelegateAgentIdsJson</c> field.
/// Each delegate agent becomes a virtual tool the LLM can call during the ReAct loop.
/// </summary>
public sealed class AgentToolProvider(
    IAgentDelegationResolver agentResolver,
    ILogger<AgentToolProvider> logger)
{
    /// <summary>
    /// Resolves a JSON array of agent IDs to <see cref="AgentDelegationTool"/> instances.
    /// Missing or disabled agents are skipped with a warning.
    /// </summary>
    /// <param name="delegateAgentIdsJson">JSON array of agent IDs, e.g. <c>["agent-1","agent-2"]</c>.</param>
    /// <param name="tenantId">Tenant for agent registry lookup.</param>
    /// <param name="excludeAgentId">Current agent ID to prevent self-delegation.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<List<AgentDelegationTool>> BuildAgentToolsAsync(
        string delegateAgentIdsJson,
        int tenantId,
        string excludeAgentId,
        CancellationToken ct)
    {
        var tools = new List<AgentDelegationTool>();

        List<string>? agentIds;
        try
        {
            // Handle both ["id1","id2"] (string array) and [1,2] (number array) formats
            var jsonArray = JsonNode.Parse(delegateAgentIdsJson)?.AsArray();
            agentIds = jsonArray?.Select(n => n?.ToString() ?? "").Where(s => s.Length > 0).ToList();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Invalid DelegateAgentIdsJson: {Json}", delegateAgentIdsJson);
            return tools;
        }

        if (agentIds is null || agentIds.Count == 0)
            return tools;

        foreach (var agentId in agentIds)
        {
            if (string.IsNullOrWhiteSpace(agentId)) continue;

            // Prevent self-delegation
            if (agentId.Equals(excludeAgentId, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("Skipping self-delegation for agent {AgentId}", agentId);
                continue;
            }

            var info = await agentResolver.GetAgentInfoAsync(agentId, tenantId, ct);
            if (info is null)
            {
                logger.LogWarning("Delegate agent {AgentId} not found for tenant {TenantId} — skipping",
                    agentId, tenantId);
                continue;
            }

            // Parse agent ID as int if possible (for DB ID), else hash
            tools.Add(new AgentDelegationTool(
                agentId,
                info.Name,
                info.Description,
                info.Capabilities));

            logger.LogDebug("Added delegation tool for agent {AgentId} ({Name})",
                agentId, info.Name);
        }

        logger.LogInformation("Built {Count} agent delegation tool(s) from DelegateAgentIdsJson", tools.Count);
        return tools;
    }
}
