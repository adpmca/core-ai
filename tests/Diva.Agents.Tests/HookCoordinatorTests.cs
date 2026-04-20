using Diva.Agents.Tests.Helpers;
using Diva.Core.Models;
using Diva.Infrastructure.LiteLLM;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Diva.Agents.Tests;

/// <summary>
/// Unit tests for <see cref="ReActHookCoordinator"/>.
/// Uses a mocked <see cref="IAgentHookPipeline"/> to exercise each lifecycle point
/// without executing real hooks or touching the DB.
/// </summary>
public class HookCoordinatorTests
{
    private readonly IAgentHookPipeline _pipeline = Substitute.For<IAgentHookPipeline>();
    private readonly ReActHookCoordinator _sut;
    private readonly AgentHookContext _ctx;

    public HookCoordinatorTests()
    {
        _sut = new ReActHookCoordinator(_pipeline, NullLogger<ReActHookCoordinator>.Instance);
        _ctx = new AgentHookContext
        {
            Request    = AgentTestFixtures.BasicRequest(),
            Tenant     = AgentTestFixtures.BasicTenant(),
            AgentId    = "agent-1",
            SystemPrompt = "Original prompt",
            SessionId  = "session-x",
        };
    }

    // ── RunOnInitAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task RunOnInitAsync_HappyPath_ReturnsHookExecutedChunk()
    {
        var result = await _sut.RunOnInitAsync([], _ctx, "Original prompt", "session-x", CancellationToken.None);

        Assert.False(result.AbortRun);
        Assert.False(result.HadError);
        Assert.Single(result.Chunks, c => c.Type == "hook_executed");
    }

    [Fact]
    public async Task RunOnInitAsync_WhenPipelineThrows_ReturnsAbortRunWithErrorChunks()
    {
        _pipeline.RunOnInitAsync(Arg.Any<List<IAgentLifecycleHook>>(), _ctx, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("init failure"));

        var result = await _sut.RunOnInitAsync([], _ctx, "prompt", "session-x", CancellationToken.None);

        Assert.True(result.AbortRun);
        Assert.True(result.HadError);
        Assert.Contains(result.Chunks, c => c.Type == "error");
        Assert.Contains(result.Chunks, c => c.Type == "done");
    }

