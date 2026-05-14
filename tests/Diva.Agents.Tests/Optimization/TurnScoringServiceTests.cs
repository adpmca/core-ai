using Diva.Core.Configuration;
using Diva.Infrastructure.Data;
using Diva.Infrastructure.Data.Entities;
using Diva.Infrastructure.LiteLLM;
using Diva.Infrastructure.Optimization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Diva.Agents.Tests.Optimization;

/// <summary>
/// Tests for TurnScoringService.
/// The LLM call paths cannot be unit-tested without a real API (no injectable abstraction).
/// Testable: EnablePerTurnScoring=false guard (no DB write, no exception).
/// </summary>
public class TurnScoringServiceTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<SessionTraceDbContext> _traceOpts;

    public TurnScoringServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _traceOpts = new DbContextOptionsBuilder<SessionTraceDbContext>()
            .UseSqlite(_connection)
            .Options;
        using var db = new SessionTraceDbContext(_traceOpts);
        db.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    private TurnScoringService BuildService(bool enableScoring)
    {
        var services = new ServiceCollection();
        services.AddDbContext<SessionTraceDbContext>(o => o.UseSqlite(_connection));

        var provider = services.BuildServiceProvider();

        var llmOpts = Options.Create(new LlmOptions
        {
            DirectProvider = new DirectProviderOptions
            {
                Provider = "OpenAI",
                ApiKey   = "test-key",
                Model    = "gpt-4o",
                Endpoint = "http://localhost:1/"   // invalid — ensures no real call
            }
        });
        var agentOpts = Options.Create(new AgentOptions
        {
            Optimization = new OptimizationOptions
            {
                EnablePerTurnScoring = enableScoring,
                ScorerMaxTokens      = 64
            }
        });

        return new TurnScoringService(
            llmOpts,
            agentOpts,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<ILlmConfigResolver>(),
            NullLogger<TurnScoringService>.Instance);
    }

    [Fact]
    public async Task ScoreTurnAsync_WhenScoringDisabled_DoesNotWriteToDatabase()
    {
        // Seed a turn record
        await using (var db = new SessionTraceDbContext(_traceOpts))
        {
            db.TraceSessions.Add(new TraceSessionEntity
            {
                SessionId = "sess-1", TenantId = 1, AgentId = "a1", AgentName = "A", UserId = "u1"
            });
            db.TraceSessionTurns.Add(new TraceSessionTurnEntity
            {
                SessionId = "sess-1", TurnNumber = 1,
                UserMessage = "Hello", AssistantMessage = "Hi",
                AgentId = "a1", ModelId = "m1", Provider = "OpenAI"
            });
            await db.SaveChangesAsync();
        }

        var svc = BuildService(enableScoring: false);

        // Should return immediately without touching the DB
        await svc.ScoreTurnAsync("sess-1", 1, "a1", "Hello", "Hi", "", default);

        await using var verifyDb = new SessionTraceDbContext(_traceOpts);
        var turn = await verifyDb.TraceSessionTurns
            .FirstAsync(t => t.SessionId == "sess-1" && t.TurnNumber == 1);

        Assert.False(turn.ScoresAvailable);
        Assert.Null(turn.FaithfulnessScore);
        Assert.Null(turn.CompletenessScore);
    }

    [Fact]
    public async Task ScoreTurnAsync_WhenScoringDisabled_DoesNotThrow()
    {
        var svc = BuildService(enableScoring: false);

        // Must not throw under any circumstances (fire-and-forget contract)
        var ex = await Record.ExceptionAsync(
            () => svc.ScoreTurnAsync("no-such-session", 99, "agent", "msg", "resp", "", default));

        Assert.Null(ex);
    }

    [Fact]
    public async Task ScoreTurnAsync_WhenLlmEndpointUnreachable_CatchesExceptionAndDoesNotThrow()
    {
        // Even with scoring enabled and an unreachable endpoint,
        // the service must swallow the error (best-effort scoring).
        var svc = BuildService(enableScoring: true);

        var ex = await Record.ExceptionAsync(
            () => svc.ScoreTurnAsync("sess-x", 1, "agent", "query", "answer", "", default));

        Assert.Null(ex);
    }
}
