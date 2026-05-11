using System.Text.Json;
using Diva.Core.Configuration;
using Diva.Core.Models;
using Diva.Infrastructure.Data;
using Diva.Infrastructure.Data.Entities;
using Diva.Tools.Optimization;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Diva.Tools.Tests.Optimization;

/// <summary>
/// Tests for AgentOptimizationMcpTools:
/// - Cross-tenant isolation (session belonging to tenant 2 is invisible to tenant 1 caller)
/// - failedOnly filter returns only verification-failed turns
/// - Unauthenticated calls return error JSON
/// - Agent config lookup
/// </summary>
public class AgentOptimizationMcpToolsTests : IAsyncDisposable
{
    private readonly SqliteConnection _divaConn;
    private readonly SqliteConnection _traceConn;
    private readonly DbContextOptions<DivaDbContext> _divaOpts;
    private readonly DbContextOptions<SessionTraceDbContext> _traceOpts;
    private readonly ServiceProvider _provider;

    public AgentOptimizationMcpToolsTests()
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
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _divaConn.DisposeAsync();
        await _traceConn.DisposeAsync();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private AgentOptimizationMcpTools BuildTools(int tenantId, bool authenticated = true)
    {
        var httpContext = new DefaultHttpContext();
        if (authenticated)
        {
            httpContext.Items["TenantContext"] = new TenantContext
            {
                TenantId   = tenantId,
                TenantName = $"Tenant{tenantId}",
                UserId     = "user-1"
            };
        }

        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);

