using Diva.Core.Models;
using ModelContextProtocol.Client;

namespace Diva.Infrastructure.LiteLLM;

/// <summary>
/// Reduces the active tool list to the subset most relevant for the current query.
/// Applied after allow/deny and ExecutionMode filtering, before strategy.Initialize().
/// Implementations must never throw — return original lists on any failure.
/// </summary>
public interface IToolSelectionStrategy
{
    Task<(List<McpClientTool> Tools, Dictionary<string, McpClient> ClientMap)> SelectAsync(
        string query,
        List<McpClientTool> allMcpTools,
        Dictionary<string, McpClient> toolClientMap,
        SupervisorLlmOverride llmOverride,
        CancellationToken ct);
}
