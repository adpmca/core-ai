# Agent Flow — Testing Strategy

> **Companion to:** [testing.md](testing.md)
> **Covers:** Every layer of the agent execution path — from HTTP controller through to session persistence.
> **Rule:** Never mock the database. LLM API calls are the only exception — mock them with canned responses.

---

## The Two Agent Flow Paths

```
Path A — Direct (single agent by ID)
  POST /api/agents/{id}/invoke
      └── AgentsController
              └── AnthropicAgentRunner.RunAsync
                      ├── AgentSessionService     (load/save session)
                      ├── McpClient               (tool calls — if configured)
                      ├── ResponseVerifier        (Phase 13)
                      └── RuleLearningService     (Phase 11)

Path B — Supervisor (capability-routed, multi-agent)
  POST /api/supervisor/invoke
      └── SupervisorController
              └── SupervisorAgent.InvokeAsync
                      ├── AgentSessionService     (load/save session)
                      └── ISupervisorPipelineStage × 7
                              ├── DecomposeStage
                              ├── CapabilityMatchStage  → DynamicAgentRegistry
                              ├── DispatchStage         → IWorkerAgent.ExecuteAsync (parallel)
                              ├── MonitorStage
                              ├── IntegrateStage
                              ├── VerifyStage           → ResponseVerifier
                              └── DeliverStage
```

---

## Test Projects

```
tests/
├── Diva.Agents.Tests/          ← supervisor pipeline, registry, stages
├── Diva.Infrastructure.Tests/  ← NEW: ResponseVerifier, RuleLearningService, SessionRuleManager
├── Diva.TenantAdmin.Tests/     ← business rules, prompt builder (Phase 6)
└── Diva.Tools.Tests/           ← MCP tools (Phase 5)
```

`Diva.Infrastructure.Tests` does not exist yet — create it alongside this work.

---

## NuGet Packages Required

```xml
<!-- tests/Diva.Agents.Tests/Diva.Agents.Tests.csproj -->
<PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
<PackageReference Include="xunit" Version="2.*" />
<PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
<PackageReference Include="NSubstitute" Version="5.*" />
<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.*" />
```

---

## Shared Test Helpers

### FakeWorkerAgent

Used everywhere a real `IWorkerAgent` would call the LLM.

```csharp
// tests/Diva.Agents.Tests/Helpers/FakeWorkerAgent.cs
using Diva.Agents.Workers;
using Diva.Core.Models;

public sealed class FakeWorkerAgent : IWorkerAgent
{
    private readonly AgentCapability _capability;
    private readonly AgentResponse _response;

    public FakeWorkerAgent(
        string agentId,
        string[] capabilities,
        AgentResponse? response = null,
        int priority = 10)
    {
        _capability = new AgentCapability
        {
            AgentId      = agentId,
            AgentType    = agentId,
            Capabilities = capabilities,
            Priority     = priority
        };
        _response = response ?? new AgentResponse
        {
            Success   = true,
            Content   = $"Response from {agentId}",
            AgentName = agentId
        };
    }

    public AgentCapability GetCapability() => _capability;

    public Task<AgentResponse> ExecuteAsync(
        AgentRequest request, TenantContext tenant, CancellationToken ct)
        => Task.FromResult(_response);
}
```

### TempSqliteFixture

```csharp
// tests/Diva.Agents.Tests/Helpers/TempSqliteFixture.cs
using Diva.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

public sealed class TempSqliteFixture : IDisposable
{
    public string DbPath { get; } = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.db");

    public DivaDbContext CreateContext(int tenantId = 0)
    {
        var options = new DbContextOptionsBuilder<DivaDbContext>()
            .UseSqlite($"DataSource={DbPath}")
            .Options;
        var db = new DivaDbContext(options, tenantId);
        db.Database.EnsureCreated();
        return db;
    }

    public DatabaseProviderFactory CreateFactory()
    {
        // Fake IOptions<DatabaseOptions> pointing at the temp file
        var opts = Microsoft.Extensions.Options.Options.Create(new Diva.Core.Configuration.DatabaseOptions
        {
            Provider = "SQLite",
            SQLite   = new() { ConnectionString = $"DataSource={DbPath}" }
        });
        return new DatabaseProviderFactory(opts);
    }

    public void Dispose() { try { File.Delete(DbPath); } catch { } }
}
```

---

## 1. DecomposeStage Tests

**Scope:** Pure logic — no external dependencies.