        return new AgentOptimizationMcpTools(
            accessor,
            _provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new AgentOptions
            {
                Optimization = new OptimizationOptions { MaxTranscriptChars = 8000 }
            }),
            NullLogger<AgentOptimizationMcpTools>.Instance);
    }

    private static JsonElement ParseJson(string raw) =>
        JsonDocument.Parse(raw).RootElement;

    // ── Authentication guard ──────────────────────────────────────────────────

    [Fact]
    public async Task GetAgentConfig_Unauthenticated_ReturnsErrorJson()
    {
        var tools  = BuildTools(0, authenticated: false);
        var result = await tools.GetAgentConfig("any-agent");
        var json   = ParseJson(result);

        Assert.Equal("Error", json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetAgentMetrics_Unauthenticated_ReturnsErrorJson()
    {
        var tools  = BuildTools(0, authenticated: false);
        var result = await tools.GetAgentMetrics("any-agent");
        var json   = ParseJson(result);

        Assert.Equal("Error", json.GetProperty("error").GetString());
    }

    // ── Cross-tenant isolation ────────────────────────────────────────────────

    [Fact]
    public async Task GetSessionConversation_SessionBelongsToOtherTenant_ReturnsNotFound()
    {
        // Seed a session for tenant 2
        await using (var db = new DivaDbContext(_divaOpts))
        {
            db.Sessions.Add(new AgentSessionEntity
                { Id = "cross-tenant-sess", TenantId = 2 });
            await db.SaveChangesAsync();
        }

        // Caller is authenticated as tenant 1
        var tools  = BuildTools(1);
        var result = await tools.GetSessionConversation("cross-tenant-sess");
        var json   = ParseJson(result);

        Assert.Equal("NotFound", json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetSessionTurnsSample_SessionBelongsToOtherTenant_ReturnsNotFound()
    {
        // Seed a trace session for tenant 2
        await using (var db = new SessionTraceDbContext(_traceOpts))
        {
            db.TraceSessions.Add(new TraceSessionEntity
            {
                SessionId = "trace-cross", TenantId = 2, AgentId = "a1",
                AgentName = "A", UserId = "u"
            });
            await db.SaveChangesAsync();
        }

        // Caller is authenticated as tenant 1
        var tools  = BuildTools(1);
        var result = await tools.GetSessionTurnsSample("trace-cross");
        var json   = ParseJson(result);

        Assert.Equal("NotFound", json.GetProperty("error").GetString());
    }

    // ── failedOnly filter ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetSessionTurnsSample_FailedOnlyTrue_ReturnsOnlyFailedTurns()
    {
        await using (var db = new SessionTraceDbContext(_traceOpts))
        {
            db.TraceSessions.Add(new TraceSessionEntity
            {
                SessionId = "filter-sess", TenantId = 1, AgentId = "fa", AgentName = "FA", UserId = "u"
            });
            db.TraceSessionTurns.AddRange(
                new TraceSessionTurnEntity
                {
                    SessionId = "filter-sess", TurnNumber = 1,
                    UserMessage = "ok q", AssistantMessage = "ok a",
                    AgentId = "fa", ModelId = "m", Provider = "OpenAI",
                    VerificationPassed = true
                },
                new TraceSessionTurnEntity
                {
                    SessionId = "filter-sess", TurnNumber = 2,
                    UserMessage = "bad q", AssistantMessage = "bad a",
                    AgentId = "fa", ModelId = "m", Provider = "OpenAI",
                    VerificationPassed = false
                }
            );
            await db.SaveChangesAsync();
        }

        var tools  = BuildTools(1);
        var result = await tools.GetSessionTurnsSample("filter-sess", failedOnly: true);
        var turns  = JsonDocument.Parse(result).RootElement.EnumerateArray().ToList();

        Assert.Single(turns);
        Assert.Equal(2, turns[0].GetProperty("turnNumber").GetInt32());
    }

    [Fact]
    public async Task GetSessionTurnsSample_FailedOnlyFalse_ReturnsAllTurns()
    {
        await using (var db = new SessionTraceDbContext(_traceOpts))
        {
            // Only add if the session doesn't exist yet (test isolation)
            if (!db.TraceSessions.Any(s => s.SessionId == "all-turns-sess"))
            {
                db.TraceSessions.Add(new TraceSessionEntity
                {
                    SessionId = "all-turns-sess", TenantId = 1, AgentId = "ta", AgentName = "TA", UserId = "u"
                });
                db.TraceSessionTurns.AddRange(
                    new TraceSessionTurnEntity
                    {
                        SessionId = "all-turns-sess", TurnNumber = 1,
                        UserMessage = "q1", AssistantMessage = "a1",
                        AgentId = "ta", ModelId = "m", Provider = "OpenAI",
                        VerificationPassed = true
                    },
                    new TraceSessionTurnEntity
                    {
                        SessionId = "all-turns-sess", TurnNumber = 2,
                        UserMessage = "q2", AssistantMessage = "a2",
                        AgentId = "ta", ModelId = "m", Provider = "OpenAI",
                        VerificationPassed = false
                    }
                );
                await db.SaveChangesAsync();
            }
        }

        var tools  = BuildTools(1);
        var result = await tools.GetSessionTurnsSample("all-turns-sess", failedOnly: false);
        var turns  = JsonDocument.Parse(result).RootElement.EnumerateArray().ToList();

        Assert.Equal(2, turns.Count);
    }

    // ── Agent config ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAgentConfig_AgentBelongsToTenant_ReturnsConfigJson()
    {
        await using (var db = new DivaDbContext(_divaOpts))
        {
            db.AgentDefinitions.Add(new AgentDefinitionEntity
            {
                Id = "config-agent", TenantId = 1, Name = "ConfigAgent", DisplayName = "CA",
                Temperature = 0.8, MaxIterations = 7, VerificationMode = "ToolGrounded"
            });
            await db.SaveChangesAsync();
        }

        var tools  = BuildTools(1);
        var result = await tools.GetAgentConfig("config-agent");
        var json   = ParseJson(result);

        Assert.Equal("config-agent", json.GetProperty("id").GetString());
        Assert.Equal(0.8, json.GetProperty("temperature").GetDouble(), precision: 5);
        Assert.Equal(7, json.GetProperty("maxIterations").GetInt32());
        Assert.Equal("ToolGrounded", json.GetProperty("verificationMode").GetString());
    }

    [Fact]
    public async Task GetAgentConfig_AgentBelongsToOtherTenant_ReturnsNotFound()
    {
        await using (var db = new DivaDbContext(_divaOpts))
        {
            db.AgentDefinitions.Add(new AgentDefinitionEntity
            {
                Id = "other-agent", TenantId = 99, Name = "Other", DisplayName = "O"
            });
            await db.SaveChangesAsync();
        }

        var tools  = BuildTools(1);   // caller is tenant 1
        var result = await tools.GetAgentConfig("other-agent");
        var json   = ParseJson(result);

        Assert.Equal("NotFound", json.GetProperty("error").GetString());
    }

    // ── Agent metrics ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAgentMetrics_WithScoredTurns_ReturnsAverageScores()
    {
        await using (var db = new SessionTraceDbContext(_traceOpts))
        {
            db.TraceSessions.Add(new TraceSessionEntity
            {
                SessionId = "metrics-sess", TenantId = 1, AgentId = "metrics-agent",
                AgentName = "MA", UserId = "u",
                CreatedAt = DateTime.UtcNow.AddDays(-1)
            });
            db.TraceSessionTurns.Add(new TraceSessionTurnEntity
            {
                SessionId = "metrics-sess", TurnNumber = 1,
                UserMessage = "q", AssistantMessage = "a",
                AgentId = "metrics-agent", ModelId = "m", Provider = "OpenAI",
                FaithfulnessScore = 0.9f, CompletenessScore = 0.8f,
                ToolEfficiencyScore = 0.7f, CoherenceScore = 0.6f,
                ScoresAvailable = true
            });
            await db.SaveChangesAsync();
        }

        var tools  = BuildTools(1);
        var result = await tools.GetAgentMetrics("metrics-agent");
        var json   = ParseJson(result);

        Assert.Equal(1, json.GetProperty("totalSessions").GetInt32());
        Assert.Equal(1, json.GetProperty("scoredTurns").GetInt32());
        Assert.Equal(0.9, json.GetProperty("avgFaithfulness").GetDouble(), precision: 5);
        Assert.Equal(0.8, json.GetProperty("avgCompleteness").GetDouble(), precision: 5);
    }

    [Fact]
    public async Task GetAgentMetrics_NoSessions_ReturnsZeroCounts()
    {
        var tools  = BuildTools(1);
        var result = await tools.GetAgentMetrics("ghost-agent");
        var json   = ParseJson(result);

        Assert.Equal(0, json.GetProperty("totalSessions").GetInt32());
        Assert.Equal(0, json.GetProperty("totalTurns").GetInt32());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("avgFaithfulness").ValueKind);
    }
}
