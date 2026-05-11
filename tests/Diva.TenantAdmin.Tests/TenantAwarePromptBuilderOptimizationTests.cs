using Diva.Core.Configuration;
using Diva.Core.Models;
using Diva.Infrastructure.Data.Entities;
using Diva.Infrastructure.Learning;
using Diva.Infrastructure.Optimization;
using Diva.TenantAdmin.Prompts;
using Diva.TenantAdmin.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Diva.TenantAdmin.Tests;

/// <summary>
/// Tests for the Phase 24 few-shot example injection in TenantAwarePromptBuilder.
/// All other prompt-builder behavior is covered by PromptBuilderTests.cs.
/// </summary>
public class TenantAwarePromptBuilderOptimizationTests
{
    private static TenantContext Tenant(int id = 1)
        => new() { TenantId = id, TenantName = "Test", UserId = "u1" };

    private static ITenantBusinessRulesService EmptyRulesService()
    {
        var svc = Substitute.For<ITenantBusinessRulesService>();
        svc.GetPromptInjectionsAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("");
        svc.GetPromptOverridesAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<string?>())
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

    private static ITenantGroupService EmptyGroupService()
    {
        var svc = Substitute.For<ITenantGroupService>();
        svc.GetActiveRulesForTenantAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<GroupBusinessRuleEntity>());
        svc.GetActiveOverridesForTenantAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<GroupPromptOverrideEntity>());
        return svc;
    }

    private static TenantAwarePromptBuilder Build(
        IAgentOptimizationService optimization,
        int maxFewShotExamples = 5)
        => new(
            EmptyRulesService(),
            EmptySessionManager(),
            EmptyGroupService(),
            optimization,
            Options.Create(new AgentOptions
            {
                Optimization = new OptimizationOptions
                {
                    MaxFewShotExamplesPerAgent = maxFewShotExamples
                }
            }),
            NullLogger<TenantAwarePromptBuilder>.Instance);

    private static FewShotExampleDto MakeExample(string userMsg, string assistantMsg, int sortOrder, bool isEnabled = true)
        => new()
        {
            UserMessage      = userMsg,
            AssistantMessage = assistantMsg,
            SortOrder        = sortOrder,
            IsEnabled        = isEnabled
        };

    // ── Few-shot injection ────────────────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_NoAgentId_DoesNotCallGetFewShotExamples()
    {
        var optSvc = Substitute.For<IAgentOptimizationService>();
        var builder = Build(optSvc);

        // agentId is null → no few-shot lookup
        await builder.BuildAsync("Base.", "generic", Tenant(), default, agentId: null);

        await optSvc.DidNotReceive()
            .GetFewShotExamplesAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuildAsync_NoExamples_PromptUnchanged()
    {
        var optSvc = Substitute.For<IAgentOptimizationService>();
        optSvc.GetFewShotExamplesAsync("agent-1", 1, Arg.Any<CancellationToken>())
              .Returns(new List<FewShotExampleDto>());

        var builder = Build(optSvc);
        var result  = await builder.BuildAsync("Base.", "generic", Tenant(), default, agentId: "agent-1");

        Assert.Equal("Base.", result);
        Assert.DoesNotContain("Response Examples", result);
    }

    [Fact]
    public async Task BuildAsync_WithEnabledExamples_AppendsResponseExamplesSection()
    {
        var optSvc = Substitute.For<IAgentOptimizationService>();
        optSvc.GetFewShotExamplesAsync("agent-1", 1, Arg.Any<CancellationToken>())
              .Returns(new List<FewShotExampleDto>
              {
                  MakeExample("What is 2+2?", "2+2 equals 4.", 0),
                  MakeExample("What color is the sky?", "The sky is blue.", 1)
              });

        var builder = Build(optSvc);
        var result  = await builder.BuildAsync("Base.", "generic", Tenant(), default, agentId: "agent-1");

        Assert.Contains("## Response Examples", result);
        Assert.Contains("What is 2+2?", result);
        Assert.Contains("2+2 equals 4.", result);
        Assert.Contains("What color is the sky?", result);
    }

    [Fact]
    public async Task BuildAsync_AllExamplesDisabled_DoesNotAppendSection()
    {
        var optSvc = Substitute.For<IAgentOptimizationService>();
        optSvc.GetFewShotExamplesAsync("agent-1", 1, Arg.Any<CancellationToken>())
              .Returns(new List<FewShotExampleDto>
              {
                  MakeExample("disabled q", "disabled a", 0, isEnabled: false)
              });

        var builder = Build(optSvc);
        var result  = await builder.BuildAsync("Base.", "generic", Tenant(), default, agentId: "agent-1");

        Assert.DoesNotContain("Response Examples", result);
        Assert.DoesNotContain("disabled q", result);
    }

    [Fact]
    public async Task BuildAsync_MaxFewShotExamples_CapsInjectedCount()
    {
        var optSvc = Substitute.For<IAgentOptimizationService>();
        optSvc.GetFewShotExamplesAsync("agent-1", 1, Arg.Any<CancellationToken>())
              .Returns(new List<FewShotExampleDto>
              {
                  MakeExample("q1", "a1", 0),
                  MakeExample("q2", "a2", 1),
                  MakeExample("q3", "a3", 2),
                  MakeExample("q4", "a4", 3),
                  MakeExample("q5", "a5", 4)
              });

        var builder = Build(optSvc, maxFewShotExamples: 2);
        var result  = await builder.BuildAsync("Base.", "generic", Tenant(), default, agentId: "agent-1");

        Assert.Contains("q1", result);
        Assert.Contains("q2", result);
        Assert.DoesNotContain("q3", result);
        Assert.DoesNotContain("q4", result);
        Assert.DoesNotContain("q5", result);
    }

    [Fact]
    public async Task BuildAsync_ExamplesRespectSortOrder()
    {
        var optSvc = Substitute.For<IAgentOptimizationService>();
        optSvc.GetFewShotExamplesAsync("agent-1", 1, Arg.Any<CancellationToken>())
              .Returns(new List<FewShotExampleDto>
              {
                  MakeExample("second question", "second answer", 1),
                  MakeExample("first question", "first answer", 0)
              });

        var builder = Build(optSvc);
        var result  = await builder.BuildAsync("Base.", "generic", Tenant(), default, agentId: "agent-1");

        var firstPos  = result.IndexOf("first question",  StringComparison.Ordinal);
        var secondPos = result.IndexOf("second question", StringComparison.Ordinal);

        Assert.True(firstPos < secondPos, "Example with SortOrder=0 must appear before SortOrder=1");
    }

    [Fact]
    public async Task BuildAsync_FewShotServiceThrows_DoesNotBubbleException()
    {
        var optSvc = Substitute.For<IAgentOptimizationService>();
        optSvc.GetFewShotExamplesAsync("agent-1", 1, Arg.Any<CancellationToken>())
              .Returns(Task.FromException<List<FewShotExampleDto>>(
                  new InvalidOperationException("DB unavailable")));

        var builder = Build(optSvc);

        // Must not throw — few-shot is best-effort
        var ex = await Record.ExceptionAsync(
            () => builder.BuildAsync("Base.", "generic", Tenant(), default, agentId: "agent-1"));

        Assert.Null(ex);
    }
}
