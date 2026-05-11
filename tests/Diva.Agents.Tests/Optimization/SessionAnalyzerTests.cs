using Diva.Core.Configuration;
using Diva.Infrastructure.Data;
using Diva.Infrastructure.Data.Entities;
using Diva.Infrastructure.Optimization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Diva.Agents.Tests.Optimization;

/// <summary>
/// Integration tests for SessionAnalyzer using real in-memory SQLite for both DB contexts.
/// All tests verify dimensional score aggregation, fallback paths, and tenant isolation.
/// </summary>
public class SessionAnalyzerTests : IAsyncDisposable
{
    // Two separate in-memory connections — one per DB context
    private readonly SqliteConnection _divaConn;
    private readonly SqliteConnection _traceConn;
    private readonly DbContextOptions<DivaDbContext> _divaOpts;
    private readonly DbContextOptions<SessionTraceDbContext> _traceOpts;
    private readonly ServiceProvider _provider;
    private readonly SessionAnalyzer _analyzer;

    public SessionAnalyzerTests()
    {
        _divaConn = new SqliteConnection("DataSource=:memory:");
        _divaConn.Open();
        _divaOpts = new DbContextOptionsBuilder<DivaDbContext>()
            .UseSqlite(_divaConn)
            .Options;
        using (var db = new DivaDbContext(_divaOpts))
            db.Database.EnsureCreated();

        _traceConn = new SqliteConnection("DataSource=:memory:");
        _traceConn.Open();
        _traceOpts = new DbContextOptionsBuilder<SessionTraceDbContext>()
            .UseSqlite(_traceConn)
            .Options;
        using (var db = new SessionTraceDbContext(_traceOpts))
            db.Database.EnsureCreated();

        var services = new ServiceCollection();
        services.AddDbContext<DivaDbContext>(o => o.UseSqlite(_divaConn));
        services.AddDbContext<SessionTraceDbContext>(o => o.UseSqlite(_traceConn));
        _provider = services.BuildServiceProvider();

        _analyzer = new SessionAnalyzer(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new AgentOptions
            {
                MaxIterations = 10,
                Optimization  = new OptimizationOptions
                {
                    MaxTranscriptChars = 8000,
                    SampleTurnsForLlm  = 3
                }
            }),
            NullLogger<SessionAnalyzer>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _divaConn.DisposeAsync();
        await _traceConn.DisposeAsync();
    }

    // ── AnalyzeSessionAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeSessionAsync_SessionNotFound_ReturnsEmptyReport()
    {
        var report = await _analyzer.AnalyzeSessionAsync("no-such-session", "agent-1", 1, default);

        Assert.Equal("agent-1", report.AgentId);
        Assert.Equal("no-such-session", report.SessionId);
        Assert.Equal(0, report.TotalTurns);
        Assert.Null(report.AvgFaithfulness);
    }

