using Diva.Infrastructure.Data;
using Diva.Infrastructure.Data.Entities;
using Diva.TenantAdmin.Services;
using Diva.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Diva.TenantAdmin.Tests;

/// <summary>
/// Tests for RulePackEngine — runtime evaluation of hook rules.
/// Uses real SQLite for ResolvePacksAsync, direct rule evaluation for unit tests.
/// </summary>
public class RulePackEngineTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DivaDbContext _db;
    private readonly RulePackEngine _engine;
    private readonly IMemoryCache _cache;

    public RulePackEngineTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var opts = new DbContextOptionsBuilder<DivaDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new DivaDbContext(opts);
        _db.Database.EnsureCreated();

        _cache = new MemoryCache(new MemoryCacheOptions());
        _engine = new RulePackEngine(
            new DirectDbFactory(opts),
            _cache,
            NullLogger<RulePackEngine>.Instance);
    }

    public void Dispose()
    {
        _engine.Dispose();
        _db.Dispose();
        _cache.Dispose();
        _connection.Dispose();
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static ResolvedRulePack MakePack(int id, string name, int priority, params HookRuleEntity[] rules) =>
        new(id, name, priority, 5000, rules.ToList());

    private static HookRuleEntity Rule(string hookPoint, string ruleType, int order,
        string? pattern = null, string? instruction = null, string? replacement = null,
        string? toolName = null, bool stopOnMatch = false) =>
        new()
        {
            Id = order, // Use order as ID for tests
            HookPoint = hookPoint,
            RuleType = ruleType,
            Pattern = pattern,
            Instruction = instruction,
            Replacement = replacement,
            ToolName = toolName,
            OrderInPack = order,
            StopOnMatch = stopOnMatch,
            IsEnabled = true,
            MaxEvaluationMs = 1000,
        };

    // ── inject_prompt ─────────────────────────────────────────────────────────

    [Fact]
    public void EvaluateOnInit_InjectPrompt_AppendsToSystemPrompt()
    {
        var packs = new List<ResolvedRulePack>
        {
            MakePack(1, "Inject", 100,
                Rule("OnInit", "inject_prompt", 1, instruction: "Always cite data sources."))
        };

        var result = _engine.EvaluateOnInit(packs, "You are a helpful assistant.", "query", "agent-1", 1);

        Assert.Contains("Always cite data sources.", result.ModifiedText);
        Assert.Contains("You are a helpful assistant.", result.ModifiedText);
        Assert.Single(result.TriggeredRules);
        Assert.Equal("injected", result.TriggeredRules[0].Action);
    }

    [Fact]
    public void EvaluateOnInit_InjectPrompt_SkipsIfNoInstruction()
    {
        var packs = new List<ResolvedRulePack>
        {
            MakePack(1, "Empty", 100,
                Rule("OnInit", "inject_prompt", 1, instruction: ""))
        };

        var result = _engine.EvaluateOnInit(packs, "base prompt", "query", "a", 1);

        Assert.Equal("base prompt", result.ModifiedText);
        Assert.Empty(result.TriggeredRules);
    }

    // ── tool_require ──────────────────────────────────────────────────────────

    [Fact]
    public void EvaluateOnInit_ToolRequire_InjectsWhenQueryMatches()
    {
        var packs = new List<ResolvedRulePack>
        {
            MakePack(1, "Tools", 100,
                Rule("OnInit", "tool_require", 1, pattern: "(?i)weather|forecast", toolName: "get_weather"))
        };

        var result = _engine.EvaluateOnInit(packs, "base", "What is the weather today?", "a", 1);

        Assert.Contains("get_weather", result.ModifiedText);
        Assert.Single(result.TriggeredRules);
    }

    [Fact]
    public void EvaluateOnInit_ToolRequire_SkipsWhenNoMatch()
    {
        var packs = new List<ResolvedRulePack>
        {
            MakePack(1, "Tools", 100,
                Rule("OnInit", "tool_require", 1, pattern: "(?i)weather", toolName: "get_weather"))
        };

        var result = _engine.EvaluateOnInit(packs, "base", "Tell me about revenue", "a", 1);

        Assert.Equal("base", result.ModifiedText);
        Assert.Empty(result.TriggeredRules);
    }

    [Fact]
    public void EvaluateOnBeforeIteration_InjectPrompt_AppendsEachIterationPrompt()
    {
        var packs = new List<ResolvedRulePack>
        {
            MakePack(1, "Iter", 100,
                Rule("OnBeforeIteration", "inject_prompt", 1, instruction: "Re-check the latest tool evidence before answering."))
        };

        var result = _engine.EvaluateOnBeforeIteration(packs, "base prompt", "query", "a", 1);

        Assert.Contains("base prompt", result.ModifiedText);
        Assert.Contains("Re-check the latest tool evidence before answering.", result.ModifiedText);
        Assert.Single(result.TriggeredRules);
        Assert.Equal("injected", result.TriggeredRules[0].Action);
    }

    [Fact]
    public void EvaluateOnToolFilter_BlockPattern_FiltersMatchingToolCall()
    {
        var packs = new List<ResolvedRulePack>
        {
            MakePack(1, "Tool Filter", 100,
                Rule("OnToolFilter", "block_pattern", 1, pattern: "weather", toolName: "get_weather"))
        };

        var toolCalls = new List<UnifiedToolCallRef>
        {
            new() { Id = "1", Name = "get_weather", InputJson = "{\"city\":\"London\"}" },
            new() { Id = "2", Name = "get_sales", InputJson = "{\"period\":\"q1\"}" },
        };

        var result = _engine.EvaluateOnToolFilter(packs, toolCalls, "show me weather", "a", 1);

        Assert.True(result.Single(tc => tc.Id == "1").Filtered);
        Assert.False(result.Single(tc => tc.Id == "2").Filtered);
    }

    [Fact]
    public void EvaluateOnToolFilter_ToolTransform_RewritesInputJson()
    {
        var packs = new List<ResolvedRulePack>
        {
            MakePack(1, "Tool Transform", 100,
                Rule("OnToolFilter", "tool_transform", 1, pattern: "London", replacement: "Paris", toolName: "get_weather"))
        };

        var toolCalls = new List<UnifiedToolCallRef>
        {
            new() { Id = "1", Name = "get_weather", InputJson = "{\"city\":\"London\"}" },
        };

        var result = _engine.EvaluateOnToolFilter(packs, toolCalls, "weather", "a", 1);

        Assert.Contains("Paris", result[0].InputJson);
    }

    [Fact]
    public void DryRun_OnBeforeIterationRule_IncludedInModifiedPrompt()
    {
        var pack = new HookRulePackEntity
        {
            Id = 1,
            Name = "Iter",
            Rules =
            [
                Rule("OnInit", "inject_prompt", 1, instruction: "Initial guidance."),
                Rule("OnBeforeIteration", "inject_prompt", 2, instruction: "Repeat this check every iteration."),
            ]
        };

        var result = _engine.DryRun(pack, "query", "response");

        Assert.Contains("Initial guidance.", result.ModifiedPrompt);
        Assert.Contains("Repeat this check every iteration.", result.ModifiedPrompt);
        Assert.Equal(2, result.TriggeredRules.Count);
    }

    // ── regex_redact ──────────────────────────────────────────────────────────

    [Fact]
    public void EvaluateOnBeforeResponse_RegexRedact_RedactsMatchingText()
    {
        var packs = new List<ResolvedRulePack>
        {
            MakePack(1, "PII", 100,
                Rule("OnBeforeResponse", "regex_redact", 1,
                    pattern: @"\b\d{3}-\d{2}-\d{4}\b", replacement: "[SSN]"))
        };

        var result = _engine.EvaluateOnBeforeResponse(packs, "Your SSN is 123-45-6789.", "query", "a", 1);

        Assert.Equal("Your SSN is [SSN].", result.ModifiedText);
        Assert.Single(result.TriggeredRules);
        Assert.Equal("modified", result.TriggeredRules[0].Action);
    }

    [Fact]
    public void EvaluateOnBeforeResponse_RegexRedact_DefaultsToRedactedTag()
    {
        var packs = new List<ResolvedRulePack>
        {
            MakePack(1, "PII", 100,
                Rule("OnBeforeResponse", "regex_redact", 1, pattern: @"\b\d{9}\b"))
        };

        var result = _engine.EvaluateOnBeforeResponse(packs, "Account 123456789 found.", "q", "a", 1);

        Assert.Equal("Account [REDACTED] found.", result.ModifiedText);
    }

    [Fact]
    public void EvaluateOnBeforeResponse_RegexRedact_NoMatchNoChange()
    {
        var packs = new List<ResolvedRulePack>
        {
            MakePack(1, "PII", 100,
                Rule("OnBeforeResponse", "regex_redact", 1, pattern: @"\b\d{9}\b", replacement: "[X]"))
        };

        var result = _engine.EvaluateOnBeforeResponse(packs, "No numbers here.", "q", "a", 1);

        Assert.Equal("No numbers here.", result.ModifiedText);
        Assert.Empty(result.TriggeredRules);
    }

    // ── block_pattern ─────────────────────────────────────────────────────────

    [Fact]
    public void EvaluateOnBeforeResponse_BlockPattern_BlocksOnMatch()
    {
        var packs = new List<ResolvedRulePack>
        {
            MakePack(1, "Security", 100,
                Rule("OnBeforeResponse", "block_pattern", 1, pattern: @"(?i)password\s*:\s*\S+"))
        };

        var result = _engine.EvaluateOnBeforeResponse(packs, "Your password: abc123!", "q", "a", 1);

        Assert.True(result.Blocked);
        Assert.Contains("blocked", result.ModifiedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvaluateOnBeforeResponse_BlockPattern_CustomBlockMessage()
    {
        var packs = new List<ResolvedRulePack>
        {
            MakePack(1, "Security", 100,
                Rule("OnBeforeResponse", "block_pattern", 1,
                    pattern: @"(?i)secret", instruction: "This content is restricted."))
        };

        var result = _engine.EvaluateOnBeforeResponse(packs, "The secret is out", "q", "a", 1);

        Assert.True(result.Blocked);
        Assert.Equal("This content is restricted.", result.ModifiedText);
    }

    [Fact]
    public void EvaluateOnBeforeResponse_BlockPattern_NoMatchPasses()
    {
        var packs = new List<ResolvedRulePack>
        {
            MakePack(1, "Security", 100,
                Rule("OnBeforeResponse", "block_pattern", 1, pattern: @"(?i)password"))
        };

        var result = _engine.EvaluateOnBeforeResponse(packs, "Everything is fine.", "q", "a", 1);

        Assert.False(result.Blocked);
    }

    // ── append_text ───────────────────────────────────────────────────────────

    [Fact]
    public void EvaluateOnBeforeResponse_AppendText_AppendsDisclaimer()
    {
        var packs = new List<ResolvedRulePack>
        {
            MakePack(1, "Legal", 100,
                Rule("OnBeforeResponse", "append_text", 1, instruction: "Disclaimer: This is not legal advice."))
        };

        var result = _engine.EvaluateOnBeforeResponse(packs, "Here is the answer.", "q", "a", 1);

        Assert.EndsWith("Disclaimer: This is not legal advice.", result.ModifiedText);
        Assert.StartsWith("Here is the answer.", result.ModifiedText);
    }

    [Fact]
    public void EvaluateOnAfterToolCall_RegexRedact_RedactsToolOutput()
    {
        var packs = new List<ResolvedRulePack>
        {
            MakePack(1, "Post Tool", 100,
                Rule("OnAfterToolCall", "regex_redact", 1, pattern: @"\b\d{4}\b", replacement: "****"))
        };

        var result = _engine.EvaluateOnAfterToolCall(packs, "Revenue 2025", "q", "a", 1);

        Assert.Equal("Revenue ****", result.ModifiedText);
        Assert.Single(result.TriggeredRules);
    }

    [Fact]
    public void EvaluateOnAfterResponse_AppendText_ProducesPostResponseResult()
    {
        var packs = new List<ResolvedRulePack>
        {
            MakePack(1, "After Response", 100,
                Rule("OnAfterResponse", "append_text", 1, instruction: "Logged after response"))
        };

        var result = _engine.EvaluateOnAfterResponse(packs, "final answer", "q", "a", 1);

        Assert.Contains("Logged after response", result.ModifiedText);
        Assert.Single(result.TriggeredRules);
    }

    [Fact]
    public void EvaluateOnError_BlockPattern_AbortsMatchingError()
    {
        var packs = new List<ResolvedRulePack>
        {
            MakePack(1, "Error", 100,
                Rule("OnError", "block_pattern", 1, pattern: "401|403"))
        };

        var result = _engine.EvaluateOnError(packs, "get_weather", new InvalidOperationException("401 unauthorized"), "query", "a", 1);

        Assert.Equal(ErrorRecoveryAction.Abort, result.Action);
        Assert.Single(result.TriggeredRules);
    }

    [Fact]
    public void EvaluateOnError_ToolRequire_RetriesMatchingTool()
    {
        var packs = new List<ResolvedRulePack>
        {
            MakePack(1, "Error", 100,
                Rule("OnError", "tool_require", 1, pattern: "timeout", toolName: "get_weather"))
        };

        var result = _engine.EvaluateOnError(packs, "get_weather", new TimeoutException("tool timeout"), "query", "a", 1);

        Assert.Equal(ErrorRecoveryAction.Retry, result.Action);
        Assert.Single(result.TriggeredRules);
    }

    // ── require_keyword ───────────────────────────────────────────────────────

    [Fact]
    public void EvaluateOnBeforeResponse_RequireKeyword_AppendsWhenMissing()
    {
        var packs = new List<ResolvedRulePack>
        {
            MakePack(1, "Compliance", 100,
                Rule("OnBeforeResponse", "require_keyword", 1, pattern: "compliance"))
        };

        var result = _engine.EvaluateOnBeforeResponse(packs, "All operations are normal.", "q", "a", 1);

        Assert.Contains("compliance", result.ModifiedText);
        Assert.Single(result.TriggeredRules);
    }

    [Fact]
    public void EvaluateOnBeforeResponse_RequireKeyword_NoChangeWhenPresent()
    {
        var packs = new List<ResolvedRulePack>
        {
            MakePack(1, "Compliance", 100,
                Rule("OnBeforeResponse", "require_keyword", 1, pattern: "compliance"))
        };

        var result = _engine.EvaluateOnBeforeResponse(packs, "This meets Compliance standards.", "q", "a", 1);

        Assert.Empty(result.TriggeredRules);
    }

    // ── format_response ───────────────────────────────────────────────────────

    [Fact]
    public void EvaluateOnInit_FormatResponse_InjectsFormatInstruction()
    {
        var packs = new List<ResolvedRulePack>
        {
            MakePack(1, "Format", 100,
                Rule("OnInit", "format_response", 1, instruction: "Use markdown tables for data."))
        };

        var result = _engine.EvaluateOnInit(packs, "base", "show data", "a", 1);

        Assert.Contains("Use markdown tables for data.", result.ModifiedText);
    }

    // ── format_enforce ────────────────────────────────────────────────────────

    [Fact]
    public void EvaluateOnBeforeResponse_FormatEnforce_ModifiesWhenNotMatching()
    {
        var packs = new List<ResolvedRulePack>
        {
            MakePack(1, "Format", 100,
                Rule("OnBeforeResponse", "format_enforce", 1,
                    pattern: @"^\|", instruction: "Format as a table:"))
        };

        var result = _engine.EvaluateOnBeforeResponse(packs, "Some plain text", "q", "a", 1);

        Assert.StartsWith("Format as a table:", result.ModifiedText);
        Assert.Single(result.TriggeredRules);
    }

    [Fact]
    public void EvaluateOnBeforeResponse_FormatEnforce_PassesWhenMatching()
    {
        var packs = new List<ResolvedRulePack>
        {
            MakePack(1, "Format", 100,
                Rule("OnBeforeResponse", "format_enforce", 1, pattern: @"^\|"))
        };

        var result = _engine.EvaluateOnBeforeResponse(packs, "| Column | Data |\n|---|---|", "q", "a", 1);

        Assert.Empty(result.TriggeredRules);
    }

    // ── tool_transform ────────────────────────────────────────────────────────

    [Fact]
    public void EvaluateOnInit_ToolTransform_InjectsWhenQueryMatches()
    {
        var packs = new List<ResolvedRulePack>
        {
            MakePack(1, "Transform", 100,
                Rule("OnInit", "tool_transform", 1,
                    pattern: "(?i)chart|graph",
                    instruction: "When calling visualization tools, always use bar chart format."))
        };

        var result = _engine.EvaluateOnInit(packs, "base", "Show me a chart of sales", "a", 1);

        Assert.Contains("bar chart format", result.ModifiedText);
    }

    // ── StopOnMatch ───────────────────────────────────────────────────────────

    [Fact]
    public void EvaluateOnInit_StopOnMatch_StopsProcessingAfterTrigger()
    {
        var packs = new List<ResolvedRulePack>
        {
            MakePack(1, "Multi", 100,
                Rule("OnInit", "inject_prompt", 1, instruction: "First rule", stopOnMatch: true),
                Rule("OnInit", "inject_prompt", 2, instruction: "Second rule"))
        };

        var result = _engine.EvaluateOnInit(packs, "", "q", "a", 1);

        Assert.Contains("First rule", result.ModifiedText);
        Assert.DoesNotContain("Second rule", result.ModifiedText);
        Assert.Single(result.TriggeredRules);
    }

    // ── Multiple packs with priority ──────────────────────────────────────────

    [Fact]
    public void EvaluateOnBeforeResponse_MultiplePacksExecuteInPriorityOrder()
    {
        var packs = new List<ResolvedRulePack>
        {
            MakePack(1, "Low Priority", 200,
                Rule("OnBeforeResponse", "append_text", 1, instruction: "Appended by pack 2")),
            MakePack(2, "High Priority", 100,
                Rule("OnBeforeResponse", "regex_redact", 1, pattern: @"\bsecret\b", replacement: "[X]")),
        };

        // High priority pack (100) redacts, low priority (200) appends
        // But packs list is already ordered by priority at resolve time—
        // we pass them in the order they'll be evaluated
        var ordered = packs.OrderBy(p => p.Priority).ToList();
        var result = _engine.EvaluateOnBeforeResponse(ordered, "The secret is here.", "q", "a", 1);

        Assert.Contains("[X]", result.ModifiedText);
        Assert.Contains("Appended by pack 2", result.ModifiedText);
        Assert.Equal(2, result.TriggeredRules.Count);
    }

    // ── DryRun ────────────────────────────────────────────────────────────────

    [Fact]
    public void DryRun_EvaluatesAllRulesWithoutSideEffects()
    {
        var pack = new HookRulePackEntity
        {
            Id = 1, TenantId = 1, Name = "Test", Version = "1.0", Priority = 100,
            IsEnabled = true, MaxEvaluationMs = 5000
        };
        pack.Rules.Add(Rule("OnInit", "inject_prompt", 1, instruction: "Context injection"));
        pack.Rules.Add(Rule("OnBeforeResponse", "regex_redact", 2, pattern: @"\d{4}", replacement: "****"));

        var result = _engine.DryRun(pack, "Show me recent data", "Revenue in 2025 was $1M");

        Assert.Contains("Context injection", result.ModifiedPrompt);
        Assert.Contains("****", result.ModifiedResponse);
        Assert.DoesNotContain("2025", result.ModifiedResponse);
        Assert.False(result.Blocked);
        Assert.Equal(2, result.TriggeredRules.Count);
    }

    [Fact]
    public void DryRun_DetectsBlock()
    {
        var pack = new HookRulePackEntity
        {
            Id = 1, TenantId = 1, Name = "Blocker", Version = "1.0", Priority = 100,
            IsEnabled = true, MaxEvaluationMs = 5000
        };
        pack.Rules.Add(Rule("OnBeforeResponse", "block_pattern", 1, pattern: "(?i)confidential"));

        var result = _engine.DryRun(pack, "query", "This is confidential information.");

        Assert.True(result.Blocked);
    }

    // ── ResolvePacksAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task ResolvePacksAsync_LoadsTenantPacks()
    {
        var pack = new HookRulePackEntity
        {
            TenantId = 1, Name = "Active Pack", Version = "1.0", Priority = 100,
            IsEnabled = true, MaxEvaluationMs = 500
        };
        pack.Rules.Add(new HookRuleEntity
        {
            HookPoint = "OnInit", RuleType = "inject_prompt",
            Instruction = "test", OrderInPack = 1, IsEnabled = true,
        });
        _db.RulePacks.Add(pack);
        await _db.SaveChangesAsync();

        var resolved = await _engine.ResolvePacksAsync(1, "general", "some query", CancellationToken.None);

        Assert.Single(resolved);
        Assert.Equal("Active Pack", resolved[0].Name);
        Assert.Single(resolved[0].Rules);
    }

    [Fact]
    public async Task ResolvePacksAsync_FiltersOutDisabledPacks()
    {
        _db.RulePacks.AddRange(
            new HookRulePackEntity { TenantId = 1, Name = "Enabled", Version = "1.0", Priority = 100, IsEnabled = true, MaxEvaluationMs = 500 },
            new HookRulePackEntity { TenantId = 1, Name = "Disabled", Version = "1.0", Priority = 200, IsEnabled = false, MaxEvaluationMs = 500 }
        );
        await _db.SaveChangesAsync();

        var resolved = await _engine.ResolvePacksAsync(1, "general", "query", CancellationToken.None);

        Assert.Single(resolved);
        Assert.Equal("Enabled", resolved[0].Name);
    }

    [Fact]
    public async Task ResolvePacksAsync_FiltersDisabledRules()
    {
        var pack = new HookRulePackEntity
        {
            TenantId = 1, Name = "Mixed", Version = "1.0", Priority = 100,
            IsEnabled = true, MaxEvaluationMs = 500
        };
        pack.Rules.Add(new HookRuleEntity { HookPoint = "OnInit", RuleType = "inject_prompt", Instruction = "Active", OrderInPack = 1, IsEnabled = true });
        pack.Rules.Add(new HookRuleEntity { HookPoint = "OnInit", RuleType = "inject_prompt", Instruction = "Inactive", OrderInPack = 2, IsEnabled = false });
        _db.RulePacks.Add(pack);
        await _db.SaveChangesAsync();

        var resolved = await _engine.ResolvePacksAsync(1, "general", "query", CancellationToken.None);

        Assert.Single(resolved);
        Assert.Single(resolved[0].Rules);
        Assert.Equal("Active", resolved[0].Rules[0].Instruction);
    }

    [Fact]
    public async Task ResolvePacksAsync_FiltersbyAppliesToArchetype()
    {
        _db.RulePacks.AddRange(
            new HookRulePackEntity { TenantId = 1, Name = "Golf Only", Version = "1.0", Priority = 100, IsEnabled = true, MaxEvaluationMs = 500, AppliesToJson = "[\"golf-ops\"]" },
            new HookRulePackEntity { TenantId = 1, Name = "All", Version = "1.0", Priority = 200, IsEnabled = true, MaxEvaluationMs = 500 }
        );
        await _db.SaveChangesAsync();

        var resolved = await _engine.ResolvePacksAsync(1, "general", "query", CancellationToken.None);

        Assert.Single(resolved);
        Assert.Equal("All", resolved[0].Name);
    }

    [Fact]
    public async Task ResolvePacksAsync_FiltersbyActivationCondition()
    {
        _db.RulePacks.AddRange(
            new HookRulePackEntity { TenantId = 1, Name = "Revenue Only", Version = "1.0", Priority = 100, IsEnabled = true, MaxEvaluationMs = 500, ActivationCondition = "revenue" },
            new HookRulePackEntity { TenantId = 1, Name = "Always", Version = "1.0", Priority = 200, IsEnabled = true, MaxEvaluationMs = 500 }
        );
        await _db.SaveChangesAsync();

        var resolved = await _engine.ResolvePacksAsync(1, "general", "show me booking data", CancellationToken.None);

        Assert.Single(resolved);
        Assert.Equal("Always", resolved[0].Name);
    }

    // ── Regex timeout resilience ──────────────────────────────────────────────

    [Fact]
    public void EvaluateOnBeforeResponse_InvalidRegex_DoesNotThrow()
    {
        var packs = new List<ResolvedRulePack>
        {
            MakePack(1, "Bad", 100,
                Rule("OnBeforeResponse", "regex_redact", 1, pattern: @"[invalid"))
        };

        // Should not throw — regex errors are caught internally
        var result = _engine.EvaluateOnBeforeResponse(packs, "test text", "q", "a", 1);

        Assert.False(result.Blocked);
    }

    // ── model_switch MatchTarget ──────────────────────────────────────────────

    private static HookRuleEntity ModelSwitchRule(
        string? pattern, string? instruction, string? toolName, string matchTarget) =>
        new()
        {
            Id = 1,
            HookPoint = "OnBeforeIteration",
            RuleType = "model_switch",
            Pattern = pattern,
            Instruction = instruction,
            ToolName = toolName,
            MatchTarget = matchTarget,
            IsEnabled = true,
            MaxEvaluationMs = 1000,
        };

    [Fact]
    public void ModelSwitch_MatchTarget_Query_MatchesUserQuery()
    {
        var packs = new List<ResolvedRulePack> { MakePack(1, "P", 100,
            ModelSwitchRule(pattern: "send.*email", instruction: "claude-opus-4-6",
                toolName: null, matchTarget: "query")) };

        // Query matches → switch fires
        var hit = _engine.EvaluateOnBeforeIteration(packs, "sys", "please send the email", "a", 1);
        Assert.NotNull(hit.ModelSwitchRequest);

        // Query doesn't match → no switch
        var miss = _engine.EvaluateOnBeforeIteration(packs, "sys", "summarize the document", "a", 1);
        Assert.Null(miss.ModelSwitchRequest);
    }

    [Fact]
    public void ModelSwitch_MatchTarget_Response_MatchesPreviousIterationResponse()
    {
        var packs = new List<ResolvedRulePack> { MakePack(1, "P", 100,
            ModelSwitchRule(pattern: "I will send the email", instruction: "claude-opus-4-6",
                toolName: null, matchTarget: "response")) };

        // Response matches → switch fires
        var hit = _engine.EvaluateOnBeforeIteration(
            packs, "sys", "do the task", "a", 1,
            lastIterationResponse: "I will send the email now via the tool.");
        Assert.NotNull(hit.ModelSwitchRequest);
        Assert.Equal("claude-opus-4-6", hit.ModelSwitchRequest!.ModelId);

        // Same query but response doesn't match → no switch
        var miss = _engine.EvaluateOnBeforeIteration(
            packs, "sys", "do the task", "a", 1,
            lastIterationResponse: "I retrieved the calendar events.");
        Assert.Null(miss.ModelSwitchRequest);
    }

    [Fact]
    public void ModelSwitch_MatchTarget_Response_SkipsOnFirstIteration_WhenResponseEmpty()
    {
        var packs = new List<ResolvedRulePack> { MakePack(1, "P", 100,
            ModelSwitchRule(pattern: "send.*email", instruction: "claude-opus-4-6",
                toolName: null, matchTarget: "response")) };

        // No last response (first iteration) → rule skipped, no switch
        var result = _engine.EvaluateOnBeforeIteration(packs, "sys", "query", "a", 1,
            lastIterationResponse: "");
        Assert.Null(result.ModelSwitchRequest);
    }

    [Fact]
    public void ModelSwitch_MatchTarget_Response_BlankPattern_AlwaysFires_WhenResponsePresent()
    {
        var packs = new List<ResolvedRulePack> { MakePack(1, "P", 100,
            ModelSwitchRule(pattern: null, instruction: "claude-haiku-4-5-20251001",
                toolName: null, matchTarget: "response")) };

        // Blank pattern + response present → always fires
        var result = _engine.EvaluateOnBeforeIteration(
            packs, "sys", "q", "a", 1,
            lastIterationResponse: "I have retrieved the data.");
        Assert.NotNull(result.ModelSwitchRequest);
        Assert.Equal("claude-haiku-4-5-20251001", result.ModelSwitchRequest!.ModelId);
    }
}
