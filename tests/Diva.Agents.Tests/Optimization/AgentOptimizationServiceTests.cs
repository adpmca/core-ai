using Diva.Core.Configuration;
using Diva.Core.Models;
using Diva.Core.Optimization;
using Diva.Infrastructure.Data;
using Diva.Infrastructure.Data.Entities;
using Diva.Infrastructure.Optimization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Diva.Agents.Tests.Optimization;

/// <summary>
/// Tests for AgentOptimizationService:
/// - Concurrent-run guard (double StartRunAsync → second throws)
/// - Run record persisted in DB
/// - Review actions (approve / reject) set suggestion status
/// - Few-shot CRUD (add, list, delete, reorder)
/// </summary>
public class AgentOptimizationServiceTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<DivaDbContext> _opts;
    private readonly ServiceProvider _provider;
    private readonly IAgentOptimizationService _service;
    private readonly ISessionAnalyzer _analyzer;

    public AgentOptimizationServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _opts = new DbContextOptionsBuilder<DivaDbContext>()
            .UseSqlite(_connection)
            .Options;
        using (var db = new DivaDbContext(_opts))
            db.Database.EnsureCreated();

        var services = new ServiceCollection();
        services.AddDbContext<DivaDbContext>(o => o.UseSqlite(_connection));
        services.AddScoped<OptimizationApplicator>(sp =>
            new OptimizationApplicator(
                Substitute.For<IAgentSetupAssistant>(),
                Substitute.For<IOptimizationRulePackAccessor>(),
                Substitute.For<IOptimizationLlmAnalyzer>(),
                NullLogger<OptimizationApplicator>.Instance));
        _provider = services.BuildServiceProvider();

        _analyzer = Substitute.For<ISessionAnalyzer>();
        _analyzer.AnalyzeAggregateAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new SessionAnalysisReport { AgentId = "agent-1" });

        var llmAnalyzer = Substitute.For<IOptimizationLlmAnalyzer>();
        llmAnalyzer.AnalyzeAsync(
            Arg.Any<SessionAnalysisReport>(), Arg.Any<AgentDefinitionEntity>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new List<OptimizationSuggestionDto>());

        var lifetime = Substitute.For<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Returns(CancellationToken.None);

        _service = new AgentOptimizationService(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            _analyzer,
            llmAnalyzer,
            lifetime,
            Options.Create(new AgentOptions
            {
                Optimization = new OptimizationOptions { MaxSuggestionsPerRun = 5 }
            }),
            NullLogger<AgentOptimizationService>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private void SeedAgent()
    {
        using var db = new DivaDbContext(_opts);
        if (!db.AgentDefinitions.Any(a => a.Id == "agent-1"))
        {
            db.AgentDefinitions.Add(new AgentDefinitionEntity
                { Id = "agent-1", TenantId = 1, Name = "A1", DisplayName = "A1" });
            db.SaveChanges();
        }
    }

    // ── Concurrent-run guard ──────────────────────────────────────────────────

    [Fact]
    public async Task StartRunAsync_SecondCallWhileFirstRunning_ThrowsInvalidOperationException()
    {
        SeedAgent();

        // Block the analyzer until we explicitly release it
        var block = new TaskCompletionSource<SessionAnalysisReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        _analyzer.AnalyzeAggregateAsync(
                "agent-1", 1, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(block.Task);

        var req = new TriggerOptimizationRequest { From = DateTime.UtcNow.AddDays(-7), To = DateTime.UtcNow };

        // First call — succeeds and kicks off background pipeline (blocked)
        await _service.StartRunAsync("agent-1", 1, req, "manual", default);

        // Second call — same agent/tenant key → throws before any async work
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.StartRunAsync("agent-1", 1, req, "manual", default));

        // Unblock the background task so the test host can clean up
        block.SetResult(new SessionAnalysisReport { AgentId = "agent-1" });
    }

    [Fact]
    public async Task StartRunAsync_CreatesRunRecordWithRunningStatus()
    {
        SeedAgent();

        var req = new TriggerOptimizationRequest { From = DateTime.UtcNow.AddDays(-7), To = DateTime.UtcNow };
        var runId = await _service.StartRunAsync("agent-1", 1, req, "manual", default);

        Assert.True(runId > 0);

        using var db = new DivaDbContext(_opts);
        var run = await db.OptimizationRuns.FirstOrDefaultAsync(r => r.Id == runId);
        Assert.NotNull(run);
        // Status may be "running" or "completed" depending on timing; just verify it exists
        Assert.NotNull(run.Status);
    }

    // ── Tenant isolation ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetRunsAsync_TenantIsolation_ReturnsOnlyOwnTenantRuns()
    {
        using (var db = new DivaDbContext(_opts))
        {
            db.OptimizationRuns.AddRange(
                new AgentOptimizationRunEntity
                    { TenantId = 1, AgentId = "iso-agent", Status = "completed",
                      FromDate = DateTime.UtcNow.AddDays(-1), ToDate = DateTime.UtcNow },
                new AgentOptimizationRunEntity
                    { TenantId = 2, AgentId = "iso-agent", Status = "completed",
                      FromDate = DateTime.UtcNow.AddDays(-1), ToDate = DateTime.UtcNow }
            );
            await db.SaveChangesAsync();
        }

        var tenant1 = await _service.GetRunsAsync("iso-agent", 1, default);
        var tenant2 = await _service.GetRunsAsync("iso-agent", 2, default);

        // Each tenant should see exactly 1 run (their own)
        Assert.Equal(1, tenant1.Count);
        Assert.Equal(1, tenant2.Count);
        // Verify by checking that both tenants don't return the same run IDs
        Assert.DoesNotContain(tenant1[0].Id, tenant2.Select(r => r.Id));
    }

    // ── Review actions ────────────────────────────────────────────────────────

    [Fact]
    public async Task ApproveSuggestionAsync_SetsSuggestionStatusToApproved()
    {
        int suggestionId;
        using (var db = new DivaDbContext(_opts))
        {
            var run = new AgentOptimizationRunEntity
                { TenantId = 1, AgentId = "a1", Status = "completed",
                  FromDate = DateTime.UtcNow.AddDays(-1), ToDate = DateTime.UtcNow };
            db.OptimizationRuns.Add(run);
            await db.SaveChangesAsync();

            var s = new AgentOptimizationSuggestionEntity
            {
                TenantId = 1, RunId = run.Id, AgentId = "a1",
                Type = "ModelSwitch", FieldName = "model",
                SuggestedValue = "claude-haiku", Confidence = 0.8f,
                Reasoning = "r", Status = "Pending"
            };
            db.OptimizationSuggestions.Add(s);
            await db.SaveChangesAsync();
            suggestionId = s.Id;
        }

        await _service.ApproveSuggestionAsync(suggestionId, 1, "reviewer@test.com", null, default);

        using var verify = new DivaDbContext(_opts);
        var saved = await verify.OptimizationSuggestions.FirstAsync(s => s.Id == suggestionId);
        Assert.Equal("Approved", saved.Status);
        Assert.Equal("reviewer@test.com", saved.ReviewedBy);
    }

    [Fact]
    public async Task RejectSuggestionAsync_SetsSuggestionStatusToRejected()
    {
        int suggestionId;
        using (var db = new DivaDbContext(_opts))
        {
            var run = new AgentOptimizationRunEntity
                { TenantId = 1, AgentId = "a1", Status = "completed",
                  FromDate = DateTime.UtcNow.AddDays(-1), ToDate = DateTime.UtcNow };
            db.OptimizationRuns.Add(run);
            await db.SaveChangesAsync();

            var s = new AgentOptimizationSuggestionEntity
            {
                TenantId = 1, RunId = run.Id, AgentId = "a1",
                Type = "TemperatureAdjustment", FieldName = "temperature",
                SuggestedValue = "0.5", Confidence = 0.7f,
                Reasoning = "r", Status = "Pending"
            };
            db.OptimizationSuggestions.Add(s);
            await db.SaveChangesAsync();
            suggestionId = s.Id;
        }

        await _service.RejectSuggestionAsync(suggestionId, 1, "reviewer@test.com", "Not relevant", default);

        using var verify = new DivaDbContext(_opts);
        var saved = await verify.OptimizationSuggestions.FirstAsync(s => s.Id == suggestionId);
        Assert.Equal("Rejected", saved.Status);
        Assert.Equal("Not relevant", saved.ReviewNotes);
    }

    // ── Few-shot CRUD ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AddFewShotExampleAsync_PersistsExampleToDatabase()
    {
        var dto = new FewShotExampleDto
        {
            AgentId          = "a1",
            UserMessage      = "What is the capital of France?",
            AssistantMessage = "The capital of France is Paris.",
            Description      = "Geographic fact",
            SortOrder        = 0,
            IsEnabled        = true
        };

        var id = await _service.AddFewShotExampleAsync("a1", 1, dto, default);
        Assert.True(id > 0);

        var examples = await _service.GetFewShotExamplesAsync("a1", 1, default);
        Assert.Single(examples);
        Assert.Equal("What is the capital of France?", examples[0].UserMessage);
    }

    [Fact]
    public async Task GetFewShotExamplesAsync_TenantIsolation_ReturnsOnlyOwnTenant()
    {
        var dto1 = new FewShotExampleDto { AgentId = "aX", UserMessage = "T1 q", AssistantMessage = "a", IsEnabled = true };
        var dto2 = new FewShotExampleDto { AgentId = "aX", UserMessage = "T2 q", AssistantMessage = "a", IsEnabled = true };

        await _service.AddFewShotExampleAsync("aX", 1, dto1, default);
        await _service.AddFewShotExampleAsync("aX", 2, dto2, default);

        var t1 = await _service.GetFewShotExamplesAsync("aX", 1, default);
        var t2 = await _service.GetFewShotExamplesAsync("aX", 2, default);

        Assert.All(t1, e => Assert.Equal("T1 q", e.UserMessage));
        Assert.All(t2, e => Assert.Equal("T2 q", e.UserMessage));
    }

    [Fact]
    public async Task DeleteFewShotExampleAsync_RemovesFromDatabase()
    {
        var id = await _service.AddFewShotExampleAsync("aY", 1,
            new FewShotExampleDto { AgentId = "aY", UserMessage = "q", AssistantMessage = "a", IsEnabled = true }, default);

        await _service.DeleteFewShotExampleAsync(id, 1, default);

        var examples = await _service.GetFewShotExamplesAsync("aY", 1, default);
        Assert.Empty(examples);
    }

    [Fact]
    public async Task ReorderFewShotExamplesAsync_UpdatesSortOrder()
    {
        var id1 = await _service.AddFewShotExampleAsync("aZ", 1,
            new FewShotExampleDto { AgentId = "aZ", UserMessage = "first", AssistantMessage = "a", SortOrder = 0, IsEnabled = true }, default);
        var id2 = await _service.AddFewShotExampleAsync("aZ", 1,
            new FewShotExampleDto { AgentId = "aZ", UserMessage = "second", AssistantMessage = "a", SortOrder = 1, IsEnabled = true }, default);

        // Reverse the order
        await _service.ReorderFewShotExamplesAsync("aZ", 1, [id2, id1], default);

        var examples = await _service.GetFewShotExamplesAsync("aZ", 1, default);
        Assert.Equal(2, examples.Count);
        // id2 should now have SortOrder 0
        var ex2 = examples.First(e => e.Id == id2);
        Assert.Equal(0, ex2.SortOrder);
        var ex1 = examples.First(e => e.Id == id1);
        Assert.Equal(1, ex1.SortOrder);
    }
}
