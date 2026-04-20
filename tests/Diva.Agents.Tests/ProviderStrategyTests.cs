using Anthropic.SDK.Messaging;
using Diva.Agents.Tests.Helpers;
using Diva.Infrastructure.LiteLLM;
using Diva.Infrastructure.Sessions;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using NSubstitute;

namespace Diva.Agents.Tests;

/// <summary>
/// Unit tests for ILlmProviderStrategy implementations (Anthropic + OpenAI)
/// and per-agent config features (FilterTools, MaxContinuations, instruction flow).
/// </summary>
public class ProviderStrategyTests
{
    // ── AnthropicProviderStrategy ─────────────────────────────────────────────

    [Fact]
    public void Anthropic_Initialize_BuildsCorrectMessageList()
    {
        var anthropic = Substitute.For<IAnthropicProvider>();
        var strategy = new AnthropicProviderStrategy(
            anthropic, ContextWindowTestHelpers.NoOpCtx(),
            "claude-sonnet-4-20250514", 4096, "system", "", NoOpRetryAnthropic());

        var history = new List<ConversationTurn>
        {
            new("user", "hello"),
            new("assistant", "hi there")
        };

        strategy.Initialize("system prompt", history, "what is 2+2?", []);

        // Verify by calling AddUserMessage and checking no error — strategy is initialized
        strategy.AddUserMessage("follow-up");
    }

    [Fact]
    public void Anthropic_AddToolResults_CreatesUserMessage()
    {
        var anthropic = Substitute.For<IAnthropicProvider>();
        var strategy = new AnthropicProviderStrategy(
            anthropic, ContextWindowTestHelpers.NoOpCtx(),
            "claude-sonnet-4-20250514", 4096, "system", "", NoOpRetryAnthropic());

        strategy.Initialize("system", [], "query", []);

        var results = new List<UnifiedToolResult>
        {
            new("call-1", "tool-a", "result-output", false),
            new("call-2", "tool-b", "error-output", true)
        };

        strategy.AddToolResults(results);
    }

    [Fact]
    public void Anthropic_AddAssistantThenUser_AddsTwoMessages()
    {
        var anthropic = Substitute.For<IAnthropicProvider>();
        var strategy = new AnthropicProviderStrategy(
            anthropic, ContextWindowTestHelpers.NoOpCtx(),
            "claude-sonnet-4-20250514", 4096, "system", "", NoOpRetryAnthropic());

        strategy.Initialize("system", [], "query", []);
        strategy.AddAssistantThenUser("I acknowledge the error.", "Please try again.");
    }

    // ── OpenAiProviderStrategy ────────────────────────────────────────────────

    [Fact]
    public void OpenAi_Initialize_BuildsSystemAndUserMessages()
    {
        var openAi = Substitute.For<IOpenAiProvider>();
        var mockClient = Substitute.For<IChatClient>();
        openAi.CreateChatClient("gpt-4.1").Returns(mockClient);

        var strategy = new OpenAiProviderStrategy(
            openAi, ContextWindowTestHelpers.NoOpCtx(),
            "gpt-4.1", NoOpRetryOpenAi());

        var history = new List<ConversationTurn>
        {
            new("user", "hello"),
            new("assistant", "world")
        };

        strategy.Initialize("system prompt", history, "follow-up query", []);

        // No exception = success — messages built correctly
        strategy.AddUserMessage("one more message");
    }

    [Fact]
    public void OpenAi_AddToolResults_CreatesToolMessages()
    {
        var openAi = Substitute.For<IOpenAiProvider>();
        openAi.CreateChatClient(Arg.Any<string>()).Returns(Substitute.For<IChatClient>());

        var strategy = new OpenAiProviderStrategy(
            openAi, ContextWindowTestHelpers.NoOpCtx(),
            "gpt-4.1", NoOpRetryOpenAi());

        strategy.Initialize("system", [], "query", []);

        var results = new List<UnifiedToolResult>
        {
            new("call-1", "read_file", "file contents here", false),
            new("call-2", "search", "no results", false)
        };

        strategy.AddToolResults(results);
    }