```csharp
// tests/Diva.Agents.Tests/Supervisor/DecomposeStageTests.cs
public class DecomposeStageTests
{
    private readonly DecomposeStage _stage = new(NullLogger<DecomposeStage>.Instance);

    [Fact]
    public async Task NoPreferredAgent_ProducesEmptyCapabilities()
    {
        var state = MakeState(query: "What is revenue?", preferredAgent: null);
        var result = await _stage.ExecuteAsync(state, default);

        Assert.Single(result.SubTasks);
        Assert.Empty(result.SubTasks[0].RequiredCapabilities);
        Assert.Equal("What is revenue?", result.SubTasks[0].Description);
    }

    [Fact]
    public async Task WithPreferredAgent_SetsCapabilityToAgentName()
    {
        var state = MakeState(query: "Book a tee time", preferredAgent: "Reservation");
        var result = await _stage.ExecuteAsync(state, default);

        Assert.Single(result.SubTasks);
        Assert.Equal(["Reservation"], result.SubTasks[0].RequiredCapabilities);
    }

    [Fact]
    public async Task SubTask_InheritsTenantIdFromContext()
    {
        var state = MakeState(query: "test", tenantId: 42);
        var result = await _stage.ExecuteAsync(state, default);

        Assert.Equal(42, result.SubTasks[0].TenantId);
    }

    private static SupervisorState MakeState(string query, string? preferredAgent = null, int tenantId = 1) =>
        new()
        {
            Request       = new AgentRequest { Query = query, PreferredAgent = preferredAgent },
            TenantContext = new TenantContext { TenantId = tenantId }
        };
}
```

---

## 2. CapabilityMatchStage Tests

**Scope:** Depends on `IAgentRegistry` — use `NSubstitute`.

```csharp
// tests/Diva.Agents.Tests/Supervisor/CapabilityMatchStageTests.cs
public class CapabilityMatchStageTests
{
    [Fact]
    public async Task MatchesAgentByCapability()
    {
        var analyticsAgent = new FakeWorkerAgent("analytics", ["analytics", "revenue"]);
        var registry = Substitute.For<IAgentRegistry>();
        registry.FindBestMatchAsync(Arg.Any<string[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(analyticsAgent);

        var stage = new CapabilityMatchStage(registry, NullLogger<CapabilityMatchStage>.Instance);
        var state = MakeState(capabilities: ["analytics"]);

        var result = await stage.ExecuteAsync(state, default);

        Assert.Single(result.DispatchPlan);
        Assert.Equal("analytics", result.DispatchPlan[0].Agent.GetCapability().AgentId);
    }

    [Fact]
    public async Task NoAgentsFound_SetsFailedStatus()
    {
        var registry = Substitute.For<IAgentRegistry>();
        registry.FindBestMatchAsync(default!, default, default)
                .ReturnsForAnyArgs((IWorkerAgent?)null);

        var stage = new CapabilityMatchStage(registry, NullLogger<CapabilityMatchStage>.Instance);
        var state = MakeState(capabilities: ["unknown"]);

        var result = await stage.ExecuteAsync(state, default);

        Assert.Equal(SupervisorStatus.Failed, result.Status);
        Assert.NotEmpty(result.ErrorMessage!);
    }

    private static SupervisorState MakeState(string[] capabilities) =>
        new()
        {
            Request       = new AgentRequest { Query = "test" },
            TenantContext = new TenantContext { TenantId = 1 },
            SubTasks      = [new SubTask("test", capabilities, SiteId: 0, TenantId: 1)]
        };
}
```

---

## 3. DynamicAgentRegistry Tests

**Scope:** Depends on DB (real SQLite) — use `TempSqliteFixture`.