    [Fact]
    public async Task RunOnInitAsync_WhenPromptModified_ReturnsUpdatedSystemPrompt()
    {
        _pipeline.RunOnInitAsync(Arg.Any<List<IAgentLifecycleHook>>(), _ctx, Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(_ => _ctx.SystemPrompt = "Modified by hook");

        var result = await _sut.RunOnInitAsync([], _ctx, "Original prompt", "session-x", CancellationToken.None);

        Assert.Equal("Modified by hook", result.UpdatedSystemPrompt);
    }

    [Fact]
    public async Task RunOnInitAsync_WhenPromptUnchanged_UpdatedSystemPromptIsNull()
    {
        var result = await _sut.RunOnInitAsync([], _ctx, _ctx.SystemPrompt, "session-x", CancellationToken.None);

        Assert.Null(result.UpdatedSystemPrompt);
    }

    // ── RunOnBeforeIterationAsync ─────────────────────────────────────────────

    [Fact]
    public async Task RunOnBeforeIterationAsync_HappyPath_SetsCurrentIteration()
    {
        var result = await _sut.RunOnBeforeIterationAsync([], _ctx, 3, "prompt", CancellationToken.None);

        Assert.Equal(3, _ctx.CurrentIteration);
        Assert.False(result.HadError);
        Assert.Single(result.Chunks, c => c.Type == "hook_executed");
    }

    [Fact]
    public async Task RunOnBeforeIterationAsync_WhenPipelineThrows_ReturnsError()
    {
        _pipeline.RunOnBeforeIterationAsync(Arg.Any<List<IAgentLifecycleHook>>(), _ctx, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("hook crash"));

        var result = await _sut.RunOnBeforeIterationAsync([], _ctx, 1, "prompt", CancellationToken.None);

        Assert.True(result.HadError);
        Assert.Contains(result.Chunks, c => c.Type == "error" && c.ErrorMessage!.Contains("Hook error:"));
    }

    // ── RunOnToolFilterAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task RunOnToolFilterAsync_HappyPath_ReturnsFilteredList()
    {
        var toolCalls = new List<UnifiedToolCallRef>
        {
            new() { Id = "1", Name = "tool_a", InputJson = "{}" },
            new() { Id = "2", Name = "tool_b", InputJson = "{}" },
        };
        var afterFilter = toolCalls[..1];  // hook removes second tool

        _pipeline.RunOnToolFilterAsync(Arg.Any<List<IAgentLifecycleHook>>(), _ctx, toolCalls, Arg.Any<CancellationToken>())
            .Returns(afterFilter);

        var (filtered, result) = await _sut.RunOnToolFilterAsync([], _ctx, toolCalls, CancellationToken.None);

        Assert.Single(filtered);
        Assert.Equal("tool_a", filtered[0].Name);
        Assert.False(result.HadError);
    }

    [Fact]
    public async Task RunOnToolFilterAsync_WhenPipelineThrows_ReturnsOriginalListAndError()
    {
        var toolCalls = new List<UnifiedToolCallRef> { new() { Id = "1", Name = "tool_a", InputJson = "{}" } };

        _pipeline.RunOnToolFilterAsync(Arg.Any<List<IAgentLifecycleHook>>(), _ctx, toolCalls, Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("filter crash"));

        var (filtered, result) = await _sut.RunOnToolFilterAsync([], _ctx, toolCalls, CancellationToken.None);

        // On error, original list is returned unmodified
        Assert.Equal(toolCalls, filtered);
        Assert.True(result.HadError);
    }

    // ── RunOnAfterToolCallAsync ───────────────────────────────────────────────

    [Fact]
    public async Task RunOnAfterToolCallAsync_HappyPath_ReturnsModifiedOutput()
    {
        _pipeline.RunOnAfterToolCallAsync(
            Arg.Any<List<IAgentLifecycleHook>>(), _ctx, "tool_a", "raw output", false, Arg.Any<CancellationToken>())
            .Returns("cleaned output");

        var (output, result) = await _sut.RunOnAfterToolCallAsync([], _ctx, "tool_a", "raw output", false, CancellationToken.None);

        Assert.Equal("cleaned output", output);
        Assert.False(result.HadError);
    }

    [Fact]
    public async Task RunOnAfterToolCallAsync_WhenPipelineThrows_ReturnsOriginalOutput()
    {
        _pipeline.RunOnAfterToolCallAsync(
            Arg.Any<List<IAgentLifecycleHook>>(), _ctx, "tool_a", "raw output", false, Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("crash"));

        var (output, result) = await _sut.RunOnAfterToolCallAsync([], _ctx, "tool_a", "raw output", false, CancellationToken.None);

        Assert.Equal("raw output", output);
        Assert.True(result.HadError);
    }

    // ── RunOnBeforeResponseAsync ──────────────────────────────────────────────

    [Fact]
    public async Task RunOnBeforeResponseAsync_HappyPath_ReturnsModifiedResponse()
    {
        _pipeline.RunOnBeforeResponseAsync(
            Arg.Any<List<IAgentLifecycleHook>>(), _ctx, "original response", Arg.Any<CancellationToken>())
            .Returns("modified response");

        var (finalResponse, result) = await _sut.RunOnBeforeResponseAsync([], _ctx, "original response", CancellationToken.None);

        Assert.Equal("modified response", finalResponse);
        Assert.False(result.HadError);
    }

    // ── RunOnAfterResponseAsync ───────────────────────────────────────────────

    [Fact]
    public async Task RunOnAfterResponseAsync_WhenPipelineThrows_ReturnsHadError()
    {
        var response = new AgentResponse { Success = true, Content = "done" };
        _pipeline.RunOnAfterResponseAsync(
            Arg.Any<List<IAgentLifecycleHook>>(), _ctx, response, Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("after response crash"));

        var result = await _sut.RunOnAfterResponseAsync([], _ctx, response, CancellationToken.None);

        Assert.True(result.HadError);
        Assert.Contains(result.Chunks, c => c.Type == "error");
    }

    // ── HookInvocationResult.Empty ────────────────────────────────────────────

    [Fact]
    public void HookInvocationResult_Empty_HasNoChunksAndNoFlags()
    {
        var empty = HookInvocationResult.Empty;

        Assert.False(empty.HadError);
        Assert.False(empty.AbortRun);
        Assert.Empty(empty.Chunks);
        Assert.Null(empty.UpdatedSystemPrompt);
    }
}
