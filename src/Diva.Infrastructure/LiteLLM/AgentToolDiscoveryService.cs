using Diva.Core.Configuration;
using Diva.Core.Models;
using Diva.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Diva.Infrastructure.LiteLLM;

/// <summary>
/// Discovers MCP tools for an agent by loading its bindings from DB and connecting
/// to the configured MCP servers via <see cref="IMcpConnectionManager"/>.
/// Uses <see cref="McpClientCache"/> internally — fast on cache hit.
/// All failures are caught and logged; the method never throws.
/// </summary>
public sealed class AgentToolDiscoveryService : IAgentToolDiscoveryService
{
    private const int TimeoutSeconds = 8;

    private readonly IDatabaseProviderFactory _db;
    private readonly IMcpConnectionManager _mcpConnMgr;
    private readonly ILogger<AgentToolDiscoveryService> _logger;

    public AgentToolDiscoveryService(
        IDatabaseProviderFactory db,
        IMcpConnectionManager mcpConnMgr,
        ILogger<AgentToolDiscoveryService> logger)
    {
        _db = db;
        _mcpConnMgr = mcpConnMgr;
        _logger = logger;
    }

    public async Task<IReadOnlyList<McpToolDetail>> DiscoverToolsAsync(
        string agentId, int tenantId, CancellationToken ct)
    {
        try
        {
            using var db = _db.CreateDbContext(TenantContext.System(tenantId));
            var agent = await db.AgentDefinitions.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == agentId && a.TenantId == tenantId, ct);

            if (agent is null || string.IsNullOrWhiteSpace(agent.ToolBindings)
                              || agent.ToolBindings.Trim() == "[]")
                return [];

            // Bounded timeout so slow MCP servers don't delay prompt generation
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

            var clients = await _mcpConnMgr.ConnectAsync(
                agent, cts.Token, TenantContext.System(tenantId));

            if (clients.Count == 0) return [];

            var (_, tools) = await _mcpConnMgr.BuildToolDataAsync(clients, cts.Token);

            return tools
                .Select(t => new McpToolDetail(t.Name, t.Description))
                .ToList();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogDebug(
                "MCP tool discovery timed out after {Timeout}s for agent {AgentId}",
                TimeoutSeconds, agentId);
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "MCP tool discovery failed for agent {AgentId} — returning empty list", agentId);
            return [];
        }
    }
}