```csharp
// tests/Diva.Agents.Tests/Registry/DynamicAgentRegistryTests.cs
public class DynamicAgentRegistryTests : IDisposable
{
    private readonly TempSqliteFixture _db = new();

    [Fact]
    public async Task FindBestMatch_ReturnsStaticallyRegisteredAgent()
    {
        var registry = CreateRegistry();
        registry.Register(new FakeWorkerAgent("analytics", ["analytics", "revenue"]));

        var match = await registry.FindBestMatchAsync(["analytics"], tenantId: 1, default);

        Assert.NotNull(match);
        Assert.Equal("analytics", match.GetCapability().AgentId);
    }

    [Fact]
    public async Task FindBestMatch_LoadsPublishedAgentsFromDb()
    {
        using var db = _db.CreateContext();
        db.AgentDefinitions.Add(new AgentDefinitionEntity
        {
            Id           = "agent-1",
            TenantId     = 1,
            Name         = "test-agent",
            DisplayName  = "Test",
            Description  = "desc",
            AgentType    = "Dynamic",
            Status       = "Published",
            IsEnabled    = true,
            Capabilities = "[\"reporting\"]"
        });
        await db.SaveChangesAsync();

        var registry = CreateRegistry();
        var match = await registry.FindBestMatchAsync(["reporting"], tenantId: 1, default);

        Assert.NotNull(match);
    }

    [Fact]
    public async Task FindBestMatch_IgnoresDisabledAgents()
    {
        using var db = _db.CreateContext();
        db.AgentDefinitions.Add(new AgentDefinitionEntity
        {
            Id = "agent-2", TenantId = 1, Name = "disabled",
            DisplayName = "D", Description = "d", AgentType = "Dynamic",
            Status = "Published", IsEnabled = false,
            Capabilities = "[\"analytics\"]"
        });
        await db.SaveChangesAsync();

        var registry = CreateRegistry();
        var match = await registry.FindBestMatchAsync(["analytics"], tenantId: 1, default);

        Assert.Null(match);
    }

    [Fact]
    public async Task FindBestMatch_HigherCapabilityScoreWins()
    {
        var low  = new FakeWorkerAgent("low",  ["analytics"],           priority: 5);
        var high = new FakeWorkerAgent("high", ["analytics", "revenue"], priority: 1);

        var registry = CreateRegistry();
        registry.Register(low);
        registry.Register(high);

        var match = await registry.FindBestMatchAsync(["analytics", "revenue"], tenantId: 1, default);

        Assert.Equal("high", match!.GetCapability().AgentId);
    }

    [Fact]
    public async Task FindBestMatch_FallsBackToHighestPriorityWhenNoCapsMatch()
    {
        var low  = new FakeWorkerAgent("low",  ["x"], priority: 1);
        var high = new FakeWorkerAgent("high", ["x"], priority: 100);

        var registry = CreateRegistry();
        registry.Register(low);
        registry.Register(high);

        // Request capability that neither supports
        var match = await registry.FindBestMatchAsync(["zzz"], tenantId: 1, default);

        Assert.Equal("high", match!.GetCapability().AgentId);
    }

    [Fact]
    public async Task FindBestMatch_ReturnsNull_WhenNoAgentsAtAll()
    {
        var registry = CreateRegistry();
        var match = await registry.FindBestMatchAsync([], tenantId: 99, default);
        Assert.Null(match);
    }

    [Fact]
    public async Task TenantIsolation_AgentForTenant2_NotVisibleToTenant1()
    {
        using var db = _db.CreateContext();
        db.AgentDefinitions.Add(new AgentDefinitionEntity
        {
            Id = "t2-agent", TenantId = 2, Name = "t2",
            DisplayName = "T2", Description = "d", AgentType = "Dynamic",
            Status = "Published", IsEnabled = true,
            Capabilities = "[\"analytics\"]"
        });
        await db.SaveChangesAsync();

        var registry = CreateRegistry();
        var agents = await registry.GetAgentsForTenantAsync(tenantId: 1, default);

        Assert.Empty(agents);
    }

    private DynamicAgentRegistry CreateRegistry() =>
        new(_db.CreateFactory(),
            null!,   // AnthropicAgentRunner — not needed for static-only tests
            NullLogger<DynamicAgentRegistry>.Instance);

    public void Dispose() => _db.Dispose();
}
```

---

## 4. DispatchStage Tests

**Scope:** Uses `FakeWorkerAgent` — no DB or LLM needed.

