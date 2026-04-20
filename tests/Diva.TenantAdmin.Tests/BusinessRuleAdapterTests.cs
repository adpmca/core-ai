using Diva.Infrastructure.Data.Entities;
using Diva.TenantAdmin.Services;

namespace Diva.TenantAdmin.Tests;

/// <summary>
/// Unit tests for BusinessRuleAdapter — pure static mapping, no DB required.
/// </summary>
public class BusinessRuleAdapterTests
{
    // ── ToVirtualHookRule ─────────────────────────────────────────────────────

    [Fact]
    public void ToVirtualHookRule_InjectPrompt_HasNegativeIdAndBulletPrefix()
    {
        var br = new TenantBusinessRuleEntity
        {
            Id = 7, HookPoint = "OnInit", HookRuleType = "inject_prompt",
            PromptInjection = "Always respond in JSON.", OrderInPack = 3,
            StopOnMatch = false, MaxEvaluationMs = 100
        };

        var rule = BusinessRuleAdapter.ToVirtualHookRule(br);

        Assert.Equal(-7, rule.Id);
        Assert.Equal("OnInit", rule.HookPoint);
        Assert.Equal("inject_prompt", rule.RuleType);
        Assert.Equal("- Always respond in JSON.", rule.Instruction);   // Gap #1: bullet prefix
        Assert.Equal(3, rule.OrderInPack);
    }

    [Fact]
    public void ToVirtualHookRule_NonInjectPrompt_NoExtraPrefix()
    {
        var br = new TenantBusinessRuleEntity
        {
            Id = 3, HookPoint = "OnBeforeResponse", HookRuleType = "regex_redact",
            Pattern = @"\d{4}", MaxEvaluationMs = 50
        };

        var rule = BusinessRuleAdapter.ToVirtualHookRule(br);

        Assert.Equal(-3, rule.Id);
        Assert.Equal("regex_redact", rule.RuleType);
        Assert.Equal(@"\d{4}", rule.Pattern);
        Assert.Null(rule.Instruction);
    }

    [Fact]
    public void ToVirtualHookRule_MapsAllHookFields()
    {
        var br = new TenantBusinessRuleEntity
        {
            Id = 5, HookPoint = "OnAfterToolCall", HookRuleType = "tool_require",
            ToolName = "search", Pattern = "find", Replacement = "lookup",
            StopOnMatch = true, MaxEvaluationMs = 200, OrderInPack = 9
        };

        var rule = BusinessRuleAdapter.ToVirtualHookRule(br);

        Assert.Equal("search",    rule.ToolName);
        Assert.Equal("find",      rule.Pattern);
        Assert.Equal("lookup",    rule.Replacement);
        Assert.True(rule.StopOnMatch);
        Assert.Equal(200,         rule.MaxEvaluationMs);
        Assert.Equal(9,           rule.OrderInPack);
    }

    // ── WrapAsVirtualPack ─────────────────────────────────────────────────────

    [Fact]
    public void WrapAsVirtualPack_EmptyList_ReturnsEmptyPack()
    {
        var pack = BusinessRuleAdapter.WrapAsVirtualPack([]);
        Assert.Empty(pack.Rules);
    }

    [Fact]
    public void WrapAsVirtualPack_InjectPromptRules_IncludesSyntheticHeader()
    {
        var rules = new List<TenantBusinessRuleEntity>
        {
            new() { Id = 1, HookPoint = "OnInit", HookRuleType = "inject_prompt",
                    PromptInjection = "Rule A", OrderInPack = 0 },
            new() { Id = 2, HookPoint = "OnInit", HookRuleType = "inject_prompt",
                    PromptInjection = "Rule B", OrderInPack = 1 },
        };

        var pack = BusinessRuleAdapter.WrapAsVirtualPack(rules);

        // Header rule at OrderInPack=-1
        var header = pack.Rules.FirstOrDefault(r => r.OrderInPack == -1);
        Assert.NotNull(header);
        Assert.Equal("inject_prompt", header.RuleType);
        Assert.Contains("Business Rules", header.Instruction);
        Assert.Equal(3, pack.Rules.Count); // header + 2 virtual rules
    }

    [Fact]
    public void WrapAsVirtualPack_Priority_Is95()
    {
        var pack = BusinessRuleAdapter.WrapAsVirtualPack([
            new() { Id = 1, HookPoint = "OnInit", HookRuleType = "inject_prompt", PromptInjection = "x" }
        ]);

        Assert.Equal(95, pack.Priority);
    }

    // ── MergeIntoPackRules ────────────────────────────────────────────────────

    [Fact]
    public void MergeIntoPackRules_NativeRulesPreserved()
    {
        var native = new List<HookRuleEntity>
        {
            new() { Id = 10, OrderInPack = 0, RuleType = "inject_prompt", HookPoint = "OnInit" }
        };

        var result = BusinessRuleAdapter.MergeIntoPackRules(native, []);

        Assert.Single(result);
        Assert.Equal(10, result[0].Id);
    }

    [Fact]
    public void MergeIntoPackRules_LinkedRulesConverted_ToNegativeId()
    {
        var linked = new List<TenantBusinessRuleEntity>
        {
            new() { Id = 5, HookPoint = "OnInit", HookRuleType = "inject_prompt",
                    PromptInjection = "linked", OrderInPack = 99 }
        };

        var result = BusinessRuleAdapter.MergeIntoPackRules([], linked);

        Assert.Single(result);
        Assert.Equal(-5, result[0].Id);
        Assert.Equal(99, result[0].OrderInPack);
    }

    [Fact]
    public void MergeIntoPackRules_EqualOrderInPack_NativeBeforeVirtual()
    {
        var native = new List<HookRuleEntity>
        {
            new() { Id = 1, OrderInPack = 5, RuleType = "inject_prompt", HookPoint = "OnInit" }
        };
        var linked = new List<TenantBusinessRuleEntity>
        {
            new() { Id = 2, HookPoint = "OnInit", HookRuleType = "inject_prompt",
                    PromptInjection = "virtual", OrderInPack = 5 }
        };

        var result = BusinessRuleAdapter.MergeIntoPackRules(native, linked);

        Assert.Equal(2, result.Count);
        Assert.Equal(1,  result[0].Id);   // native (non-negative) first — Gap #6
        Assert.Equal(-2, result[1].Id);   // virtual after
    }

    [Fact]
    public void MergeIntoPackRules_SortedByOrderInPack()
    {
        var native = new List<HookRuleEntity>
        {
            new() { Id = 1, OrderInPack = 10, RuleType = "inject_prompt", HookPoint = "OnInit" },
            new() { Id = 2, OrderInPack = 5,  RuleType = "inject_prompt", HookPoint = "OnInit" },
        };

        var result = BusinessRuleAdapter.MergeIntoPackRules(native, []);

        Assert.Equal(5,  result[0].OrderInPack);
        Assert.Equal(10, result[1].OrderInPack);
    }
}
