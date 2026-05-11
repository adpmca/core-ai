using Diva.Core.Models;
using Diva.Core.Optimization;
using Diva.Infrastructure.Data;
using Diva.Infrastructure.Data.Entities;
using Diva.Infrastructure.Optimization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Diva.Agents.Tests.Optimization;

/// <summary>
/// Tests for OptimizationApplicator — all 8 suggestion type branches + guard.
/// Uses real in-memory SQLite and NSubstitute for IAgentSetupAssistant.
/// </summary>
public class OptimizationApplicatorTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<DivaDbContext> _opts;
    private readonly IAgentSetupAssistant _setupAssistant;
    private readonly IOptimizationRulePackAccessor _rulePackAccessor;
    private readonly IOptimizationLlmAnalyzer _llmAnalyzer;
    private readonly OptimizationApplicator _applicator;

    public OptimizationApplicatorTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _opts = new DbContextOptionsBuilder<DivaDbContext>()
            .UseSqlite(_connection)
            .Options;
        using var db = new DivaDbContext(_opts);
        db.Database.EnsureCreated();

        _setupAssistant = Substitute.For<IAgentSetupAssistant>();
        _setupAssistant.SavePromptVersionAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        _rulePackAccessor = Substitute.For<IOptimizationRulePackAccessor>();
        _rulePackAccessor.EnablePackAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        _llmAnalyzer = Substitute.For<IOptimizationLlmAnalyzer>();
        // Default: simulate a coherent merge by joining current + suggested
        _llmAnalyzer.MergePromptAsync(
                Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<AgentDefinitionEntity>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var current = ci.ArgAt<string>(0);
                var changes = ci.ArgAt<IReadOnlyList<string>>(1);
                var parts = new[] { current }.Concat(changes).Where(s => !string.IsNullOrWhiteSpace(s));
                return Task.FromResult(string.Join("\n\n", parts));
            });

        _applicator = new OptimizationApplicator(
            _setupAssistant,
            _rulePackAccessor,
            _llmAnalyzer,
            NullLogger<OptimizationApplicator>.Instance);
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<(AgentOptimizationSuggestionEntity suggestion, AgentDefinitionEntity agentDef, DivaDbContext db)>
        SeedApprovedSuggestion(string type, string suggestedValue, string? currentValue = null)
    {
        var db = new DivaDbContext(_opts);

        if (!db.AgentDefinitions.Any(a => a.Id == "agent-1"))
        {
            db.AgentDefinitions.Add(new AgentDefinitionEntity
            {
                Id = "agent-1", TenantId = 1, Name = "TestAgent", DisplayName = "TA",
                SystemPrompt = "Original prompt.", Temperature = 0.7, MaxIterations = 5,
                MaxContinuations = 3, VerificationMode = "Off", ModelId = "gpt-4o"
            });
        }

        var run = new AgentOptimizationRunEntity
        {
            TenantId = 1, AgentId = "agent-1", Status = "completed",
            FromDate = DateTime.UtcNow.AddDays(-1), ToDate = DateTime.UtcNow
        };
        db.OptimizationRuns.Add(run);
        await db.SaveChangesAsync();

        var suggestion = new AgentOptimizationSuggestionEntity
        {
            TenantId = 1, RunId = run.Id, AgentId = "agent-1",
            Type = type, FieldName = "field", Status = "Approved",
            SuggestedValue = suggestedValue, CurrentValue = currentValue,
            Confidence = 0.9f, Reasoning = "reason"
        };
        db.OptimizationSuggestions.Add(suggestion);
        await db.SaveChangesAsync();

        var agentDef = await db.AgentDefinitions.FirstAsync(a => a.Id == "agent-1");
        return (suggestion, agentDef, db);
    }

    // ── Guard ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApplyAsync_PendingSuggestion_ThrowsInvalidOperationException()
    {
        await using var db = new DivaDbContext(_opts);
        var agentDef = new AgentDefinitionEntity
            { Id = "a-guard", TenantId = 1, Name = "G", DisplayName = "G" };
        var suggestion = new AgentOptimizationSuggestionEntity
        {
            TenantId = 1, AgentId = "a-guard", Type = "TemperatureAdjustment",
            FieldName = "temperature", SuggestedValue = "0.5",
            Status = "Pending", Confidence = 0.8f, Reasoning = "r"
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _applicator.ApplyAsync(suggestion, agentDef, db, "append", default));
    }

    // ── Suggestion types ──────────────────────────────────────────────────────

    [Fact]
    public async Task ApplyAsync_TemperatureAdjustment_UpdatesTemperatureAndClamps()
    {
        var (suggestion, agentDef, db) = await SeedApprovedSuggestion("TemperatureAdjustment", "1.5");
        await using (db)
        {
            await _applicator.ApplyAsync(suggestion, agentDef, db, "append", default);
        }

        await using var verify = new DivaDbContext(_opts);
        var saved = await verify.AgentDefinitions.FirstAsync(a => a.Id == "agent-1");
        Assert.Equal(1.5, saved.Temperature, precision: 5);
    }

    [Fact]
    public async Task ApplyAsync_TemperatureAdjustment_ClampsAboveMax()
    {
        var (suggestion, agentDef, db) = await SeedApprovedSuggestion("TemperatureAdjustment", "5.0");
        await using (db)
        {
            await _applicator.ApplyAsync(suggestion, agentDef, db, "append", default);
        }

        await using var verify = new DivaDbContext(_opts);
        var saved = await verify.AgentDefinitions.FirstAsync(a => a.Id == "agent-1");
        Assert.Equal(2.0, saved.Temperature, precision: 5);
    }

    [Fact]
    public async Task ApplyAsync_MaxIterationsAdjustment_UpdatesAndClamps()
    {
        var (suggestion, agentDef, db) = await SeedApprovedSuggestion("MaxIterationsAdjustment", "8");
        await using (db)
        {
            await _applicator.ApplyAsync(suggestion, agentDef, db, "append", default);
        }

        await using var verify = new DivaDbContext(_opts);
        var saved = await verify.AgentDefinitions.FirstAsync(a => a.Id == "agent-1");
        Assert.Equal(8, saved.MaxIterations);
    }

    [Fact]
    public async Task ApplyAsync_MaxIterationsAdjustment_ClampsAboveMax()
    {
        var (suggestion, agentDef, db) = await SeedApprovedSuggestion("MaxIterationsAdjustment", "999");
        await using (db)
        {
            await _applicator.ApplyAsync(suggestion, agentDef, db, "append", default);
        }

        await using var verify = new DivaDbContext(_opts);
        var saved = await verify.AgentDefinitions.FirstAsync(a => a.Id == "agent-1");
        Assert.Equal(50, saved.MaxIterations);
    }

    [Fact]
    public async Task ApplyAsync_MaxContinuationsAdjustment_UpdatesAndClamps()
    {
        var (suggestion, agentDef, db) = await SeedApprovedSuggestion("MaxContinuationsAdjustment", "5");
        await using (db)
        {
            await _applicator.ApplyAsync(suggestion, agentDef, db, "append", default);
        }

        await using var verify = new DivaDbContext(_opts);
        var saved = await verify.AgentDefinitions.FirstAsync(a => a.Id == "agent-1");
        Assert.Equal(5, saved.MaxContinuations);
    }

    [Fact]
    public async Task ApplyAsync_MaxContinuationsAdjustment_ClampsBelowMin()
    {
        var (suggestion, agentDef, db) = await SeedApprovedSuggestion("MaxContinuationsAdjustment", "-5");
        await using (db)
        {
            await _applicator.ApplyAsync(suggestion, agentDef, db, "append", default);
        }

        await using var verify = new DivaDbContext(_opts);
        var saved = await verify.AgentDefinitions.FirstAsync(a => a.Id == "agent-1");
        Assert.Equal(0, saved.MaxContinuations);
    }

    [Fact]
    public async Task ApplyAsync_VerificationModeUpgrade_UpdatesMode()
    {
        var (suggestion, agentDef, db) = await SeedApprovedSuggestion("VerificationModeUpgrade", "ToolGrounded");
        await using (db)
        {
            await _applicator.ApplyAsync(suggestion, agentDef, db, "append", default);
        }

        await using var verify = new DivaDbContext(_opts);
        var saved = await verify.AgentDefinitions.FirstAsync(a => a.Id == "agent-1");
        Assert.Equal("ToolGrounded", saved.VerificationMode);
    }

    [Fact]
    public async Task ApplyAsync_ModelSwitch_UpdatesModelId()
    {
        var (suggestion, agentDef, db) = await SeedApprovedSuggestion("ModelSwitch", "claude-sonnet-4-6");
        await using (db)
        {
            await _applicator.ApplyAsync(suggestion, agentDef, db, "append", default);
        }

        await using var verify = new DivaDbContext(_opts);
        var saved = await verify.AgentDefinitions.FirstAsync(a => a.Id == "agent-1");
        Assert.Equal("claude-sonnet-4-6", saved.ModelId);
    }

    [Fact]
    public async Task ApplyAsync_SystemPromptImprovement_UpdatesPromptAndCallsSaveVersion()
    {
        var newPrompt = "You are an improved agent.";
        // Configure mock to return the expected merged result
        _llmAnalyzer.MergePromptAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<AgentDefinitionEntity>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(newPrompt));

        var (suggestion, agentDef, db) = await SeedApprovedSuggestion("SystemPromptImprovement", newPrompt);
        await using (db)
        {
            await _applicator.ApplyAsync(suggestion, agentDef, db, "append", default);
        }

        await using var verify = new DivaDbContext(_opts);
        var saved = await verify.AgentDefinitions.FirstAsync(a => a.Id == "agent-1");
        Assert.Equal(newPrompt, saved.SystemPrompt);

        await _setupAssistant.Received(1).SavePromptVersionAsync(
            "agent-1", 1, newPrompt, "optimization", Arg.Any<string?>(), "optimization", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyAsync_ToolStrategyHint_AppendsToExistingPrompt()
    {
        var hint = "Always use the search tool first.";
        var (suggestion, agentDef, db) = await SeedApprovedSuggestion("ToolStrategyHint", hint);
        await using (db)
        {
            await _applicator.ApplyAsync(suggestion, agentDef, db, "append", default);
        }

        await using var verify = new DivaDbContext(_opts);
        var saved = await verify.AgentDefinitions.FirstAsync(a => a.Id == "agent-1");
        Assert.Contains("Original prompt.", saved.SystemPrompt!);
        Assert.Contains(hint, saved.SystemPrompt!);

        // SavePromptVersionAsync should be called with the combined prompt
        await _setupAssistant.Received(1).SavePromptVersionAsync(
            "agent-1", 1, Arg.Is<string>(p => p.Contains(hint)), "optimization",
            Arg.Any<string?>(), "optimization", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyAsync_SystemPromptImprovement_LlmMergeFails_FallsBackToAppend()
    {
        var hint = "Add tool instructions.";
        _llmAnalyzer.MergePromptAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<AgentDefinitionEntity>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("LLM unavailable"));

        var (suggestion, agentDef, db) = await SeedApprovedSuggestion("SystemPromptImprovement", hint);
        await using (db)
            await _applicator.ApplyAsync(suggestion, agentDef, db, "append", default);

        await using var verify = new DivaDbContext(_opts);
        var saved = await verify.AgentDefinitions.FirstAsync(a => a.Id == "agent-1");
        Assert.Contains("Original prompt.", saved.SystemPrompt!);
        Assert.Contains(hint, saved.SystemPrompt!);
    }

    [Fact]
    public async Task ApplyAsync_SuggestionStatusSetToApplied()
    {
        var (suggestion, agentDef, db) = await SeedApprovedSuggestion("ModelSwitch", "new-model");
        await using (db)
        {
            await _applicator.ApplyAsync(suggestion, agentDef, db, "append", default);
        }

        await using var verify = new DivaDbContext(_opts);
        var saved = await verify.OptimizationSuggestions.FirstAsync(s => s.AgentId == "agent-1");
        Assert.Equal("Applied", saved.Status);
    }

    [Fact]
    public async Task ApplyAsync_VerificationModeUpgrade_InvalidMode_SkipsUpdate()
    {
        var (suggestion, agentDef, db) = await SeedApprovedSuggestion("VerificationModeUpgrade", "hybrid");
        await using (db)
        {
            await _applicator.ApplyAsync(suggestion, agentDef, db, "append", default);
        }

        await using var verify = new DivaDbContext(_opts);
        var saved = await verify.AgentDefinitions.FirstAsync(a => a.Id == "agent-1");
        Assert.Equal("Off", saved.VerificationMode); // seed value unchanged
    }

    [Fact]
    public async Task ApplyAsync_RulePackSuggestion_CallsEnablePack()
    {
        var (suggestion, agentDef, db) = await SeedApprovedSuggestion("RulePackSuggestion", "42");
        await using (db)
        {
            await _applicator.ApplyAsync(suggestion, agentDef, db, "append", default);
        }

        await _rulePackAccessor.Received(1).EnablePackAsync(42, 1, Arg.Any<CancellationToken>());
    }
}