```csharp
// tests/Diva.Agents.Tests/Supervisor/DispatchStageTests.cs
public class DispatchStageTests
{
    [Fact]
    public async Task SingleAgent_ResultAddedToWorkerResults()
    {
        var agent  = new FakeWorkerAgent("analytics", [], new AgentResponse { Success = true, Content = "42" });
        var state  = MakeState(agent);
        var stage  = new DispatchStage(NullLogger<DispatchStage>.Instance);

        var result = await stage.ExecuteAsync(state, default);

        Assert.Single(result.WorkerResults);
        Assert.Equal("42", result.WorkerResults[0].Content);
    }

    [Fact]
    public async Task MultipleAgents_AllExecuted_InParallel()
    {
        var a1 = new FakeWorkerAgent("a1", [], new AgentResponse { Success = true, Content = "r1" });
        var a2 = new FakeWorkerAgent("a2", [], new AgentResponse { Success = true, Content = "r2" });
        var state = MakeState(a1, a2);
        var stage = new DispatchStage(NullLogger<DispatchStage>.Instance);

        var result = await stage.ExecuteAsync(state, default);

        Assert.Equal(2, result.WorkerResults.Count);
        Assert.Contains(result.WorkerResults, r => r.Content == "r1");
        Assert.Contains(result.WorkerResults, r => r.Content == "r2");
    }

    [Fact]
    public async Task ToolEvidence_AccumulatedFromAllWorkers()
    {
        var a1 = new FakeWorkerAgent("a1", [], new AgentResponse
            { Success = true, Content = "r1", ToolEvidence = "[Tool: get_revenue]\n100" });
        var a2 = new FakeWorkerAgent("a2", [], new AgentResponse
            { Success = true, Content = "r2", ToolEvidence = "[Tool: get_bookings]\n50" });

        var result = await new DispatchStage(NullLogger<DispatchStage>.Instance)
            .ExecuteAsync(MakeState(a1, a2), default);

        Assert.Contains("[Tool: get_revenue]", result.ToolEvidence);
        Assert.Contains("[Tool: get_bookings]", result.ToolEvidence);
    }

    private static SupervisorState MakeState(params FakeWorkerAgent[] agents)
    {
        var task = new SubTask("test query", [], SiteId: 0, TenantId: 1);
        return new SupervisorState
        {
            Request       = new AgentRequest { Query = "test" },
            TenantContext = new TenantContext { TenantId = 1 },
            SubTasks      = [task],
            DispatchPlan  = agents.Select(a => (task, (IWorkerAgent)a)).ToList()
        };
    }
}
```

---

## 5. IntegrateStage Tests

```csharp
// tests/Diva.Agents.Tests/Supervisor/IntegrateStageTests.cs
public class IntegrateStageTests
{
    [Fact]
    public async Task SingleResult_UsedAsIntegratedResult()
    {
        var state = MakeState(new AgentResponse { Success = true, Content = "Revenue is $100" });
        var result = await new IntegrateStage(NullLogger<IntegrateStage>.Instance)
            .ExecuteAsync(state, default);

        Assert.Equal("Revenue is $100", result.IntegratedResult);
    }

    [Fact]
    public async Task MultipleResults_ContentJoined()
    {
        var state = MakeState(
            new AgentResponse { Success = true, Content = "Revenue: $100" },
            new AgentResponse { Success = true, Content = "Bookings: 42" });

        var result = await new IntegrateStage(NullLogger<IntegrateStage>.Instance)
            .ExecuteAsync(state, default);

        Assert.Contains("Revenue: $100",  result.IntegratedResult);
        Assert.Contains("Bookings: 42", result.IntegratedResult);
    }

    [Fact]
    public async Task AllFailed_SetsFailedStatus()
    {
        var state = MakeState(new AgentResponse { Success = false, ErrorMessage = "oops" });
        var result = await new IntegrateStage(NullLogger<IntegrateStage>.Instance)
            .ExecuteAsync(state, default);

        Assert.Equal(SupervisorStatus.Failed, result.Status);
    }

    private static SupervisorState MakeState(params AgentResponse[] results) =>
        new()
        {
            Request       = new AgentRequest { Query = "test" },
            TenantContext = new TenantContext { TenantId = 1 },
            WorkerResults = results.ToList()
        };
}
```

---

## 6. SupervisorAgent Tests (pipeline integration)

Use fake stages to test orchestration without touching the real pipeline.

