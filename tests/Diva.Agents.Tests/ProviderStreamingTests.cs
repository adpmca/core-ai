using Anthropic.SDK.Common;
using Anthropic.SDK.Messaging;
using Diva.Agents.Tests.Helpers;
using Diva.Infrastructure.LiteLLM;
using NSubstitute;
using System.Runtime.CompilerServices;

namespace Diva.Agents.Tests;

/// <summary>
/// Unit tests for AnthropicProviderStrategy streaming path:
///   - Parallel tool call extraction (dedup by ID across all stream events)
///   - Empty/null tool arguments → "{}"
///   - Token usage extraction from streaming events (including cache stats)
///   - Token usage from non-streaming CallLlmAsync
///   - LastTokenUsage updates between calls
/// </summary>
public class ProviderStreamingTests
{
    // ── Parallel tool calls ───────────────────────────────────────────────────

    /// <summary>
    /// The Anthropic SDK sets ToolCalls on the message_delta event but leaves ToolCalls=null
    /// on the final message_stop event. Verify that scanning ALL outputs captures both tools.
    /// </summary>
    [Fact]
    public async Task StreamLlmAsync_ParallelToolCalls_BothCaptured()
    {
        var funcA = new Function("get_scores", "", (System.Text.Json.Nodes.JsonNode?)null) { Id = "call-a" };
        var funcB = new Function("get_handicap", "", (System.Text.Json.Nodes.JsonNode?)null) { Id = "call-b" };

        var events = new[]
        {
            MakeStartEvent(50),                                       // message_start
            MakeToolDeltaEvent("tool_use", funcA, funcB),             // message_delta — has both tools
            MakeStopEvent()                                           // message_stop — ToolCalls=null
        };

        var strategy = BuildStrategy(events);
        strategy.Initialize("sys", [], "query", []);

        var result = await ConsumeStreamAsync(strategy);

        Assert.Equal(2, result.ToolCalls.Count);
        Assert.Contains(result.ToolCalls, tc => tc.Id == "call-a" && tc.Name == "get_scores");
        Assert.Contains(result.ToolCalls, tc => tc.Id == "call-b" && tc.Name == "get_handicap");
    }

    /// <summary>
    /// If the same tool ID appears in multiple stream events (SDK repeats it), deduplicate.
    /// </summary>
    [Fact]
    public async Task StreamLlmAsync_DuplicateToolIdAcrossEvents_DeduplicatedToOne()
    {
        var func = new Function("run_query", "", (System.Text.Json.Nodes.JsonNode?)null) { Id = "call-x" };

        var events = new[]
        {
            MakeStartEvent(30),
            MakeToolDeltaEvent("tool_use", func),   // first occurrence
            MakeToolDeltaEvent("tool_use", func),   // duplicate — same ID
            MakeStopEvent()
        };

        var strategy = BuildStrategy(events);
        strategy.Initialize("sys", [], "query", []);

        var result = await ConsumeStreamAsync(strategy);

        Assert.Single(result.ToolCalls);
        Assert.Equal("call-x", result.ToolCalls[0].Id);
    }

    /// <summary>
    /// A tool with no arguments must produce InputJson = "{}" not "" or null.
    /// The SDK returns an empty string for Arguments when the tool takes no params.
    /// </summary>
    [Fact]
    public async Task StreamLlmAsync_NullArguments_InputJsonIsEmptyObject()
    {
        var func = new Function("get_time", "", (System.Text.Json.Nodes.JsonNode?)null) { Id = "call-z" };

        var events = new[]
        {
            MakeStartEvent(20),
            MakeToolDeltaEvent("tool_use", func),
            MakeStopEvent()
        };

        var strategy = BuildStrategy(events);
        strategy.Initialize("sys", [], "query", []);

        var result = await ConsumeStreamAsync(strategy);

        Assert.Single(result.ToolCalls);
        Assert.Equal("{}", result.ToolCalls[0].InputJson);
    }

    // ── Token usage — streaming ───────────────────────────────────────────────

    [Fact]
    public async Task StreamLlmAsync_TokenUsage_ExtractsAllFields()
    {
        var events = new[]
        {
            MakeStartEventWithCache(100, cacheRead: 500, cacheCreate: 20),
            MakeDeltaText("hello"),
            MakeStopEvent(outputTokens: 42)
        };

        var strategy = BuildStrategy(events);
        strategy.Initialize("sys", [], "query", []);

        await ConsumeStreamAsync(strategy);

        var u = strategy.LastTokenUsage;
        Assert.Equal(100,  u.Input);
        Assert.Equal(42,   u.Output);
        Assert.Equal(500,  u.CacheRead);
        Assert.Equal(20,   u.CacheCreation);
        Assert.Equal(620,  u.TotalEffectiveInput); // 100 + 500 + 20
    }

    // ── Token usage — non-streaming CallLlmAsync ──────────────────────────────

