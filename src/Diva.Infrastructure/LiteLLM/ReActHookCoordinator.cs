using Diva.Core.Models;
using Microsoft.Extensions.Logging;

namespace Diva.Infrastructure.LiteLLM;

/// <summary>
/// Result of a single hook lifecycle invocation. Contains any SSE chunks emitted
/// during the hook call and flags that require runner state mutation.
/// </summary>
public sealed class HookInvocationResult
{
    /// <summary>True when the hook threw an exception (an "error" chunk is included).</summary>
    public bool HadError { get; init; }

    /// <summary>True when an OnInit hook failure should abort the run immediately.</summary>
    public bool AbortRun { get; init; }

    /// <summary>Chunks to yield in order. May be empty.</summary>
    public IReadOnlyList<AgentStreamChunk> Chunks { get; init; } = [];

    /// <summary>
    /// When non-null, the runner should call <c>strategy.UpdateSystemPrompt</c>
    /// with this value.
    /// </summary>
    public string? UpdatedSystemPrompt { get; init; }

    public static HookInvocationResult Empty { get; } = new();
}

/// <summary>
/// Centralises all agent lifecycle hook calls so the main ReAct loop stays concise.
/// Each method wraps one hook lifecycle point with try/catch and chunk assembly.
///
/// Created by <see cref="AnthropicAgentRunner"/> when a hook pipeline is present.
/// Stateless — all context is passed per call.
/// </summary>
public sealed class ReActHookCoordinator(
    IAgentHookPipeline pipeline,
    ILogger<ReActHookCoordinator> logger) : IReActHookCoordinator
{
    // ── OnInit ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs <c>OnInit</c> hooks. When the hook fails the returned result has
    /// <see cref="HookInvocationResult.AbortRun"/> = true so the caller can yield error + done.
    /// </summary>
    public async Task<HookInvocationResult> RunOnInitAsync(
        List<IAgentLifecycleHook> hooks,
        AgentHookContext hookCtx,
        string systemPrompt,
        string sessionId,
        CancellationToken ct)
    {
        Exception? hookEx = null;
        try { await pipeline.RunOnInitAsync(hooks, hookCtx, ct); }
        catch (Exception e) { hookEx = e; logger.LogWarning(e, "OnInit hook failed"); }

        if (hookEx is not null)
        {
            return new HookInvocationResult
            {
                HadError  = true,
                AbortRun  = true,
                Chunks    =
                [
                    new AgentStreamChunk { Type = "error", ErrorMessage = $"Hook error: {hookEx.Message}" },
                    new AgentStreamChunk { Type = "done",  SessionId    = sessionId },
                ],
            };
        }

        var updatedPrompt = string.Equals(hookCtx.SystemPrompt, systemPrompt, StringComparison.Ordinal)
            ? null
            : hookCtx.SystemPrompt;

        return new HookInvocationResult
        {
            UpdatedSystemPrompt = updatedPrompt,
            Chunks = [BuildHookChunk("OnInit", "init", hookCtx)],
        };
    }

    // ── OnBeforeIteration ────────────────────────────────────────────────────

    public async Task<HookInvocationResult> RunOnBeforeIterationAsync(
        List<IAgentLifecycleHook> hooks,
        AgentHookContext hookCtx,
        int iteration,
        string systemPrompt,
        CancellationToken ct)
    {
        hookCtx.CurrentIteration    = iteration;
        Exception? hookEx = null;
        try { await pipeline.RunOnBeforeIterationAsync(hooks, hookCtx, iteration, ct); }
        catch (Exception e) { hookEx = e; logger.LogWarning(e, "OnBeforeIteration hook failed"); }

        if (hookEx is not null)
            return new HookInvocationResult
            {
                HadError = true,
                Chunks   = [new AgentStreamChunk { Type = "error", ErrorMessage = $"Hook error: {hookEx.Message}" }],
            };

        var updatedPrompt = string.Equals(hookCtx.SystemPrompt, systemPrompt, StringComparison.Ordinal)
            ? null
            : hookCtx.SystemPrompt;

        return new HookInvocationResult
        {
            UpdatedSystemPrompt = updatedPrompt,
            Chunks = [BuildHookChunk("OnBeforeIteration", "before_iteration", hookCtx)],
        };
    }

    // ── OnToolFilter ─────────────────────────────────────────────────────────

    public async Task<(List<UnifiedToolCallRef> Filtered, HookInvocationResult Result)> RunOnToolFilterAsync(
        List<IAgentLifecycleHook> hooks,
        AgentHookContext hookCtx,
        List<UnifiedToolCallRef> toolCalls,
        CancellationToken ct)
    {
        Exception? hookEx = null;
        List<UnifiedToolCallRef> filtered = toolCalls;
        try { filtered = await pipeline.RunOnToolFilterAsync(hooks, hookCtx, toolCalls, ct); }
        catch (Exception e) { hookEx = e; logger.LogWarning(e, "OnToolFilter hook failed"); }

        if (hookEx is not null)
            return (toolCalls, new HookInvocationResult
            {
                HadError = true,
                Chunks   = [new AgentStreamChunk { Type = "error", ErrorMessage = $"Hook error: {hookEx.Message}" }],
            });

        return (filtered, new HookInvocationResult
        {
            Chunks = [BuildHookChunk("OnToolFilter", "tool_filter", hookCtx)],
        });
    }

    // ── OnAfterToolCall ──────────────────────────────────────────────────────

    public async Task<(string FinalOutput, HookInvocationResult Result)> RunOnAfterToolCallAsync(
        List<IAgentLifecycleHook> hooks,
        AgentHookContext hookCtx,
        string toolName,
        string toolOutput,
        bool stepFailed,
        CancellationToken ct)
    {
        Exception? hookEx = null;
        string finalOutput = toolOutput;
        try { finalOutput = await pipeline.RunOnAfterToolCallAsync(hooks, hookCtx, toolName, toolOutput, stepFailed, ct); }
        catch (Exception e) { hookEx = e; logger.LogWarning(e, "OnAfterToolCall hook failed"); }

        if (hookEx is not null)
            return (toolOutput, new HookInvocationResult
            {
                HadError = true,
                Chunks   = [new AgentStreamChunk { Type = "error", ErrorMessage = $"Hook error: {hookEx.Message}" }],
            });

        return (finalOutput, new HookInvocationResult
        {
            Chunks = [BuildHookChunk("OnAfterToolCall", "after_tool_call", hookCtx)],
        });
    }

    // ── OnError ──────────────────────────────────────────────────────────────

    public async Task<(ErrorRecoveryAction Action, HookInvocationResult Result)> RunOnErrorAsync(
        List<IAgentLifecycleHook> hooks,
        AgentHookContext hookCtx,
        string? toolName,
        Exception error,
        string errorLabel,
        CancellationToken ct)
    {
        Exception? hookEx = null;
        var action = ErrorRecoveryAction.Continue;
        try { action = await pipeline.RunOnErrorAsync(hooks, hookCtx, toolName, error, ct); }
        catch (Exception e) { hookEx = e; logger.LogWarning(e, "OnError hook failed"); }

        if (hookEx is not null)
            return (ErrorRecoveryAction.Continue, new HookInvocationResult
            {
                HadError = true,
                Chunks   = [new AgentStreamChunk { Type = "error", ErrorMessage = $"Hook error: {hookEx.Message}" }],
            });

        return (action, new HookInvocationResult
        {
            Chunks = [BuildHookChunk(errorLabel, "error", hookCtx)],
        });
    }

    // ── OnBeforeResponse ─────────────────────────────────────────────────────

    public async Task<(string FinalResponse, HookInvocationResult Result)> RunOnBeforeResponseAsync(
        List<IAgentLifecycleHook> hooks,
        AgentHookContext hookCtx,
        string response,
        CancellationToken ct)
    {
        Exception? hookEx = null;
        string finalResponse = response;
        try { finalResponse = await pipeline.RunOnBeforeResponseAsync(hooks, hookCtx, response, ct); }
        catch (Exception e) { hookEx = e; logger.LogWarning(e, "OnBeforeResponse hook failed"); }

        if (hookEx is not null)
            return (response, new HookInvocationResult
            {
                HadError = true,
                Chunks   = [new AgentStreamChunk { Type = "error", ErrorMessage = $"Hook error: {hookEx.Message}" }],
            });

        return (finalResponse, new HookInvocationResult
        {
            Chunks = [BuildHookChunk("OnBeforeResponse", "before_response", hookCtx)],
        });
    }

    // ── OnAfterResponse ──────────────────────────────────────────────────────

    public async Task<HookInvocationResult> RunOnAfterResponseAsync(
        List<IAgentLifecycleHook> hooks,
        AgentHookContext hookCtx,
        AgentResponse afterResponse,
        CancellationToken ct)
    {
        Exception? hookEx = null;
        try { await pipeline.RunOnAfterResponseAsync(hooks, hookCtx, afterResponse, ct); }
        catch (Exception e) { hookEx = e; logger.LogWarning(e, "OnAfterResponse hook failed"); }

        if (hookEx is not null)
            return new HookInvocationResult
            {
                HadError = true,
                Chunks   = [new AgentStreamChunk { Type = "error", ErrorMessage = $"Hook error: {hookEx.Message}" }],
            };

        return new HookInvocationResult
        {
            Chunks = [BuildHookChunk("OnAfterResponse", "after_response", hookCtx)],
        };
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private static AgentStreamChunk BuildHookChunk(string hookName, string hookPoint, AgentHookContext ctx)
        => new()
        {
            Type                   = "hook_executed",
            HookName               = hookName,
            HookPoint              = hookPoint,
            RulePackTriggeredCount = ctx.RulePackLastTriggeredCount,
            RulePackTriggeredRules = ctx.RulePackLastTriggeredRules,
            RulePackFilteredCount  = ctx.RulePackLastFilteredCount,
            RulePackErrorAction    = ctx.RulePackLastErrorAction,
            RulePackBlocked        = ctx.RulePackLastBlocked,
        };
}