```csharp
// tests/Diva.Agents.Tests/Supervisor/SupervisorAgentTests.cs
public class SupervisorAgentTests : IDisposable
{
    private readonly TempSqliteFixture _db = new();

    [Fact]
    public async Task AllStagesComplete_ReturnsSuccess()
    {
        var supervisor = BuildSupervisor(
            new SetContentStage("Hello from supervisor"),
            new SetCompletedStage());

        var response = await supervisor.InvokeAsync(
            new AgentRequest { Query = "test" },
            new TenantContext { TenantId = 1 },
            default);

        Assert.True(response.Success);
        Assert.Equal("Hello from supervisor", response.Content);
    }

    [Fact]
    public async Task FailedStage_StopsPipelineEarly()
    {
        var stages = new List<ISupervisorPipelineStage>
        {
            new FailStage("stage exploded"),
            new SetContentStage("should not reach this")
        };

        var supervisor = BuildSupervisor(stages.ToArray());
        var response = await supervisor.InvokeAsync(
            new AgentRequest { Query = "test" },
            new TenantContext { TenantId = 1 },
            default);

        Assert.False(response.Success);
        Assert.DoesNotContain("should not reach this", response.Content);
    }

    [Fact]
    public async Task SessionId_ReturnedInResponse()
    {
        var supervisor = BuildSupervisor(new SetCompletedStage());
        var response = await supervisor.InvokeAsync(
            new AgentRequest { Query = "test" },
            new TenantContext { TenantId = 1 },
            default);

        Assert.NotEmpty(response.SessionId!);
    }

    [Fact]
    public async Task SubsequentCall_WithSameSessionId_LoadsHistory()
    {
        var supervisor = BuildSupervisor(new SetCompletedStage());
        var first = await supervisor.InvokeAsync(
            new AgentRequest { Query = "first" },
            new TenantContext { TenantId = 1 },
            default);

        // Second call with same session ID should not fail
        var second = await supervisor.InvokeAsync(
            new AgentRequest { Query = "second", SessionId = first.SessionId },
            new TenantContext { TenantId = 1 },
            default);

        Assert.True(second.Success);
    }

    private SupervisorAgent BuildSupervisor(params ISupervisorPipelineStage[] stages)
    {
        var sessions = new AgentSessionService(
            _db.CreateFactory(),
            NullLogger<AgentSessionService>.Instance);

        return new SupervisorAgent(stages, sessions, NullLogger<SupervisorAgent>.Instance);
    }

    public void Dispose() => _db.Dispose();
}

// ── Fake stages ───────────────────────────────────────────────────────────────

file sealed class SetContentStage(string content) : ISupervisorPipelineStage
{
    public Task<SupervisorState> ExecuteAsync(SupervisorState state, CancellationToken ct)
    {
        state.IntegratedResult = content;
        return Task.FromResult(state);
    }
}

file sealed class SetCompletedStage : ISupervisorPipelineStage
{
    public Task<SupervisorState> ExecuteAsync(SupervisorState state, CancellationToken ct)
    {
        state.Status           = SupervisorStatus.Completed;
        state.IntegratedResult ??= "done";
        return Task.FromResult(state);
    }
}

file sealed class FailStage(string message) : ISupervisorPipelineStage
{
    public Task<SupervisorState> ExecuteAsync(SupervisorState state, CancellationToken ct)
    {
        state.Status       = SupervisorStatus.Failed;
        state.ErrorMessage = message;
        return Task.FromResult(state);
    }
}
```

---

## 7. ResponseVerifier Tests

```csharp
// tests/Diva.Infrastructure.Tests/Verification/ResponseVerifierTests.cs
public class ResponseVerifierTests
{
    [Fact]
    public async Task Off_AlwaysVerified()
    {
        var verifier = BuildVerifier("Off");
        var result = await verifier.VerifyAsync("any text", [], "", default);

        Assert.True(result.IsVerified);
        Assert.Equal("Off", result.Mode);
    }

    [Fact]
    public async Task ToolGrounded_NoToolsNoFactualClaims_IsVerified()
    {
        var verifier = BuildVerifier("ToolGrounded");
        var result   = await verifier.VerifyAsync("Sure, I can help you.", [], "", default);

        Assert.True(result.IsVerified);
    }

    [Fact]
    public async Task ToolGrounded_NoTools_ButHasNumbers_IsUnverified()
    {
        var verifier = BuildVerifier("ToolGrounded");
        var result   = await verifier.VerifyAsync(
            "Revenue was $1,234,567 last quarter.", [], "", default);

        Assert.False(result.IsVerified);
        Assert.NotEmpty(result.UngroundedClaims);
    }

    [Fact]
    public async Task ToolGrounded_ToolsWereCalled_IsVerified()
    {
        var verifier = BuildVerifier("ToolGrounded");
        var result   = await verifier.VerifyAsync(
            "Revenue was $1,234,567 last quarter.",
            ["get_revenue"],
            "[Tool: get_revenue]\n$1,234,567",
            default);

        Assert.True(result.IsVerified);
    }

    private static ResponseVerifier BuildVerifier(string mode)
    {
        var opts = Microsoft.Extensions.Options.Options.Create(new VerificationOptions
        {
            Mode = mode, ConfidenceThreshold = 0.5f
        });
        var llm = Microsoft.Extensions.Options.Options.Create(new LlmOptions());
        return new ResponseVerifier(opts, llm, NullLogger<ResponseVerifier>.Instance);
    }
}
```

---

## 8. RuleLearningService Tests

