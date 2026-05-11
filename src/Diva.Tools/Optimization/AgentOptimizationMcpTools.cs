using System.ComponentModel;
using System.Text.Json;
using Diva.Core.Configuration;
using Diva.Infrastructure.Data;
using Diva.Infrastructure.Data.Entities;
using Diva.Infrastructure.Optimization;
using Diva.Tools.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace Diva.Tools.Optimization;

[McpServerToolType]
public sealed class AgentOptimizationMcpTools(
    IHttpContextAccessor http,
    IServiceScopeFactory scopeFactory,
    IOptions<AgentOptions> opts,
    ILogger<AgentOptimizationMcpTools> logger) : IDivaMcpToolType
{
    private readonly AgentOptions _opts = opts.Value;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented        = false
    };

    private string NotFound(string message) =>
        JsonSerializer.Serialize(new { error = "NotFound", message }, _json);

    private string Error(string message) =>
        JsonSerializer.Serialize(new { error = "Error", message }, _json);

    // ─────────────────────────────────────────────────────────────────────────

    [McpServerTool, Description("Get aggregated quality metrics and trace stats for an agent over a date range.")]
    public async Task<string> GetAgentMetrics(
        [Description("Agent ID")] string agentId,
        [Description("ISO date from (default: 30 days ago)")] string? from = null,
        [Description("ISO date to (default: now)")] string? to = null)
    {
        var ctx = McpServerContext.FromHttpContext(http);
        if (!ctx.IsAuthenticated) return Error("Unauthenticated");

        try
        {
            await using var scope   = scopeFactory.CreateAsyncScope();
            var traceDb = scope.ServiceProvider.GetRequiredService<SessionTraceDbContext>();
            var fromDate = from is not null ? DateTime.Parse(from) : DateTime.UtcNow.AddDays(-30);
            var toDate   = to   is not null ? DateTime.Parse(to)   : DateTime.UtcNow;

            var sessions = await traceDb.TraceSessions
                .Where(s => s.AgentId == agentId && s.TenantId == ctx.TenantId
                         && s.CreatedAt >= fromDate && s.CreatedAt <= toDate)
                .ToListAsync();

            var sessionIds = sessions.Select(s => s.SessionId).ToList();
            var turns = await traceDb.TraceSessionTurns
                .Where(t => sessionIds.Contains(t.SessionId))
                .ToListAsync();

            var scored = turns.Where(t => t.ScoresAvailable).ToList();

            return JsonSerializer.Serialize(new
            {
                agentId,
                from             = fromDate,
                to               = toDate,
                totalSessions    = sessions.Count,
                totalTurns       = turns.Count,
                scoredTurns      = scored.Count,
                avgFaithfulness  = scored.Count > 0 ? (double?)scored.Average(t => t.FaithfulnessScore!.Value) : null,
                avgCompleteness  = scored.Count > 0 ? (double?)scored.Average(t => t.CompletenessScore!.Value)  : null,
                avgToolEfficiency= scored.Count > 0 ? (double?)scored.Average(t => t.ToolEfficiencyScore!.Value): null,
                avgCoherence     = scored.Count > 0 ? (double?)scored.Average(t => t.CoherenceScore!.Value)     : null,
                verificationFailureRate = turns.Count > 0
                    ? (double)turns.Count(t => t.VerificationPassed == false) / turns.Count : 0,
                avgIterationsPerTurn = turns.Count > 0 ? turns.Average(t => (double)t.TotalIterations) : 0
            }, _json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "get_agent_metrics failed for agent {AgentId}", agentId);
            return Error(ex.Message);
        }
    }

    [McpServerTool, Description("Get an agent's current configuration (system prompt, temperature, iterations, verification mode).")]
    public async Task<string> GetAgentConfig([Description("Agent ID")] string agentId)
    {
        var ctx = McpServerContext.FromHttpContext(http);
        if (!ctx.IsAuthenticated) return Error("Unauthenticated");

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DivaDbContext>();
            var agent = await db.AgentDefinitions
                .FirstOrDefaultAsync(a => a.Id == agentId && a.TenantId == ctx.TenantId);

            if (agent is null) return NotFound($"Agent {agentId} not found");

            return JsonSerializer.Serialize(new
            {
                id               = agent.Id,
                name             = agent.Name,
                agentType        = agent.AgentType,
                temperature      = agent.Temperature,
                maxIterations    = agent.MaxIterations,
                maxContinuations = agent.MaxContinuations,
                verificationMode = agent.VerificationMode,
                systemPrompt     = agent.SystemPrompt != null
                    ? Truncate(agent.SystemPrompt, 2000)
                    : null
            }, _json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "get_agent_config failed for agent {AgentId}", agentId);
            return Error(ex.Message);
        }
    }

    [McpServerTool, Description("Get the conversation transcript of a specific session (tenant-scoped).")]
    public async Task<string> GetSessionConversation(
        [Description("Session ID")] string sessionId,
        [Description("Max characters to return")] int? maxChars = null)
    {
        var ctx = McpServerContext.FromHttpContext(http);
        if (!ctx.IsAuthenticated) return Error("Unauthenticated");

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db      = scope.ServiceProvider.GetRequiredService<DivaDbContext>();
            var traceDb = scope.ServiceProvider.GetRequiredService<SessionTraceDbContext>();

            // Always query via parent session for tenant safety
            var session = await db.Sessions
                .Include(s => s.Messages.OrderBy(m => m.CreatedAt))
                .FirstOrDefaultAsync(s => s.Id == sessionId && s.TenantId == ctx.TenantId);

            if (session is null) return NotFound($"Session {sessionId} not found");

            // Get agentId from trace session (AgentSessionEntity stores agent type, not ID)
            var traceSession = await traceDb.TraceSessions
                .FirstOrDefaultAsync(s => s.SessionId == sessionId);

            var limit = maxChars ?? _opts.Optimization.MaxTranscriptChars;
            var sb    = new System.Text.StringBuilder();
            foreach (var m in session.Messages)
            {
                var line = $"{m.Role}: {m.Content}\n";
                if (sb.Length + line.Length > limit) break;
                sb.Append(line);
            }

            return JsonSerializer.Serialize(new
            {
                sessionId,
                agentId   = traceSession?.AgentId,
                createdAt = session.CreatedAt,
                turns     = session.Messages.GroupBy(m => m.Role).Count(),
                transcript= sb.ToString()
            }, _json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "get_session_conversation failed for session {SessionId}", sessionId);
            return Error(ex.Message);
        }
    }

    [McpServerTool, Description("Get a sample of turns from a session's trace data, optionally filtering to failed verification turns.")]
    public async Task<string> GetSessionTurnsSample(
        [Description("Session ID")] string sessionId,
        [Description("Max turns to return")] int? limit = null,
        [Description("Only return turns where verification failed")] bool failedOnly = false)
    {
        var ctx = McpServerContext.FromHttpContext(http);
        if (!ctx.IsAuthenticated) return Error("Unauthenticated");

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var traceDb = scope.ServiceProvider.GetRequiredService<SessionTraceDbContext>();

            // Verify session belongs to tenant via trace DB (TenantId stored on TraceSession)
            var traceSession = await traceDb.TraceSessions
                .FirstOrDefaultAsync(s => s.SessionId == sessionId && s.TenantId == ctx.TenantId);
            if (traceSession is null) return NotFound($"Session {sessionId} not found");

            var query = traceDb.TraceSessionTurns.Where(t => t.SessionId == sessionId);
            if (failedOnly) query = query.Where(t => t.VerificationPassed == false);

            var turns = await query.OrderBy(t => t.TurnNumber).Take(limit ?? 10).ToListAsync();

            return JsonSerializer.Serialize(turns.Select(t => new
            {
                t.TurnNumber,
                userMessage      = Truncate(t.UserMessage, 300),
                assistantMessage = Truncate(t.AssistantMessage, 500),
                t.TotalIterations,
                t.TotalToolCalls,
                t.VerificationPassed,
                t.ScoresAvailable,
                faithfulness     = t.FaithfulnessScore,
                completeness     = t.CompletenessScore,
                toolEfficiency   = t.ToolEfficiencyScore,
                coherence        = t.CoherenceScore
            }), _json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "get_session_turns_sample failed for session {SessionId}", sessionId);
            return Error(ex.Message);
        }
    }

    [McpServerTool, Description("Get tool usage statistics for an agent over a date range.")]
    public async Task<string> GetToolUsageStats(
        [Description("Agent ID")] string agentId,
        [Description("ISO date from (default: 30 days ago)")] string? from = null,
        [Description("ISO date to (default: now)")] string? to = null)
    {
        var ctx = McpServerContext.FromHttpContext(http);
        if (!ctx.IsAuthenticated) return Error("Unauthenticated");

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var traceDb = scope.ServiceProvider.GetRequiredService<SessionTraceDbContext>();
            var fromDate = from is not null ? DateTime.Parse(from) : DateTime.UtcNow.AddDays(-30);
            var toDate   = to   is not null ? DateTime.Parse(to)   : DateTime.UtcNow;

            var sessionIds = await traceDb.TraceSessions
                .Where(s => s.AgentId == agentId && s.TenantId == ctx.TenantId
                         && s.CreatedAt >= fromDate && s.CreatedAt <= toDate)
                .Select(s => s.SessionId)
                .ToListAsync();

            var toolCalls = await traceDb.TraceToolCalls
                .Where(tc => sessionIds.Contains(tc.SessionId))
                .ToListAsync();

            var stats = toolCalls
                .GroupBy(tc => tc.ToolName)
                .Select(g => new
                {
                    toolName   = g.Key,
                    callCount  = g.Count(),
                    errorCount = g.Count(tc => tc.ToolOutput.StartsWith("{\"error\":")),
                    errorRate  = g.Count() > 0
                        ? (double)g.Count(tc => tc.ToolOutput.StartsWith("{\"error\":")) / g.Count()
                        : 0.0
                })
                .OrderByDescending(s => s.callCount)
                .ToList();

            return JsonSerializer.Serialize(stats, _json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "get_tool_usage_stats failed for agent {AgentId}", agentId);
            return Error(ex.Message);
        }
    }

    private static string Truncate(string s, int max) =>
        s.Length > max ? s[..max] + "..." : s;
}
