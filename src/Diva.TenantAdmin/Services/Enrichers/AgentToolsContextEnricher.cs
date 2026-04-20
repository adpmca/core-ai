using Diva.Core.Configuration;
using Diva.Core.Models;
using Microsoft.Extensions.Logging;

namespace Diva.TenantAdmin.Services.Enrichers;

/// <summary>
/// Enriches <see cref="AgentSetupContext"/> with actual MCP tool names/descriptions
/// and resolved delegate sub-agent details before the LLM prompt suggestion call.
///
/// MCP tools: discovered via <see cref="IAgentToolDiscoveryService"/> (cache-first, 8s timeout).
/// Delegates: resolved via <see cref="IAgentDelegationResolver"/> using IDs sent by the frontend.
///
/// Requires <see cref="AgentSetupContext.AgentId"/> to be set (only available for saved agents).
/// All failures are caught and logged — enrichment is best-effort and never blocks the LLM call.
/// </summary>
public sealed class AgentToolsContextEnricher : ISetupAssistantContextEnricher
{
    private readonly IAgentToolDiscoveryService _toolDiscovery;
    private readonly IAgentDelegationResolver _delegationResolver;
    private readonly ILogger<AgentToolsContextEnricher> _logger;

    public AgentToolsContextEnricher(
        IAgentToolDiscoveryService toolDiscovery,
        IAgentDelegationResolver delegationResolver,
        ILogger<AgentToolsContextEnricher> logger)
    {
        _toolDiscovery = toolDiscovery;
        _delegationResolver = delegationResolver;
        _logger = logger;
    }

    public async ValueTask EnrichAsync(AgentSetupContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ctx.AgentId)) return;

        try
        {
            // 1. MCP tools — actual function names + descriptions
            var tools = await _toolDiscovery.DiscoverToolsAsync(ctx.AgentId, ctx.TenantId, ct);
            ctx.McpTools = tools.ToList();

            // 2. Delegate agents — resolve full details from IDs passed by frontend
            if (ctx.DelegateAgentIds.Length > 0)
            {
                var details = new List<DelegateAgentDetail>(ctx.DelegateAgentIds.Length);
                foreach (var id in ctx.DelegateAgentIds)
                {
                    var info = await _delegationResolver.GetAgentInfoAsync(id, ctx.TenantId, ct);
                    if (info is not null)
                        details.Add(new DelegateAgentDetail(
                            info.AgentId, info.Name, info.Description, info.Capabilities));
                }
                ctx.DelegateAgents = details;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "AgentToolsContextEnricher failed for agent {AgentId} — enrichment skipped",
                ctx.AgentId);
        }
    }
}