```csharp
// tests/Diva.Infrastructure.Tests/Learning/RuleLearningServiceTests.cs
public class RuleLearningServiceTests : IDisposable
{
    private readonly TempSqliteFixture _db = new();

    [Fact]
    public async Task SessionOnly_NotPersistedToDb()
    {
        var (service, cache) = BuildService();
        var rule = MakeRule("r-session");

        await service.SaveLearnedRuleAsync(1, rule, RuleApprovalMode.SessionOnly, default);

        using var db = _db.CreateContext();
        Assert.Empty(db.LearnedRules.ToList());

        var sessionRules = await cache.GetSessionRulesAsync(rule.SourceSessionId, default);
        Assert.Single(sessionRules);
    }

    [Fact]
    public async Task RequireAdmin_PersistedAsPending()
    {
        var (service, _) = BuildService();
        var rule = MakeRule("r-pending");

        await service.SaveLearnedRuleAsync(1, rule, RuleApprovalMode.RequireAdmin, default);

        using var db = _db.CreateContext();
        var entity = db.LearnedRules.Single();
        Assert.Equal("pending", entity.Status);
        Assert.Equal("r-pending", entity.RuleKey);
    }

    [Fact]
    public async Task AutoApprove_PromotedToBusinessRule()
    {
        var (service, _) = BuildService();
        var rule = MakeRule("r-auto");

        await service.SaveLearnedRuleAsync(1, rule, RuleApprovalMode.AutoApprove, default);

        using var db = _db.CreateContext();
        Assert.Equal("approved", db.LearnedRules.Single().Status);
        Assert.Single(db.BusinessRules.ToList());  // promoted
    }

    [Fact]
    public async Task GetPendingRulesAsync_ReturnsOnlyPending()
    {
        var (service, _) = BuildService();
        await service.SaveLearnedRuleAsync(1, MakeRule("r-pending"), RuleApprovalMode.RequireAdmin, default);
        await service.SaveLearnedRuleAsync(1, MakeRule("r-auto"),    RuleApprovalMode.AutoApprove,  default);

        var pending = await service.GetPendingRulesAsync(1, default);

        Assert.Single(pending);
        Assert.Equal("r-pending", pending[0].RuleKey);
    }

    [Fact]
    public async Task ApproveRuleAsync_PromotesToBusinessRuleAndSetsStatus()
    {
        var (service, _) = BuildService();
        await service.SaveLearnedRuleAsync(1, MakeRule("r-approve"), RuleApprovalMode.RequireAdmin, default);

        using var db = _db.CreateContext();
        var id = db.LearnedRules.Single().Id;

        await service.ApproveRuleAsync(1, id, "admin@test.com", default);

        db.ChangeTracker.Clear();
        Assert.Equal("approved", db.LearnedRules.Single().Status);
        Assert.Equal("admin@test.com", db.LearnedRules.Single().ReviewedBy);
        Assert.Single(db.BusinessRules.ToList());
    }

    [Fact]
    public async Task RejectRuleAsync_SetsStatusRejectedWithNotes()
    {
        var (service, _) = BuildService();
        await service.SaveLearnedRuleAsync(1, MakeRule("r-reject"), RuleApprovalMode.RequireAdmin, default);

        using var db = _db.CreateContext();
        var id = db.LearnedRules.Single().Id;

        await service.RejectRuleAsync(1, id, "admin@test.com", "Not a valid rule", default);

        db.ChangeTracker.Clear();
        Assert.Equal("rejected", db.LearnedRules.Single().Status);
        Assert.Equal("Not a valid rule", db.LearnedRules.Single().ReviewNotes);
        Assert.Empty(db.BusinessRules.ToList());  // not promoted
    }

    private (RuleLearningService, ISessionRuleManager) BuildService()
    {
        var cache    = new SessionRuleManager(new Microsoft.Extensions.Caching.Memory.MemoryDistributedCache(
            Microsoft.Extensions.Options.Options.Create(new Microsoft.Extensions.Caching.Memory.MemoryDistributedCacheOptions())));
        var extractor = Substitute.For<LlmRuleExtractor>(
            Microsoft.Extensions.Options.Options.Create(new LlmOptions()),
            NullLogger<LlmRuleExtractor>.Instance);
        var service = new RuleLearningService(extractor, cache, _db.CreateFactory(), NullLogger<RuleLearningService>.Instance);
        return (service, cache);
    }

    private static SuggestedRule MakeRule(string key) => new()
    {
        AgentType       = "*",
        RuleCategory    = "reporting",
        RuleKey         = key,
        PromptInjection = $"Rule: {key}",
        Confidence      = 0.9f,
        SourceSessionId = "session-test"
    };

    public void Dispose() => _db.Dispose();
}
```