    [Fact]
    public async Task CallLlmAsync_TokenUsage_ExtractsAllFields()
    {
        var anthropic = Substitute.For<IAnthropicProvider>();
        anthropic.GetClaudeMessageAsync(
                Arg.Any<MessageParameters>(), Arg.Any<CancellationToken>(), Arg.Any<string?>())
            .Returns(new MessageResponse
            {
                Content    = [new Anthropic.SDK.Messaging.TextContent { Text = "result" }],
                StopReason = "end_turn",
                Usage      = new Usage
                {
                    InputTokens              = 200,
                    OutputTokens             = 75,
                    CacheReadInputTokens     = 1000,
                    CacheCreationInputTokens = 0
                }
            });

        var strategy = new AnthropicProviderStrategy(
            anthropic, ContextWindowTestHelpers.NoOpCtx(),
            "claude-sonnet-4-6", 4096, "sys", "", NoOpRetry());

        strategy.Initialize("sys", [], "query", []);
        await strategy.CallLlmAsync(CancellationToken.None);

        var u = strategy.LastTokenUsage;
        Assert.Equal(200,  u.Input);
        Assert.Equal(75,   u.Output);
        Assert.Equal(1000, u.CacheRead);
        Assert.Equal(0,    u.CacheCreation);
        Assert.Equal(1200, u.TotalEffectiveInput); // 200 + 1000 + 0
    }

    // ── LastTokenUsage updates between calls ──────────────────────────────────

    [Fact]
    public async Task CallLlmAsync_CalledTwice_LastTokenUsageReflectsSecondCall()
    {
        var anthropic = Substitute.For<IAnthropicProvider>();
        anthropic.GetClaudeMessageAsync(
                Arg.Any<MessageParameters>(), Arg.Any<CancellationToken>(), Arg.Any<string?>())
            .Returns(
                new MessageResponse
                {
                    Content    = [new Anthropic.SDK.Messaging.TextContent { Text = "first" }],
                    StopReason = "end_turn",
                    Usage      = new Usage { InputTokens = 100, OutputTokens = 50 }
                },
                new MessageResponse
                {
                    Content    = [new Anthropic.SDK.Messaging.TextContent { Text = "second" }],
                    StopReason = "end_turn",
                    Usage      = new Usage { InputTokens = 300, OutputTokens = 80 }
                });

        var strategy = new AnthropicProviderStrategy(
            anthropic, ContextWindowTestHelpers.NoOpCtx(),
            "claude-sonnet-4-6", 4096, "sys", "", NoOpRetry());

        strategy.Initialize("sys", [], "query", []);
        await strategy.CallLlmAsync(CancellationToken.None);
        strategy.CommitAssistantResponse();
        strategy.AddUserMessage("follow-up");
        await strategy.CallLlmAsync(CancellationToken.None);

        var u = strategy.LastTokenUsage;
        Assert.Equal(300, u.Input);
        Assert.Equal(80,  u.Output);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static AnthropicProviderStrategy BuildStrategy(MessageResponse[] events)
    {
        var anthropic = Substitute.For<IAnthropicProvider>();
        anthropic.StreamClaudeMessageAsync(
                Arg.Any<MessageParameters>(), Arg.Any<CancellationToken>(), Arg.Any<string?>())
            .Returns(StreamOf(events));
        return new AnthropicProviderStrategy(
            anthropic, ContextWindowTestHelpers.NoOpCtx(),
            "claude-sonnet-4-6", 4096, "sys", "", NoOpRetry());
    }

    private static async Task<UnifiedLlmResponse> ConsumeStreamAsync(AnthropicProviderStrategy strategy)
    {
        UnifiedLlmResponse? result = null;
        await foreach (var delta in strategy.StreamLlmAsync(CancellationToken.None))
        {
            if (delta.IsDone) result = delta.Final;
        }
        return result!;
    }

    private static async IAsyncEnumerable<MessageResponse> StreamOf(
        MessageResponse[] items,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            yield return item;
        }
    }

    private static MessageResponse MakeStartEvent(int inputTokens) => new()
    {
        StreamStartMessage = new StreamMessage
        {
            Usage = new Usage { InputTokens = inputTokens }
        }
    };

    private static MessageResponse MakeStartEventWithCache(
        int inputTokens, int cacheRead, int cacheCreate) => new()
    {
        StreamStartMessage = new StreamMessage
        {
            Usage = new Usage
            {
                InputTokens              = inputTokens,
                CacheReadInputTokens     = cacheRead,
                CacheCreationInputTokens = cacheCreate
            }
        }
    };

    private static MessageResponse MakeToolDeltaEvent(string stopReason, params Function[] funcs) => new()
    {
        StopReason = stopReason,
        ToolCalls  = [.. funcs]
    };

    private static MessageResponse MakeDeltaText(string text) => new()
    {
        Delta = new Delta { Text = text }
    };

    private static MessageResponse MakeStopEvent(int outputTokens = 0) => new()
    {
        StopReason = "tool_use",
        ToolCalls  = null,   // mirrors real SDK — message_stop has no tool calls
        Usage      = new Usage { OutputTokens = outputTokens }
    };

    private static Func<Func<Task<MessageResponse>>, CancellationToken, Task<MessageResponse>> NoOpRetry()
        => (fn, ct) => fn();
}
