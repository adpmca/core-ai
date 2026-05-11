using Diva.Core.Configuration;
using Diva.Core.Models;
using Diva.Infrastructure.Data;
using Diva.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Diva.Infrastructure.Optimization;

public sealed class SessionAnalyzer : ISessionAnalyzer
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AgentOptions _opts;
    private readonly ILogger<SessionAnalyzer> _logger;

    public SessionAnalyzer(
        IServiceScopeFactory scopeFactory,
        IOptions<AgentOptions> opts,
        ILogger<SessionAnalyzer> logger)
    {
        _scopeFactory = scopeFactory;
        _opts         = opts.Value;
        _logger       = logger;
    }

    public async Task<SessionAnalysisReport> AnalyzeSessionAsync(
        string sessionId, string agentId, int tenantId, CancellationToken ct)
    {
        await using var scope  = _scopeFactory.CreateAsyncScope();
        var db      = scope.ServiceProvider.GetRequiredService<DivaDbContext>();
        var traceDb = scope.ServiceProvider.GetRequiredService<SessionTraceDbContext>();

        var session = await db.Sessions
            .Include(s => s.Messages.OrderBy(m => m.CreatedAt))
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.TenantId == tenantId, ct);
        if (session is null)
        {
            _logger.LogWarning("Session {Session} not found for tenant {Tenant}", sessionId, tenantId);
            return EmptyReport(agentId, sessionId);
        }

        var turns = await traceDb.TraceSessionTurns
            .Where(t => t.SessionId == sessionId)
            .OrderBy(t => t.TurnNumber)
            .ToListAsync(ct);

        var iterations = await traceDb.TraceIterations
            .Where(i => i.SessionId == sessionId)
            .ToListAsync(ct);

        var toolCalls = await traceDb.TraceToolCalls
            .Where(tc => tc.SessionId == sessionId)
            .ToListAsync(ct);

        var transcript = BuildTranscript(session.Messages.ToList(), _opts.Optimization.MaxTranscriptChars);
        var maxIter    = await GetMaxIterationsAsync(db, agentId, tenantId, ct);

        return BuildReport(agentId, sessionId, [session.Id], turns, iterations, toolCalls, maxIter, [transcript]);
    }

    public async Task<SessionAnalysisReport> AnalyzeAggregateAsync(
        string agentId, int tenantId, DateTime from, DateTime to, CancellationToken ct)
    {
        await using var scope  = _scopeFactory.CreateAsyncScope();
        var db      = scope.ServiceProvider.GetRequiredService<DivaDbContext>();
        var traceDb = scope.ServiceProvider.GetRequiredService<SessionTraceDbContext>();

        var sessions = await traceDb.TraceSessions
            .Where(s => s.AgentId == agentId && s.TenantId == tenantId
                     && s.CreatedAt >= from && s.CreatedAt <= to)
            .ToListAsync(ct);

        if (sessions.Count == 0)
            return EmptyReport(agentId, null);

        var sessionIds = sessions.Select(s => s.SessionId).ToList();

        var turns = await traceDb.TraceSessionTurns
            .Where(t => sessionIds.Contains(t.SessionId))
            .ToListAsync(ct);

        var iterations = await traceDb.TraceIterations
            .Where(i => sessionIds.Contains(i.SessionId))
            .ToListAsync(ct);

        var toolCalls = await traceDb.TraceToolCalls
            .Where(tc => sessionIds.Contains(tc.SessionId))
            .ToListAsync(ct);

        var maxIter = await GetMaxIterationsAsync(db, agentId, tenantId, ct);

        // Worst-scoring turns for LLM context
        var scoredTurns = turns.Where(t => t.ScoresAvailable).ToList();
        var samples = scoredTurns.Count > 0
            ? scoredTurns
                .OrderBy(t => (t.CompletenessScore ?? 0f) + (t.CoherenceScore ?? 0f))
                .Take(_opts.Optimization.SampleTurnsForLlm)
                .Select(t =>
                    $"[Turn {t.TurnNumber} — F:{t.FaithfulnessScore:F2} C:{t.CompletenessScore:F2} T:{t.ToolEfficiencyScore:F2} Co:{t.CoherenceScore:F2}]\n" +
                    $"User: {Truncate(t.UserMessage, 300)}\nAssistant: {Truncate(t.AssistantMessage, 500)}")
                .ToList()
            : turns
                .Where(t => t.VerificationPassed == false)
                .Take(_opts.Optimization.SampleTurnsForLlm)
                .Select(t => $"[Turn {t.TurnNumber} — verification failed]\nUser: {Truncate(t.UserMessage, 300)}\nAssistant: {Truncate(t.AssistantMessage, 500)}")
                .ToList();

        return BuildReport(agentId, null, sessionIds, turns, iterations, toolCalls, maxIter, samples) with
        {
            TotalSessions = sessions.Count
        };
    }

    // ── Shared helpers ────────────────────────────────────────────────────────

    private SessionAnalysisReport BuildReport(
        string agentId,
        string? sessionId,
        List<string> sessionIds,
        List<Data.Entities.TraceSessionTurnEntity> turns,
        List<Data.Entities.TraceIterationEntity> iterations,
        List<Data.Entities.TraceToolCallEntity> toolCalls,
        int maxIter,
        List<string> sampleContent)
    {
        var scoredTurns  = turns.Where(t => t.ScoresAvailable).ToList();
        var verifyFailed = turns.Count(t => t.VerificationPassed == false);
        var corrections  = iterations.Count(i => i.IsCorrection);
        var hitMaxIter   = turns.Count(t => t.TotalIterations >= maxIter);

        var toolErrors = toolCalls
            .Where(tc => tc.ToolOutput.StartsWith("{\"error\":", StringComparison.Ordinal))
            .Select(tc => tc.ToolName)
            .GroupBy(n => n)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => $"{g.Key} ({g.Count()}x)")
            .ToList();

        return new SessionAnalysisReport
        {
            AgentId                   = agentId,
            SessionId                 = sessionId,
            TotalSessions             = sessionIds.Count,
            TotalTurns                = turns.Count,
            ScoredTurns               = scoredTurns.Count,
            AvgFaithfulness           = scoredTurns.Count > 0 ? (double?)scoredTurns.Average(t => t.FaithfulnessScore!.Value) : null,
            AvgCompleteness           = scoredTurns.Count > 0 ? (double?)scoredTurns.Average(t => t.CompletenessScore!.Value) : null,
            AvgToolEfficiency         = scoredTurns.Count > 0 ? (double?)scoredTurns.Average(t => t.ToolEfficiencyScore!.Value) : null,
            AvgCoherence              = scoredTurns.Count > 0 ? (double?)scoredTurns.Average(t => t.CoherenceScore!.Value) : null,
            VerificationFailureRate   = turns.Count > 0 ? (double)verifyFailed / turns.Count : 0,
            CorrectionRetryRate       = iterations.Count > 0 ? (double)corrections / iterations.Count : 0,
            MaxIterationsHitRate      = turns.Count > 0 ? (double)hitMaxIter / turns.Count : 0,
            ToolErrorRate             = toolCalls.Count > 0
                ? (double)toolCalls.Count(tc => tc.ToolOutput.StartsWith("{\"error\":")) / toolCalls.Count : 0,
            AverageIterationsPerTurn  = turns.Count > 0 ? turns.Average(t => (double)t.TotalIterations) : 0,
            AverageInputTokensPerTurn = turns.Count > 0 ? turns.Average(t => (double)t.TotalInputTokens) : 0,
            FrequentToolErrors        = toolErrors,
            SampleTurnContent         = sampleContent
        };
    }

    private async Task<int> GetMaxIterationsAsync(DivaDbContext db, string agentId, int tenantId, CancellationToken ct)
    {
        var agentDef = await db.AgentDefinitions
            .FirstOrDefaultAsync(a => a.Id == agentId && a.TenantId == tenantId, ct);
        return agentDef?.MaxIterations ?? _opts.MaxIterations;
    }

    private static SessionAnalysisReport EmptyReport(string agentId, string? sessionId) => new()
    {
        AgentId = agentId, SessionId = sessionId
    };

    private static string BuildTranscript(List<AgentSessionMessageEntity> messages, int maxChars)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var m in messages)
        {
            var line = $"{m.Role}: {m.Content}\n";
            if (sb.Length + line.Length > maxChars) break;
            sb.Append(line);
        }
        return sb.ToString();
    }

    private static string Truncate(string s, int max) =>
        s.Length > max ? s[..max] + "..." : s;
}