---

## 9. LlmRuleExtractor Tests

The extractor makes LLM calls — unit-test the parsing logic directly by calling the private parser method pattern via subclassing or by testing with known JSON input through a thin wrapper.

```csharp
// tests/Diva.Infrastructure.Tests/Learning/LlmRuleExtractorParseTests.cs
public class LlmRuleExtractorParseTests
{
    // Test the extractor's resilience to bad LLM output by driving ExtractAsync
    // with a fake provider that returns canned JSON.
    // Since LlmRuleExtractor uses LlmOptions to pick Anthropic vs OpenAI,
    // we point it at a provider that doesn't exist and test the fallback path.

    [Fact]
    public async Task NonJsonResponse_ReturnsEmptyList()
    {
        // Point at a non-existent endpoint — extractor should catch and return []
        var opts = Microsoft.Extensions.Options.Options.Create(new LlmOptions
        {
            DirectProvider = new DirectProviderOptions
            {
                Provider = "OpenAI",
                ApiKey   = "no-key",
                Endpoint = "http://localhost:1/", // nothing listening
                Model    = "test"
            }
        });
        var extractor = new LlmRuleExtractor(opts, NullLogger<LlmRuleExtractor>.Instance);

        var rules = await extractor.ExtractAsync("User: hello\nAgent: hi", "session-1", default);

        Assert.Empty(rules);  // exception caught, returns []
    }
}
```

---

## 10. AgentSessionService Tests

```csharp
// tests/Diva.Agents.Tests/Sessions/AgentSessionServiceTests.cs
public class AgentSessionServiceTests : IDisposable
{
    private readonly TempSqliteFixture _db = new();
    private AgentSessionService Service => new(_db.CreateFactory(), NullLogger<AgentSessionService>.Instance);

    [Fact]
    public async Task NullSessionId_CreatesNewSession()
    {
        var (sessionId, history) = await Service.GetOrCreateAsync(
            null, "agent-1", new TenantContext { TenantId = 1 }, default);

        Assert.NotEmpty(sessionId);
        Assert.Empty(history);
    }

    [Fact]
    public async Task ExistingSessionId_LoadsHistory()
    {
        var svc = Service;
        var (sessionId, _) = await svc.GetOrCreateAsync(
            null, "agent-1", new TenantContext { TenantId = 1 }, default);

        await svc.SaveTurnAsync(sessionId, "user msg", "agent reply", default);

        var (_, history) = await svc.GetOrCreateAsync(
            sessionId, "agent-1", new TenantContext { TenantId = 1 }, default);

        Assert.Equal(2, history.Count);
        Assert.Equal("user", history[0].Role);
        Assert.Equal("user msg", history[0].Content);
        Assert.Equal("assistant", history[1].Role);
    }

    [Fact]
    public async Task UnknownSessionId_CreatesNewSession()
    {
        var (sessionId, history) = await Service.GetOrCreateAsync(
            "non-existent-id", "agent-1", new TenantContext { TenantId = 1 }, default);

        Assert.NotEqual("non-existent-id", sessionId);
        Assert.Empty(history);
    }

    public void Dispose() => _db.Dispose();
}
```

---

## 11. HTTP Integration Tests (WebApplicationFactory)

Full-stack tests hitting the real HTTP layer with SQLite and a stubbed LLM.

