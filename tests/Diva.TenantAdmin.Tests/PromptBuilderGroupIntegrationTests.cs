using Diva.Core.Models;
using Diva.Infrastructure.Data.Entities;
using Diva.Infrastructure.Learning;
using Diva.TenantAdmin.Prompts;
using Diva.TenantAdmin.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Diva.TenantAdmin.Tests;

/// <summary>
/// Tests for TenantAwarePromptBuilder with group rules and overrides injected.
/// </summary>
public class PromptBuilderGroupIntegrationTests
{
    private static TenantContext Tenant(int id = 1, string? sessionId = null)
        => new() { TenantId = id, TenantName = "Test", UserId = "u1", SessionId = sessionId };

    private static ITenantBusinessRulesService EmptyRulesService()
    {
        var svc = Substitute.For<ITenantBusinessRulesService>();
        svc.GetPromptInjectionsAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("");
        svc.GetPromptOverridesAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<TenantPromptOverrideEntity>());
        return svc;
    }

    private static ISessionRuleManager EmptySessionManager()
    {
        var mgr = Substitute.For<ISessionRuleManager>();
        mgr.GetSessionRulesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<SuggestedRule>());
        return mgr;
    }

    private static ITenantGroupService GroupServiceWith(
        List<GroupBusinessRuleEntity>? rules = null,
        List<GroupPromptOverrideEntity>? overrides = null)
    {
        var svc = Substitute.For<ITenantGroupService>();
        svc.GetActiveRulesForTenantAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(rules ?? []);
        svc.GetActiveOverridesForTenantAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(overrides ?? []);
        return svc;
    }

    private static TenantAwarePromptBuilder Build(
        ITenantBusinessRulesService? rules = null,
        ITenantGroupService? groups = null,
        ISessionRuleManager? session = null)
        => new(
            rules   ?? EmptyRulesService(),
            session ?? EmptySessionManager(),
            groups  ?? GroupServiceWith(),
            NullLogger<TenantAwarePromptBuilder>.Instance);

    // ── Group rules injected ──────────────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_GroupRulesInjected_AppearsInPrompt()
    {
        var groups = GroupServiceWith(rules:
        [
            new GroupBusinessRuleEntity { AgentType = "Analytics", RuleKey = "gr1", PromptInjection = "Use metric system", IsActive = true, Priority = 50 }
        ]);

        var builder = Build(groups: groups);
        var result  = await builder.BuildAsync("Base.", "Analytics", Tenant(), default);

        Assert.Contains("Group Rules", result);
        Assert.Contains("Use metric system", result);
    }

    [Fact]
    public async Task BuildAsync_NoGroupMembership_NoGroupRulesSection()
    {
        var builder = Build(groups: GroupServiceWith(rules: []));
        var result  = await builder.BuildAsync("Base.", "Analytics", Tenant(), default);

        Assert.DoesNotContain("Group Rules", result);
    }

    [Fact]
    public async Task BuildAsync_GroupRulesFlowViaGroupService_NotDirectlyFromTenantRules()
    {
        // Group-level business rules are injected by the group service path in PromptBuilder.
        // Tenant-level business rules now flow via TenantRulePackHook, not PromptBuilder.
        var tenantRules = Substitute.For<ITenantBusinessRulesService>();
        tenantRules.GetPromptOverridesAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns(new List<TenantPromptOverrideEntity>());

        var groups = GroupServiceWith(rules:
        [
            new GroupBusinessRuleEntity { PromptInjection = "Group rule", IsActive = true, Priority = 50 }
        ]);

        var builder = Build(rules: tenantRules, groups: groups);
        var result  = await builder.BuildAsync("Base.", "Analytics", Tenant(), default);

        // Group rule IS injected by PromptBuilder; tenant rules are not
        Assert.Contains("Group rule", result);
        await tenantRules.DidNotReceive().GetPromptInjectionsAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── Group overrides applied to base prompt ────────────────────────────────

    [Fact]
    public async Task BuildAsync_GroupOverrideAppend_AppendsToBasePrompt()
    {
        var groups = GroupServiceWith(overrides:
        [
            new GroupPromptOverrideEntity { AgentType = "Analytics", Section = "base", CustomText = "GROUP FOOTER", MergeMode = "Append", IsActive = true }
        ]);

        var builder = Build(groups: groups);
        var result  = await builder.BuildAsync("Base prompt.", "Analytics", Tenant(), default);

        Assert.Contains("Base prompt.", result);
        Assert.Contains("GROUP FOOTER", result);
    }

    [Fact]
    public async Task BuildAsync_GroupOverridePrepend_PrependsToBasePrompt()
    {
        var groups = GroupServiceWith(overrides:
        [
            new GroupPromptOverrideEntity { CustomText = "GROUP HEADER", MergeMode = "Prepend", IsActive = true }
        ]);

        var builder = Build(groups: groups);
        var result  = await builder.BuildAsync("Base.", "Analytics", Tenant(), default);

        Assert.StartsWith("GROUP HEADER", result);
        Assert.Contains("Base.", result);
    }

    // ── Tenant overrides win over group overrides ─────────────────────────────

    [Fact]
    public async Task BuildAsync_TenantOverrideReplace_WinsOverGroupAppend()
    {
        // Group appends something, but tenant replaces the whole prompt
        var tenantRules = Substitute.For<ITenantBusinessRulesService>();
        tenantRules.GetPromptInjectionsAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("");
        tenantRules.GetPromptOverridesAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns([new TenantPromptOverrideEntity { MergeMode = "Replace", CustomText = "TENANT WINS", Version = 1 }]);

        var groups = GroupServiceWith(overrides:
        [
            new GroupPromptOverrideEntity { CustomText = "Group addition", MergeMode = "Append", IsActive = true }
        ]);

        var builder = Build(rules: tenantRules, groups: groups);
        var result  = await builder.BuildAsync("Original base.", "Analytics", Tenant(), default);

        // Tenant Replace applied on top of group-modified prompt → replaces everything in the base prompt slot
        Assert.Contains("TENANT WINS", result);
        Assert.DoesNotContain("Original base.", result);
    }

    [Fact]
    public async Task BuildAsync_TenantOverrideAppend_AppliedAfterGroupAppend()
    {
        var tenantRules = Substitute.For<ITenantBusinessRulesService>();
        tenantRules.GetPromptInjectionsAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("");
        tenantRules.GetPromptOverridesAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns([new TenantPromptOverrideEntity { MergeMode = "Append", CustomText = "TENANT APPEND", Version = 1 }]);

        var groups = GroupServiceWith(overrides:
        [
            new GroupPromptOverrideEntity { CustomText = "GROUP APPEND", MergeMode = "Append", IsActive = true }
        ]);

        var builder = Build(rules: tenantRules, groups: groups);
        var result  = await builder.BuildAsync("Base.", "Analytics", Tenant(), default);

        var groupPos  = result.IndexOf("GROUP APPEND", StringComparison.Ordinal);
        var tenantPos = result.IndexOf("TENANT APPEND", StringComparison.Ordinal);
        Assert.True(groupPos < tenantPos, "Group override should be applied before tenant override");
    }
}
