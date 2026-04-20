namespace Diva.Core.Models;

/// <summary>
/// Abstraction over the hook pipeline — enables mocking in unit tests.
/// Placed in Diva.Core so both Diva.Agents and Diva.Infrastructure can depend on it.
/// </summary>
public interface IAgentHookPipeline
{
    /// <summary>
    /// Resolves hooks from config. Returns a disposable scope that must outlive the hooks.
    /// Caller is responsible for disposing the scope after all hook calls are complete.
    /// The <paramref name="tenant"/> is registered in the scope so hooks get tenant-scoped data access.
    /// </summary>
    (List<IAgentLifecycleHook> Hooks, IDisposable Scope) ResolveHooks(Dictionary<string, string> hookConfig, string archetypeId, TenantContext tenant);
    Task RunOnInitAsync(List<IAgentLifecycleHook> hooks, AgentHookContext ctx, CancellationToken ct);
    Task RunOnBeforeIterationAsync(List<IAgentLifecycleHook> hooks, AgentHookContext ctx, int iteration, CancellationToken ct);
    Task<List<UnifiedToolCallRef>> RunOnToolFilterAsync(List<IAgentLifecycleHook> hooks, AgentHookContext ctx, List<UnifiedToolCallRef> calls, CancellationToken ct);
    Task<string> RunOnAfterToolCallAsync(List<IAgentLifecycleHook> hooks, AgentHookContext ctx, string toolName, string output, bool isError, CancellationToken ct);
    Task<ErrorRecoveryAction> RunOnErrorAsync(List<IAgentLifecycleHook> hooks, AgentHookContext ctx, string? toolName, Exception exception, CancellationToken ct);
    Task<string> RunOnBeforeResponseAsync(List<IAgentLifecycleHook> hooks, AgentHookContext ctx, string text, CancellationToken ct);
    Task RunOnAfterResponseAsync(List<IAgentLifecycleHook> hooks, AgentHookContext ctx, AgentResponse response, CancellationToken ct);
}
