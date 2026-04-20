using Diva.Core.Models;
using Diva.Infrastructure.Data.Entities;
using Diva.Infrastructure.Learning;
using Diva.TenantAdmin.Prompts;
using Diva.TenantAdmin.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Diva.TenantAdmin.Tests;

public class PromptBuilderTests
{
    private static TenantContext Tenant(int id = 1, string? sessionId = null)
        => new() { TenantId = id, TenantName = "Test", UserId = "u1", SessionId = sessionId };

    private static TenantAwarePromptBuilder Build(
        ITenantBusinessRulesService? rules = null,
        ISessionRuleManager? session = null,
        ITenantGroupService? groups = null)
    {
        var r = rules   ?? DefaultRulesService();
        var s = session ?? DefaultSessionManager();
        var g = groups  ?? DefaultGroupService();
        return new TenantAwarePromptBuilder(r, s, g, NullLogger<TenantAwarePromptBuilder>.Instance);
    }

    private static ITenantGroupService DefaultGroupService()
    {
        var svc = Substitute.For<ITenantGroupService>();
        svc.GetActiveRulesForTenantAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<Diva.Infrastructure.Data.Entities.GroupBusinessRuleEntity>());
        svc.GetActiveOverridesForTenantAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<Diva.Infrastructure.Data.Entities.GroupPromptOverrideEntity>());
        return svc;
    }

    private static ITenantBusinessRulesService DefaultRulesService()
    {
        var svc = Substitute.For<ITenantBusinessRulesService>();
        svc.GetPromptInjectionsAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("");
        svc.GetPromptOverridesAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<TenantPromptOverrideEntity>());
        return svc;
    }

    private static ISessionRuleManager DefaultSessionManager()
    {
        var mgr = Substitute.For<ISessionRuleManager>();
        mgr.GetSessionRulesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<SuggestedRule>());
        return mgr;
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_NoRules_ReturnsBasePromptUnchanged()
    {
        var builder = Build();
        var result  = await builder.BuildAsync("Base prompt.", "generic", Tenant(), default);
        Assert.Equal("Base prompt.", result);
    }

    [Fact]
    public async Task BuildAsync_BusinessRulesNotInjectedDirectly_FlowsViaHooks()
    {
        // Business rules are now injected via TenantRulePackHook/RulePackEngine, NOT by PromptBuilder.
        // This test verifies GetPromptInjectionsAsync is never called during BuildAsync.
        var rules = Substitute.For<ITenantBusinessRulesService>();
        rules.GetPromptOverridesAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(new List<TenantPromptOverrideEntity>());

        var builder = Build(rules);
        await builder.BuildAsync("Base.", "Analytics", Tenant(), default);

        await rules.DidNotReceive().GetPromptInjectionsAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuildAsync_SessionIdPresent_AppendsSessionRules()
    {
        var sessionMgr = Substitute.For<ISessionRuleManager>();
        sessionMgr.GetSessionRulesAsync("sess-1", Arg.Any<CancellationToken>())
                  .Returns([new SuggestedRule { PromptInjection = "Always respond in Spanish" }]);

        var builder = Build(session: sessionMgr);
        var result  = await builder.BuildAsync("Base.", "generic", Tenant(sessionId: "sess-1"), default);

        Assert.Contains("Session Rules", result);
        Assert.Contains("Always respond in Spanish", result);
    }

    [Fact]
    public async Task BuildAsync_NoSessionId_SessionRulesNotFetched()
    {
        var sessionMgr = Substitute.For<ISessionRuleManager>();
        var builder    = Build(session: sessionMgr);

        await builder.BuildAsync("Base.", "generic", Tenant(sessionId: null), default);

        await sessionMgr.DidNotReceive()
            .GetSessionRulesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuildAsync_Override_Replace_ReplacesEntireBasePrompt()
    {
        var rules = Substitute.For<ITenantBusinessRulesService>();
        rules.GetPromptInjectionsAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns("");
        rules.GetPromptOverridesAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns([new TenantPromptOverrideEntity
             {
                 MergeMode  = "Replace",
                 CustomText = "REPLACEMENT PROMPT",
                 Version    = 1
             }]);

        var builder = Build(rules);
        var result  = await builder.BuildAsync("Original base.", "generic", Tenant(), default);

        Assert.Equal("REPLACEMENT PROMPT", result.Trim());
        Assert.DoesNotContain("Original base.", result);
    }

    [Fact]
    public async Task BuildAsync_Override_Prepend_PrependsBefore()
    {
        var rules = Substitute.For<ITenantBusinessRulesService>();
        rules.GetPromptInjectionsAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns("");
        rules.GetPromptOverridesAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns([new TenantPromptOverrideEntity
             {
                 MergeMode  = "Prepend",
                 CustomText = "HEADER",
                 Version    = 1
             }]);

        var builder = Build(rules);
        var result  = await builder.BuildAsync("Base.", "generic", Tenant(), default);

        Assert.StartsWith("HEADER", result);
        Assert.Contains("Base.", result);
    }

    [Fact]
    public async Task BuildAsync_Override_Append_AppendsAfter()
    {
        var rules = Substitute.For<ITenantBusinessRulesService>();
        rules.GetPromptInjectionsAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns("");
        rules.GetPromptOverridesAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns([new TenantPromptOverrideEntity
             {
                 MergeMode  = "Append",
                 CustomText = "FOOTER",
                 Version    = 1
             }]);

        var builder = Build(rules);
        var result  = await builder.BuildAsync("Base.", "generic", Tenant(), default);

        Assert.Contains("Base.", result);
        Assert.EndsWith("FOOTER", result.Trim());
    }

    // ── Variable substitution ────────────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_BuiltInVariable_ResolvedInBasePrompt()
    {
        var builder = Build();
        var result  = await builder.BuildAsync(
            "Today is {{current_date}}.", "generic", Tenant(), default);

        Assert.DoesNotContain("{{current_date}}", result);
        Assert.Contains(DateTime.UtcNow.ToString("yyyy-MM-dd"), result);
    }

    [Fact]
    public async Task BuildAsync_CustomVariable_ResolvedFromJson()
    {
        var builder = Build();
        var result  = await builder.BuildAsync(
            "You work for {{company_name}}.", "generic", Tenant(), default,
            customVariablesJson: """{"company_name":"Acme Corp"}""");

        Assert.Equal("You work for Acme Corp.", result);
    }

    [Fact]
    public async Task BuildAsync_CustomVariablesJson_ResolvedInBasePrompt()
    {
        // Custom variables are resolved in the base prompt; business rules no longer flow here.
        var builder = Build();
        var result  = await builder.BuildAsync(
            "Greet users as {{greeting}}", "generic", Tenant(), default,
            customVariablesJson: """{"greeting":"Good day"}""");

        Assert.Contains("Greet users as Good day", result);
        Assert.DoesNotContain("{{greeting}}", result);
    }

    [Fact]
    public async Task BuildAsync_NullCustomVariablesJson_BuiltInsStillResolved()
    {
        var builder = Build();
        var result  = await builder.BuildAsync(
            "Time: {{current_time}}", "generic", Tenant(), default,
            customVariablesJson: null);

        Assert.DoesNotContain("{{current_time}}", result);
        Assert.Contains("UTC", result);
    }

    [Fact]
    public async Task BuildAsync_UnknownVariable_LeftAsIs()
    {
        var builder = Build();
        var result  = await builder.BuildAsync(
            "Hello {{unknown}}.", "generic", Tenant(), default);

        Assert.Equal("Hello {{unknown}}.", result);
    }

    [Fact]
    public async Task BuildAsync_MultipleOverrides_AppliedInVersionOrder()
    {
        var rules = Substitute.For<ITenantBusinessRulesService>();
        rules.GetPromptInjectionsAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns("");
        rules.GetPromptOverridesAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns([
                 new TenantPromptOverrideEntity { MergeMode = "Append", CustomText = "V2", Version = 2 },
                 new TenantPromptOverrideEntity { MergeMode = "Append", CustomText = "V1", Version = 1 }
             ]);

        var builder = Build(rules);
        var result  = await builder.BuildAsync("Base.", "generic", Tenant(), default);

        var v1Pos = result.IndexOf("V1", StringComparison.Ordinal);
        var v2Pos = result.IndexOf("V2", StringComparison.Ordinal);
        Assert.True(v1Pos < v2Pos, "V1 override (lower version) should be applied before V2");
    }
}