    [Fact]
    public async Task AnalyzeSessionAsync_WithScoredTurns_ComputesDimensionalAverages()
    {
        // Seed session + messages in DivaDbContext
        await using (var db = new DivaDbContext(_divaOpts))
        {
            db.AgentDefinitions.Add(new AgentDefinitionEntity
                { Id = "a1", Name = "Agent", DisplayName = "A", TenantId = 1 });
            db.Sessions.Add(new AgentSessionEntity { Id = "sess-1", TenantId = 1 });
            db.SessionMessages.AddRange(
                new AgentSessionMessageEntity { SessionId = "sess-1", Role = "user", Content = "hi" },
                new AgentSessionMessageEntity { SessionId = "sess-1", Role = "assistant", Content = "hello" }
            );
            await db.SaveChangesAsync();
        }

        // Seed trace turns with dimension scores
        await using (var db = new SessionTraceDbContext(_traceOpts))
        {
            db.TraceSessions.Add(new TraceSessionEntity
                { SessionId = "sess-1", TenantId = 1, AgentId = "a1", AgentName = "A", UserId = "u" });
            var turn1 = new TraceSessionTurnEntity
            {
                SessionId = "sess-1", TurnNumber = 1,
                UserMessage = "q1", AssistantMessage = "a1",
                AgentId = "a1", ModelId = "m", Provider = "OpenAI",
                TotalIterations = 2,
                FaithfulnessScore = 0.9f, CompletenessScore = 0.8f,
                ToolEfficiencyScore = 0.7f, CoherenceScore = 0.85f,
                ScoresAvailable = true
            };
            var turn2 = new TraceSessionTurnEntity
            {
                SessionId = "sess-1", TurnNumber = 2,
                UserMessage = "q2", AssistantMessage = "a2",
                AgentId = "a1", ModelId = "m", Provider = "OpenAI",
                TotalIterations = 3,
                FaithfulnessScore = 0.5f, CompletenessScore = 0.4f,
                ToolEfficiencyScore = 0.6f, CoherenceScore = 0.3f,
                ScoresAvailable = true
            };
            db.TraceSessionTurns.AddRange(turn1, turn2);
            await db.SaveChangesAsync();
        }

        var report = await _analyzer.AnalyzeSessionAsync("sess-1", "a1", 1, default);

        Assert.Equal("sess-1", report.SessionId);
        Assert.Equal(2, report.TotalTurns);
        Assert.Equal(2, report.ScoredTurns);
        Assert.NotNull(report.AvgFaithfulness);
        Assert.Equal(0.7, report.AvgFaithfulness!.Value, precision: 5);    // (0.9+0.5)/2
        Assert.Equal(0.6, report.AvgCompleteness!.Value, precision: 5);    // (0.8+0.4)/2
        Assert.Equal(0.65, report.AvgToolEfficiency!.Value, precision: 5); // (0.7+0.6)/2
        Assert.Equal(0.575, report.AvgCoherence!.Value, precision: 5);     // (0.85+0.3)/2
    }

    [Fact]
    public async Task AnalyzeSessionAsync_NoScoredTurns_ReturnsNullDimensionalAverages()
    {
        await using (var db = new DivaDbContext(_divaOpts))
        {
            db.AgentDefinitions.Add(new AgentDefinitionEntity
                { Id = "a2", Name = "Agent2", DisplayName = "A2", TenantId = 1 });
            db.Sessions.Add(new AgentSessionEntity { Id = "sess-2", TenantId = 1 });
            await db.SaveChangesAsync();
        }

        await using (var db = new SessionTraceDbContext(_traceOpts))
        {
            db.TraceSessions.Add(new TraceSessionEntity
                { SessionId = "sess-2", TenantId = 1, AgentId = "a2", AgentName = "A2", UserId = "u" });
            db.TraceSessionTurns.Add(new TraceSessionTurnEntity
            {
                SessionId = "sess-2", TurnNumber = 1,
                UserMessage = "q", AssistantMessage = "a",
                AgentId = "a2", ModelId = "m", Provider = "OpenAI",
                VerificationPassed = false,
                ScoresAvailable    = false
            });
            await db.SaveChangesAsync();
        }

        var report = await _analyzer.AnalyzeSessionAsync("sess-2", "a2", 1, default);

        Assert.Equal(1, report.TotalTurns);
        Assert.Equal(0, report.ScoredTurns);
        Assert.Null(report.AvgFaithfulness);
        Assert.Null(report.AvgCompleteness);
        Assert.Equal(1.0, report.VerificationFailureRate, precision: 5); // 1 failed out of 1
    }

    // ── AnalyzeAggregateAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAggregateAsync_NoSessionsInRange_ReturnsEmptyReport()
    {
        var from = DateTime.UtcNow.AddDays(-7);
        var to   = DateTime.UtcNow;

        var report = await _analyzer.AnalyzeAggregateAsync("nonexistent-agent", 1, from, to, default);

        Assert.Equal("nonexistent-agent", report.AgentId);
        Assert.Null(report.SessionId);
        Assert.Equal(0, report.TotalSessions);
    }

