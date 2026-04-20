namespace Diva.Agents.Hooks;

using Diva.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Resolves and executes lifecycle hooks for an agent execution.
/// Hooks are loaded from archetype defaults and agent definition HooksJson.
/// Uses HookTypeRegistry (built once at startup) for O(1) resolution.
/// </summary>
public sealed class AgentHookPipeline : IAgentHookPipeline
{
    private readonly IServiceProvider _sp;
    private readonly HookTypeRegistry _typeRegistry;
    private readonly ILogger<AgentHookPipeline> _logger;

    public AgentHookPipeline(
        IServiceProvider sp,
        HookTypeRegistry typeRegistry,
        ILogger<AgentHookPipeline> logger)
    {
        _sp = sp;
        _typeRegistry = typeRegistry;
        _logger = logger;
    }

    public (List<IAgentLifecycleHook> Hooks, IDisposable Scope) ResolveHooks(
        Dictionary<string, string> hookConfig,
        string archetypeId,
        TenantContext tenant)
    {
        var hooks = new List<IAgentLifecycleHook>();
        var scope = _sp.CreateScope();

        // Register TenantContext into the scope so hooks + any service they depend on
        // (e.g. DivaDbContext via IDatabaseProviderFactory) get proper tenant isolation.
        // ActivatorUtilities will supply TenantContext as an explicit parameter to hook constructors.
        foreach (var (hookPoint, className) in hookConfig)
        {
            if (string.IsNullOrWhiteSpace(className)) continue;

            var hookType = _typeRegistry.Resolve(className);
            if (hookType is null)
            {
                _logger.LogWarning(
                    "Hook class '{ClassName}' for point '{HookPoint}' not found in registry",
                    className, hookPoint);
                continue;
            }

            // Only pass TenantContext as an explicit argument when the hook's constructor
            // declares it — ActivatorUtilities requires every explicit arg to match a parameter.
            // Hooks that don't need constructor-level tenant isolation get it from AgentHookContext.Tenant.
            var needsTenant = hookType.GetConstructors()
                .Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(TenantContext)));
            var instance = (needsTenant
                ? ActivatorUtilities.CreateInstance(scope.ServiceProvider, hookType, tenant)
                : ActivatorUtilities.CreateInstance(scope.ServiceProvider, hookType)) as IAgentLifecycleHook;
            if (instance is not null)
                hooks.Add(instance);
        }

        return (hooks.OrderBy(h => h.Order).ToList(), scope);
    }

    public async Task RunOnInitAsync(
        List<IAgentLifecycleHook> hooks, AgentHookContext ctx, CancellationToken ct)
    {
        foreach (var hook in hooks.OfType<IOnInitHook>())
        {
            _logger.LogDebug("Running OnInit hook: {Hook}", hook.GetType().Name);
            await hook.OnInitAsync(ctx, ct);
        }
    }

    public async Task RunOnBeforeIterationAsync(
        List<IAgentLifecycleHook> hooks, AgentHookContext ctx, int iteration, CancellationToken ct)
    {
        foreach (var hook in hooks.OfType<IOnBeforeIterationHook>())
            await hook.OnBeforeIterationAsync(ctx, iteration, ct);
    }

    public async Task<List<UnifiedToolCallRef>> RunOnToolFilterAsync(
        List<IAgentLifecycleHook> hooks, AgentHookContext ctx,
        List<UnifiedToolCallRef> calls, CancellationToken ct)
    {
        var current = calls;
        foreach (var hook in hooks.OfType<IOnToolFilterHook>())
            current = await hook.OnToolFilterAsync(ctx, current, ct);
        return current;
    }

    public async Task<string> RunOnAfterToolCallAsync(
        List<IAgentLifecycleHook> hooks, AgentHookContext ctx,
        string toolName, string output, bool isError, CancellationToken ct)
    {
        var current = output;
        foreach (var hook in hooks.OfType<IOnAfterToolCallHook>())
            current = await hook.OnAfterToolCallAsync(ctx, toolName, current, isError, ct);
        return current;
    }

    public async Task<ErrorRecoveryAction> RunOnErrorAsync(
        List<IAgentLifecycleHook> hooks, AgentHookContext ctx,
        string? toolName, Exception exception, CancellationToken ct)
    {
        var action = ErrorRecoveryAction.Continue;
        foreach (var hook in hooks.OfType<IOnErrorHook>())
        {
            var result = await hook.OnErrorAsync(ctx, toolName, exception, ct);
            if (result > action) action = result; // Most severe action wins
        }
        return action;
    }

    public async Task<string> RunOnBeforeResponseAsync(
        List<IAgentLifecycleHook> hooks, AgentHookContext ctx, string text, CancellationToken ct)
    {
        var current = text;
        foreach (var hook in hooks.OfType<IOnBeforeResponseHook>())
            current = await hook.OnBeforeResponseAsync(ctx, current, ct);
        return current;
    }

    public async Task RunOnAfterResponseAsync(
        List<IAgentLifecycleHook> hooks, AgentHookContext ctx, AgentResponse response, CancellationToken ct)
    {
        foreach (var hook in hooks.OfType<IOnAfterResponseHook>())
            await hook.OnAfterResponseAsync(ctx, response, ct);
    }
}