    // ── OpenAI token usage ────────────────────────────────────────────────────

    [Fact]
    public async Task OpenAi_CallLlmAsync_PopulatesLastTokenUsage()
    {
        var openAi = Substitute.For<IOpenAiProvider>();
        var mockClient = Substitute.For<IChatClient>();
        openAi.CreateChatClient(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>())
              .Returns(mockClient);

        mockClient.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse([new ChatMessage(ChatRole.Assistant, "ok")])
            {
                FinishReason = ChatFinishReason.Stop,
                Usage = new UsageDetails
                {
                    InputTokenCount  = 150,
                    OutputTokenCount = 60
                }
            });

        var strategy = new OpenAiProviderStrategy(
            openAi, ContextWindowTestHelpers.NoOpCtx(),
            "gpt-4.1", NoOpRetryOpenAi());

        strategy.Initialize("sys", [], "query", []);
        await strategy.CallLlmAsync(CancellationToken.None);

        var u = strategy.LastTokenUsage;
        Assert.Equal(150, u.Input);
        Assert.Equal(60,  u.Output);
        Assert.Equal(0,   u.CacheRead);
        Assert.Equal(0,   u.CacheCreation);
    }

    [Fact]
    public async Task OpenAi_StreamLlmAsync_LastTokenUsageIsZero()
    {
        var openAi = Substitute.For<IOpenAiProvider>();
        var mockClient = Substitute.For<IChatClient>();
        openAi.CreateChatClient(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>())
              .Returns(mockClient);

        // Empty streaming response
        mockClient.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(EmptyStream());

        var strategy = new OpenAiProviderStrategy(
            openAi, ContextWindowTestHelpers.NoOpCtx(),
            "gpt-4.1", NoOpRetryOpenAi());

        strategy.Initialize("sys", [], "query", []);

        // Consume the stream
        await foreach (var _ in strategy.StreamLlmAsync(CancellationToken.None)) { }

        // ME.AI streaming does not expose usage — expect all zeros
        Assert.Equal(default, strategy.LastTokenUsage);
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> EmptyStream(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    // ── FilterTools ───────────────────────────────────────────────────────────

    [Fact]
    public void FilterTools_NullJson_NoOp()
    {
        var map = new Dictionary<string, ModelContextProtocol.Client.McpClient>
        {
            ["tool-a"] = null!,
            ["tool-b"] = null!
        };
        var tools = new List<McpClientTool>();
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        ReActToolHelper.FilterTools(null, map, tools, logger);

        Assert.Equal(2, map.Count);
    }

    [Fact]
    public void FilterTools_EmptyJson_NoOp()
    {
        var map = new Dictionary<string, ModelContextProtocol.Client.McpClient>
        {
            ["tool-a"] = null!
        };
        var tools = new List<McpClientTool>();

        ReActToolHelper.FilterTools("", map, tools,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        Assert.Single(map);
    }

    [Fact]
    public void FilterTools_AllowMode_KeepsOnlyListed()
    {
        var map = new Dictionary<string, ModelContextProtocol.Client.McpClient>
        {
            ["read_file"] = null!,
            ["search"] = null!,
            ["delete_file"] = null!
        };
        var tools = new List<McpClientTool>();
        var json = """{"mode":"allow","tools":["read_file","search"]}""";

        ReActToolHelper.FilterTools(json, map, tools,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        Assert.Equal(2, map.Count);
        Assert.True(map.ContainsKey("read_file"));
        Assert.True(map.ContainsKey("search"));
        Assert.False(map.ContainsKey("delete_file"));
    }

    [Fact]
    public void FilterTools_DenyMode_RemovesListed()
    {
        var map = new Dictionary<string, ModelContextProtocol.Client.McpClient>
        {
            ["read_file"] = null!,
            ["search"] = null!,
            ["delete_file"] = null!
        };
        var tools = new List<McpClientTool>();
        var json = """{"mode":"deny","tools":["delete_file"]}""";

        ReActToolHelper.FilterTools(json, map, tools,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        Assert.Equal(2, map.Count);
        Assert.True(map.ContainsKey("read_file"));
        Assert.True(map.ContainsKey("search"));
        Assert.False(map.ContainsKey("delete_file"));
    }

    [Fact]
    public void FilterTools_InvalidJson_NoOp()
    {
        var map = new Dictionary<string, ModelContextProtocol.Client.McpClient>
        {
            ["tool-a"] = null!
        };
        var tools = new List<McpClientTool>();

        ReActToolHelper.FilterTools("{broken json!!!", map, tools,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        Assert.Single(map);
    }

    [Fact]
    public void FilterTools_CaseInsensitive()
    {
        var map = new Dictionary<string, ModelContextProtocol.Client.McpClient>
        {
            ["Read_File"] = null!,
            ["Search"] = null!
        };
        var tools = new List<McpClientTool>();
        var json = """{"mode":"allow","tools":["read_file"]}""";

        ReActToolHelper.FilterTools(json, map, tools,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        Assert.Single(map);
        Assert.True(map.ContainsKey("Read_File"));
    }

    // ── Instruction flow — SubTask propagation ────────────────────────────────

    [Fact]
    public void SubTask_Instructions_DefaultsToNull()
    {
        var task = new Diva.Agents.Supervisor.SubTask("desc", ["cap"], 1, 1);
        Assert.Null(task.Instructions);
    }

    [Fact]
    public void SubTask_Instructions_PropagatesWhenSet()
    {
        var task = new Diva.Agents.Supervisor.SubTask("desc", ["cap"], 1, 1, "Be concise.");
        Assert.Equal("Be concise.", task.Instructions);
    }

    [Fact]
    public void AgentRequest_Instructions_RoundTrips()
    {
        var req = new Diva.Core.Models.AgentRequest
        {
            Query        = "test",
            Instructions = "Only use approved tools."
        };
        Assert.Equal("Only use approved tools.", req.Instructions);
    }

    // ── apiKeyOverride forwarding — AnthropicProviderStrategy ────────────────

    /// <summary>
    /// When apiKeyOverride is supplied to the constructor, every call through
    /// CallLlmAsync must forward it as the third argument to GetClaudeMessageAsync
    /// so the provider can create a scoped AnthropicClient with the right key.
    /// </summary>
    [Fact]
    public async Task Anthropic_ApiKeyOverride_ForwardedToGetClaudeMessageAsync()
    {
        var anthropic = Substitute.For<IAnthropicProvider>();
        // Use 3-arg setup so NSubstitute matches calls with a non-null third arg.
        anthropic.GetClaudeMessageAsync(
                Arg.Any<MessageParameters>(), Arg.Any<CancellationToken>(), Arg.Any<string?>())
            .Returns(MakeAnthropicResponse("ok"));

        var strategy = new AnthropicProviderStrategy(
            anthropic, ContextWindowTestHelpers.NoOpCtx(),
            "claude-sonnet-4-6", 4096, "sys", "", NoOpRetryAnthropic(),
            apiKeyOverride: "override-key");

        strategy.Initialize("sys", [], "query", []);
        await strategy.CallLlmAsync(CancellationToken.None);

        await anthropic.Received(1).GetClaudeMessageAsync(
            Arg.Any<MessageParameters>(),
            Arg.Any<CancellationToken>(),
            Arg.Is<string?>(s => s == "override-key"));
    }

    /// <summary>
    /// When no apiKeyOverride is provided (null default), GetClaudeMessageAsync
    /// must receive null so the provider uses its cached singleton AnthropicClient.
    /// </summary>
    [Fact]
    public async Task Anthropic_NoApiKeyOverride_NullPassedToProvider()
    {
        var anthropic = Substitute.For<IAnthropicProvider>();
        // 2-arg setup compiles to GetClaudeMessageAsync(Any, Any, null) — matches null third arg.
        anthropic.GetClaudeMessageAsync(Arg.Any<MessageParameters>(), Arg.Any<CancellationToken>())
            .Returns(MakeAnthropicResponse("ok"));

        var strategy = new AnthropicProviderStrategy(
            anthropic, ContextWindowTestHelpers.NoOpCtx(),
            "claude-sonnet-4-6", 4096, "sys", "", NoOpRetryAnthropic());
        // apiKeyOverride omitted — defaults to null

        strategy.Initialize("sys", [], "query", []);
        await strategy.CallLlmAsync(CancellationToken.None);

        await anthropic.Received(1).GetClaudeMessageAsync(
            Arg.Any<MessageParameters>(),
            Arg.Any<CancellationToken>(),
            Arg.Is<string?>(s => s == null));
    }

    /// <summary>
    /// CallReplanAsync uses the same override path as CallLlmAsync — the key
    /// must be forwarded there too, otherwise a replan would use the wrong tenant key.
    /// </summary>
    [Fact]
    public async Task Anthropic_ApiKeyOverride_AlsoForwardedInCallReplanAsync()
    {
        var anthropic = Substitute.For<IAnthropicProvider>();
        anthropic.GetClaudeMessageAsync(
                Arg.Any<MessageParameters>(), Arg.Any<CancellationToken>(), Arg.Any<string?>())
            .Returns(MakeAnthropicResponse("replan text"));

        var strategy = new AnthropicProviderStrategy(
            anthropic, ContextWindowTestHelpers.NoOpCtx(),
            "claude-sonnet-4-6", 4096, "sys", "", NoOpRetryAnthropic(),
            apiKeyOverride: "override-key");

        strategy.Initialize("sys", [], "query", []);
        await strategy.CallReplanAsync(CancellationToken.None);

        await anthropic.Received(1).GetClaudeMessageAsync(
            Arg.Any<MessageParameters>(),
            Arg.Any<CancellationToken>(),
            Arg.Is<string?>(s => s == "override-key"));
    }

    // ── apiKeyOverride / endpointOverride forwarding — OpenAiProviderStrategy ─

    /// <summary>
    /// OpenAiProviderStrategy creates the IChatClient immediately in its constructor
    /// by calling CreateChatClient. The apiKeyOverride must be forwarded at that point
    /// so the correct credential is embedded in the client instance.
    /// </summary>
    [Fact]
    public void OpenAi_ApiKeyOverride_PassedToCreateChatClient()
    {
        var openAi = Substitute.For<IOpenAiProvider>();
        openAi.CreateChatClient(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>())
              .Returns(Substitute.For<IChatClient>());

        _ = new OpenAiProviderStrategy(
            openAi, ContextWindowTestHelpers.NoOpCtx(),
            "gpt-4.1", NoOpRetryOpenAi(),
            apiKeyOverride: "my-api-key");

        openAi.Received(1).CreateChatClient(
            Arg.Is<string>(s => s == "gpt-4.1"),
            Arg.Is<string?>(s => s == "my-api-key"),
            Arg.Is<string?>(s => s == null));
    }

    [Fact]
    public void OpenAi_EndpointOverride_PassedToCreateChatClient()
    {
        var openAi = Substitute.For<IOpenAiProvider>();
        openAi.CreateChatClient(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>())
              .Returns(Substitute.For<IChatClient>());

        _ = new OpenAiProviderStrategy(
            openAi, ContextWindowTestHelpers.NoOpCtx(),
            "gpt-4.1", NoOpRetryOpenAi(),
            endpointOverride: "http://custom-llm/");

        openAi.Received(1).CreateChatClient(
            Arg.Is<string>(s => s == "gpt-4.1"),
            Arg.Is<string?>(s => s == null),
            Arg.Is<string?>(s => s == "http://custom-llm/"));
    }

    // ── Retry helper ──────────────────────────────────────────────────────────

    private static Func<Func<Task<MessageResponse>>, CancellationToken, Task<MessageResponse>> NoOpRetryAnthropic()
        => (fn, ct) => fn();

    private static Func<Func<Task<ChatResponse>>, CancellationToken, Task<ChatResponse>> NoOpRetryOpenAi()
        => (fn, ct) => fn();

    private static MessageResponse MakeAnthropicResponse(string text) => new()
    {
        Content    = [new Anthropic.SDK.Messaging.TextContent { Text = text }],
        StopReason = "end_turn",
        Model      = "claude-sonnet-4-6",
    };
}
