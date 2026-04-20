using System.Text.Json;
using Diva.Core.Configuration;
using Diva.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Diva.Infrastructure.LiteLLM;

/// <summary>
/// Handles execution of <c>call_agent_*</c> tool calls by resolving and invoking local agents.
/// Tracks delegation depth to prevent infinite recursion.
/// </summary>
public sealed class AgentToolExecutor(
    IAgentDelegationResolver agentResolver,
    IOptions<A2AOptions> a2aOptions,
    IOptions<AgentOptions> agentOptions,
    ILogger<AgentToolExecutor> logger)
{
    /// <summary>
    /// Executes a delegation tool call by resolving the target agent and invoking it.
    /// </summary>
    /// <returns>Same tuple shape as <see cref="ToolExecutor.ExecuteAsync"/>.</returns>
    public async Task<(string Output, bool Failed, Exception? Error)> ExecuteAsync(
        AgentDelegationTool tool,
        string inputJson,
        TenantContext tenant,
        int currentDepth,
        int maxToolResultChars,
        CancellationToken ct,
        bool forwardSsoToMcp = false,
        string? parentSessionId = null)
    {
        var maxDepth = a2aOptions.Value.MaxDelegationDepth;
        if (currentDepth >= maxDepth)
        {
            var msg = $"Agent delegation depth limit reached ({maxDepth}). Cannot delegate further.";
            logger.LogWarning("Delegation blocked: agent {AgentId} at depth {Depth}/{Max}",
                tool.AgentId, currentDepth, maxDepth);
            return (msg, true, null);
        }

        // Parse input
        string query;
        string? context = null;
        try
        {
            var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(inputJson) ?? [];
            query = args.TryGetValue("query", out var q) ? q.GetString() ?? "" : "";
            if (args.TryGetValue("context", out var c)) context = c.GetString();
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Malformed input for agent delegation tool {Tool}", tool.Name);
            return ($"Error: Invalid input JSON for agent delegation: {ex.Message}", true, ex);
        }

        if (string.IsNullOrWhiteSpace(query))
            return ("Error: 'query' parameter is required for agent delegation.", true, null);

        // Build request with context
        var delegatedQuery = string.IsNullOrWhiteSpace(context)
            ? query
            : $"{query}\n\nAdditional context: {context}";

        var request = new AgentRequest
        {
            Query = delegatedQuery,
            ForwardSsoToMcp = forwardSsoToMcp,
            ParentSessionId = parentSessionId,
            Metadata = new Dictionary<string, object?>
            {
                ["a2a_local_depth"] = currentDepth + 1,
            },
        };

        logger.LogInformation(
            "Delegating to agent {AgentId} at depth {Depth}: {Query}",
            tool.AgentId, currentDepth + 1,
            query.Length > 100 ? query[..100] + "…" : query);

        // Execute with timeout — sub-agents run a full ReAct loop, so use the dedicated
        // SubAgentTimeoutSeconds (default 120 s) rather than the MCP ToolTimeoutSeconds (30 s).
        var timeoutSec = agentOptions.Value.SubAgentTimeoutSeconds;
        using var cts = timeoutSec > 0
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : null;
        cts?.CancelAfter(TimeSpan.FromSeconds(timeoutSec));
        var effectiveCt = cts?.Token ?? ct;

        try
        {
            var response = await agentResolver.ExecuteAgentAsync(
                tool.AgentId, request, tenant, effectiveCt);
            var output = response.Content ?? "(Agent returned no response)";
            output = ReActToolHelper.TruncateResult(output, maxToolResultChars);

            logger.LogInformation(
                "Agent delegation complete: {AgentId} returned {Len} chars, success={Success}",
                tool.AgentId, output.Length, response.Success);

            return (output, !response.Success, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Local sub-agent timeout fired — not the parent request being cancelled.
            var msg = $"Agent delegation to agent {tool.AgentId} timed out after {timeoutSec}s.";
            logger.LogWarning(msg);
            return (msg, true, new TimeoutException(msg));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Agent delegation to {AgentId} failed", tool.AgentId);
            return ($"Error delegating to agent: {ex.Message}", true, ex);
        }
    }
}