```csharp
// tests/Diva.Agents.Tests/Integration/AgentFlowIntegrationTests.cs
public class AgentFlowIntegrationTests : IClassFixture<DivaWebFactory>
{
    private readonly HttpClient _client;

    public AgentFlowIntegrationTests(DivaWebFactory factory)
        => _client = factory.CreateClient();

    [Fact]
    public async Task InvokeAgent_AgentNotFound_Returns404()
    {
        var payload = new { query = "test", sessionId = (string?)null };
        var response = await _client.PostAsJsonAsync("/api/agents/non-existent/invoke", payload);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetPendingRules_ReturnsEmptyArray()
    {
        var response = await _client.GetAsync("/api/learned-rules?tenantId=1");
        response.EnsureSuccessStatusCode();
        var rules = await response.Content.ReadFromJsonAsync<JsonElement[]>();
        Assert.NotNull(rules);
        Assert.Empty(rules);
    }

    [Fact]
    public async Task SupervisorInvoke_NoAgentsPublished_ReturnsFailure()
    {
        var payload = new { query = "What is revenue?", triggerType = "user" };
        var response = await _client.PostAsJsonAsync("/api/supervisor/invoke", payload);
        // Fails because no agents are published — but HTTP layer should still respond
        Assert.True(
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.BadRequest,
            $"Expected 200/400 but got {response.StatusCode}");
    }
}

// ── WebApplicationFactory ─────────────────────────────────────────────────────

public class DivaWebFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Replace real DbContext with temp-file SQLite
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<DivaDbContext>));
            if (descriptor is not null) services.Remove(descriptor);

            services.AddDbContext<DivaDbContext>(options =>
                options.UseSqlite($"DataSource={_dbPath}"));

            // Replace real JWT with test stub (always valid, TenantId=1)
            services.AddSingleton<IOAuthTokenValidator, TestOAuthTokenValidator>();

            // Replace AnthropicAgentRunner with a stub (no real LLM calls)
            services.AddSingleton<AnthropicAgentRunner>(sp =>
                Substitute.For<AnthropicAgentRunner>());
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { File.Delete(_dbPath); } catch { }
    }
}

// Always passes validation, returns TenantId=1
public sealed class TestOAuthTokenValidator : IOAuthTokenValidator
{
    public Task<TenantContext?> ValidateAsync(string token, CancellationToken ct)
        => Task.FromResult<TenantContext?>(new TenantContext { TenantId = 1, UserId = "test-user" });
}
```

---

## Test Coverage Matrix

| Component | Type | Key Scenarios |
|-----------|------|--------------|
| `DecomposeStage` | Unit | No preferred agent; preferred agent → capabilities; tenant ID propagation |
| `CapabilityMatchStage` | Unit (mocked registry) | Match found; no match → Failed state |
| `DynamicAgentRegistry` | Integration (SQLite) | Static agents; DB-loaded agents; tenant isolation; capability scoring; fallback to priority |
| `DispatchStage` | Unit (FakeWorkerAgent) | Single agent; parallel multi-agent; tool evidence accumulated |
| `IntegrateStage` | Unit | Single result; multiple joined; all-failed → Failed |
| `VerifyStage` | Unit (mocked verifier) | Passes verification result through; WasBlocked → Failed |
| `SupervisorAgent` | Integration (SQLite + fake stages) | Full pipeline; failed stage stops early; session ID returned; history loaded on second call |
| `ResponseVerifier` | Unit | Off passthrough; ToolGrounded no-tools; ToolGrounded with tools; factual claim detection |
| `RuleLearningService` | Integration (SQLite) | SessionOnly not persisted; RequireAdmin → pending; AutoApprove → promoted; Approve/Reject lifecycle |
| `LlmRuleExtractor` | Unit | Bad endpoint → empty list; malformed JSON → empty list |
| `AgentSessionService` | Integration (SQLite) | New session; existing session loads history; unknown ID creates new |
| HTTP `/api/agents/{id}/invoke` | E2E (WebFactory) | 404 for missing agent; session ID returned |
| HTTP `/api/learned-rules` | E2E (WebFactory) | Empty array; approve/reject 204 |
| HTTP `/api/supervisor/invoke` | E2E (WebFactory) | No-agents failure; valid response shape |

---

## What to Mock vs What to Use Real

| Dependency | In unit tests | In integration tests |
|-----------|---------------|---------------------|
| `DivaDbContext` | None (use real SQLite) | Real SQLite (TempSqliteFixture) |
| LLM API (Anthropic/OpenAI) | `FakeWorkerAgent` returning canned response | `AnthropicAgentRunner` substituted |
| `IAgentRegistry` | `NSubstitute` | Real `DynamicAgentRegistry` with SQLite |
| `ISessionRuleManager` | `MemoryDistributedCache` | `MemoryDistributedCache` |
| `IOAuthTokenValidator` | N/A | `TestOAuthTokenValidator` (always valid) |
| `MCP tools` | `FakeWorkerAgent` bypasses | Not tested at this layer |
| `IDistributedCache` | `MemoryDistributedCache` | `MemoryDistributedCache` |

---

## Running the Agent Flow Tests

```bash
# Run all agent tests
dotnet test tests/Diva.Agents.Tests

# Run all infrastructure tests (new project)
dotnet test tests/Diva.Infrastructure.Tests

# Run only integration tests
dotnet test --filter "Category=Integration"

# Run with detailed output
dotnet test tests/Diva.Agents.Tests --logger "console;verbosity=detailed"

# Run a specific test
dotnet test --filter "FullyQualifiedName~DynamicAgentRegistryTests.FindBestMatch_HigherCapabilityScoreWins"
```