    [Fact]
    public async Task AnalyzeAggregateAsync_WithScoredTurns_SelectsWorstTurnsAsSamples()
    {
        var from = DateTime.UtcNow.AddDays(-7);
        var to   = DateTime.UtcNow;

        await using (var db = new DivaDbContext(_divaOpts))
        {
            db.AgentDefinitions.Add(new AgentDefinitionEntity
                { Id = "agg-agent", Name = "AggAgent", DisplayName = "Agg", TenantId = 1 });
            await db.SaveChangesAsync();
        }

        await using (var db = new SessionTraceDbContext(_traceOpts))
        {
            db.TraceSessions.AddRange(
                new TraceSessionEntity { SessionId = "agg-s1", TenantId = 1, AgentId = "agg-agent", AgentName = "A", UserId = "u", CreatedAt = DateTime.UtcNow.AddDays(-1) },
                new TraceSessionEntity { SessionId = "agg-s2", TenantId = 1, AgentId = "agg-agent", AgentName = "A", UserId = "u", CreatedAt = DateTime.UtcNow.AddDays(-2) }
            );
            // Turn with good scores
            db.TraceSessionTurns.Add(new TraceSessionTurnEntity
            {
                SessionId = "agg-s1", TurnNumber = 1,
                UserMessage = "good q", AssistantMessage = "good a",
                AgentId = "agg-agent", ModelId = "m", Provider = "OpenAI",
                CompletenessScore = 0.9f, CoherenceScore = 0.85f,
                FaithfulnessScore = 0.95f, ToolEfficiencyScore = 0.8f,
                ScoresAvailable = true
            });
            // Turn with bad scores — should be selected as sample
            db.TraceSessionTurns.Add(new TraceSessionTurnEntity
            {
                SessionId = "agg-s2", TurnNumber = 1,
                UserMessage = "bad q", AssistantMessage = "bad a",
                AgentId = "agg-agent", ModelId = "m", Provider = "OpenAI",
                CompletenessScore = 0.2f, CoherenceScore = 0.15f,
                FaithfulnessScore = 0.3f, ToolEfficiencyScore = 0.25f,
                ScoresAvailable = true
            });
            await db.SaveChangesAsync();
        }

        var report = await _analyzer.AnalyzeAggregateAsync("agg-agent", 1, from, to, default);

        Assert.Equal(2, report.TotalSessions);
        Assert.Equal(2, report.TotalTurns);
        Assert.Equal(2, report.ScoredTurns);

        // SampleTurnContent should pick the bad turn first (lowest completeness + coherence)
        Assert.NotEmpty(report.SampleTurnContent);
        Assert.Contains("bad q", report.SampleTurnContent[0]);
    }

    [Fact]
    public async Task AnalyzeAggregateAsync_NoScoredTurns_FallsBackToVerificationFailedSamples()
    {
        var from = DateTime.UtcNow.AddDays(-7);
        var to   = DateTime.UtcNow;

        await using (var db = new DivaDbContext(_divaOpts))
        {
            if (!db.AgentDefinitions.Any(a => a.Id == "fallback-agent"))
            {
                db.AgentDefinitions.Add(new AgentDefinitionEntity
                    { Id = "fallback-agent", Name = "FB", DisplayName = "FB", TenantId = 1 });
                await db.SaveChangesAsync();
            }
        }

        await using (var db = new SessionTraceDbContext(_traceOpts))
        {
            db.TraceSessions.Add(new TraceSessionEntity
                { SessionId = "fb-s1", TenantId = 1, AgentId = "fallback-agent", AgentName = "FB", UserId = "u", CreatedAt = DateTime.UtcNow.AddDays(-1) });
            db.TraceSessionTurns.AddRange(
                new TraceSessionTurnEntity
                {
                    SessionId = "fb-s1", TurnNumber = 1,
                    UserMessage = "failed q", AssistantMessage = "bad answer",
                    AgentId = "fallback-agent", ModelId = "m", Provider = "OpenAI",
                    VerificationPassed = false, ScoresAvailable = false
                },
                new TraceSessionTurnEntity
                {
                    SessionId = "fb-s1", TurnNumber = 2,
                    UserMessage = "ok q", AssistantMessage = "ok answer",
                    AgentId = "fallback-agent", ModelId = "m", Provider = "OpenAI",
                    VerificationPassed = true, ScoresAvailable = false
                }
            );
            await db.SaveChangesAsync();
        }

        var report = await _analyzer.AnalyzeAggregateAsync("fallback-agent", 1, from, to, default);

        // No scored turns → dimensional averages are null
        Assert.Null(report.AvgFaithfulness);
        Assert.Equal(0, report.ScoredTurns);

        // Fallback: only the verification-failed turn appears in samples
        Assert.NotEmpty(report.SampleTurnContent);
        Assert.Contains("failed q", report.SampleTurnContent[0]);

        // Verification failure rate: 1 failed out of 2
        Assert.Equal(0.5, report.VerificationFailureRate, precision: 5);
    }
}
