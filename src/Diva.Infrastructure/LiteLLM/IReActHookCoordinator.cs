using Diva.Core.Models;

namespace Diva.Infrastructure.LiteLLM;

/// <summary>
/// Abstracts all agent lifecycle hook dispatch so <see cref="AnthropicAgentRunner"/>
/// can be tested without a real hook pipeline.
/// </summary>
public interface IReActHookCoordinator
{
    Task<HookInvocationResult> RunOnInitAsync(
        List<IAgentLifecycleHook> hooks,
        AgentHookContext hookCtx,
        string systemPrompt,
        string sessionId,
        CancellationToken ct);

    Task<HookInvocationResult> RunOnBeforeIterationAsync(
        List<IAgentLifecycleHook> hooks,
        AgentHookContext hookCtx,
        int iteration,
        string systemPrompt,
        CancellationToken ct);

    Task<(List<UnifiedToolCallRef> Filtered, HookInvocationResult Result)> RunOnToolFilterAsync(
        List<IAgentLifecycleHook> hooks,
        AgentHookContext hookCtx,
        List<UnifiedToolCallRef> toolCalls,
        CancellationToken ct);

    Task<(string FinalOutput, HookInvocationResult Result)> RunOnAfterToolCallAsync(
        List<IAgentLifecycleHook> hooks,
        AgentHookContext hookCtx,
        string toolName,
        string toolOutput,
        bool stepFailed,
        CancellationToken ct);

    Task<(ErrorRecoveryAction Action, HookInvocationResult Result)> RunOnErrorAsync(
        List<IAgentLifecycleHook> hooks,
        AgentHookContext hookCtx,
        string? toolName,
        Exception error,
        string errorLabel,
        CancellationToken ct);

    Task<(string FinalResponse, HookInvocationResult Result)> RunOnBeforeResponseAsync(
        List<IAgentLifecycleHook> hooks,
        AgentHookContext hookCtx,
        string response,
        CancellationToken ct);

    Task<HookInvocationResult> RunOnAfterResponseAsync(
        List<IAgentLifecycleHook> hooks,
        AgentHookContext hookCtx,
        AgentResponse afterResponse,
        CancellationToken ct);
}
