namespace Diva.Agents.Hooks.BuiltIn;

using Diva.Core.Models;
using Diva.TenantAdmin.Services;
using Microsoft.Extensions.Logging;

/// <summary>
/// Built-in hook that loads and executes Rule Packs from the database at runtime.
/// Integrates at all supported Rule Pack lifecycle stages.
///
/// This hook is automatically registered for all agents — no manual configuration needed.
/// It resolves packs from the tenant's DB + group-inherited packs.
/// </summary>
public sealed class TenantRulePackHook :
    IOnInitHook,
    IOnBeforeIterationHook,
    IOnToolFilterHook,
    IOnAfterToolCallHook,
    IOnBeforeResponseHook,
    IOnAfterResponseHook,
    IOnErrorHook
{
    private readonly RulePackEngine _engine;
    private readonly ILogger<TenantRulePackHook> _logger;

    /// <summary>Run early so rule pack prompt injections are in place before other hooks.</summary>
    public int Order => 2;

    // State key for storing resolved packs between hook stages.
    private const string PacksStateKey = "__resolved_rule_packs";

    public TenantRulePackHook(
        RulePackEngine engine,
        ILogger<TenantRulePackHook> logger)
    {
        _engine = engine;
        _logger = logger;
    }

    public async Task OnInitAsync(AgentHookContext context, CancellationToken ct)
    {
        var tenantId = context.Tenant.TenantId;
        if (tenantId <= 0) return; // Skip for system/admin context

        var packs = await _engine.ResolvePacksAsync(
            tenantId, context.ArchetypeId, context.Request.Query, ct,
            agentType: context.ArchetypeId, agentId: context.AgentId?.ToString());

        if (packs.Count == 0) return;

        // Store resolved packs for OnBeforeResponse
        context.State[PacksStateKey] = packs;

        var result = _engine.EvaluateOnInit(
            packs, context.SystemPrompt, context.Request.Query, context.AgentId, tenantId);
        StoreLastExecution(context, result);

        if (result.TriggeredRules.Count > 0)
        {
            context.SystemPrompt = result.ModifiedText;
            _logger.LogDebug(
                "Rule packs injected {Count} OnInit rules for tenant {TenantId}, agent {AgentId}",
                result.TriggeredRules.Count, tenantId, context.AgentId);
        }

        if (result.Blocked)
        {
            _logger.LogWarning(
                "Rule pack blocked agent execution at OnInit for tenant {TenantId}, agent {AgentId}",
                tenantId, context.AgentId);
            // Store blocked state — runner should check this
            context.State["__rule_pack_blocked"] = true;
            context.State["__rule_pack_blocked_message"] = result.ModifiedText;
        }
    }

    public Task OnBeforeIterationAsync(AgentHookContext context, int iteration, CancellationToken ct)
    {
        var tenantId = context.Tenant.TenantId;
        if (tenantId <= 0)
            return Task.CompletedTask;

        if (!context.State.TryGetValue(PacksStateKey, out var packsObj) ||
            packsObj is not List<ResolvedRulePack> packs || packs.Count == 0)
        {
            return Task.CompletedTask;
        }

        var result = _engine.EvaluateOnBeforeIteration(
            packs, context.SystemPrompt, context.Request.Query, context.AgentId, tenantId,
            context.LastIterationResponse);
        StoreLastExecution(context, result);

        if (result.TriggeredRules.Count > 0)
        {
            context.SystemPrompt = result.ModifiedText;
            _logger.LogDebug(
                "Rule packs applied {Count} OnBeforeIteration rules for tenant {TenantId}, agent {AgentId}, iteration {Iteration}",
                result.TriggeredRules.Count, tenantId, context.AgentId, iteration);
        }

        if (result.Blocked)
        {
            _logger.LogWarning(
                "Rule pack blocked agent execution at OnBeforeIteration for tenant {TenantId}, agent {AgentId}, iteration {Iteration}",
                tenantId, context.AgentId, iteration);
            context.State["__rule_pack_blocked"] = true;
            context.State["__rule_pack_blocked_message"] = result.ModifiedText;
        }

        // Apply model_switch rule if triggered and no override already set by a higher-priority hook
        if (result.ModelSwitchRequest is { } msr && !HasModelOverrideAlready(context))
        {
            if (msr.LlmConfigId.HasValue)
            {
                context.LlmConfigIdOverride = msr.LlmConfigId.Value;
                // ModelId alongside LlmConfigId = model override on top of config (provider/key from config, model overridden)
                if (!string.IsNullOrEmpty(msr.ModelId))
                    context.ModelOverride = msr.ModelId;
            }
            else if (!string.IsNullOrEmpty(msr.ModelId))
            {
                context.ModelOverride     = msr.ModelId;
                context.MaxTokensOverride = msr.MaxTokens;
            }
            context.ModelSwitchReason = "rule_pack";
        }

        return Task.CompletedTask;
    }

    private static bool HasModelOverrideAlready(AgentHookContext ctx) =>
        ctx.LlmConfigIdOverride.HasValue || !string.IsNullOrEmpty(ctx.ModelOverride);

    public Task<string> OnBeforeResponseAsync(
        AgentHookContext context, string responseText, CancellationToken ct)
    {
        var tenantId = context.Tenant.TenantId;
        if (tenantId <= 0) return Task.FromResult(responseText);

        if (!context.State.TryGetValue(PacksStateKey, out var packsObj) ||
            packsObj is not List<ResolvedRulePack> packs || packs.Count == 0)
        {
            return Task.FromResult(responseText);
        }

        var result = _engine.EvaluateOnBeforeResponse(
            packs, responseText, context.Request.Query, context.AgentId, tenantId);
        StoreLastExecution(context, result);

        if (result.TriggeredRules.Count > 0)
        {
            _logger.LogDebug(
                "Rule packs applied {Count} OnBeforeResponse rules for tenant {TenantId}",
                result.TriggeredRules.Count, tenantId);
        }

        if (result.Blocked)
        {
            _logger.LogWarning(
                "Rule pack blocked response for tenant {TenantId}, agent {AgentId}",
                tenantId, context.AgentId);
        }

        return Task.FromResult(result.ModifiedText);
    }

    public Task<List<UnifiedToolCallRef>> OnToolFilterAsync(
        AgentHookContext context, List<UnifiedToolCallRef> toolCalls, CancellationToken ct)
    {
        var tenantId = context.Tenant.TenantId;
        if (tenantId <= 0)
            return Task.FromResult(toolCalls);

        if (!TryGetPacks(context, out var packs))
            return Task.FromResult(toolCalls);

        var filtered = _engine.EvaluateOnToolFilter(
            packs, toolCalls, context.Request.Query, context.AgentId, tenantId);

        var filteredCount = filtered.Count(tc => tc.Filtered);
        context.RulePackLastFilteredCount  = filteredCount;
        context.RulePackLastTriggeredCount = filteredCount;
        context.RulePackLastTriggeredRules = filteredCount > 0
            ? ["tool_filter:filtered"]
            : Array.Empty<string>();
        context.RulePackLastBlocked       = false;
        context.RulePackLastErrorAction   = null;
        if (filteredCount > 0)
        {
            _logger.LogDebug(
                "Rule packs filtered {Count} tool call(s) for tenant {TenantId}, agent {AgentId}, iteration {Iteration}",
                filteredCount, tenantId, context.AgentId, context.CurrentIteration);
        }

        return Task.FromResult(filtered);
    }

    public Task<string> OnAfterToolCallAsync(
        AgentHookContext context, string toolName, string toolOutput, bool isError, CancellationToken ct)
    {
        var tenantId = context.Tenant.TenantId;
        if (tenantId <= 0)
            return Task.FromResult(toolOutput);

        if (!TryGetPacks(context, out var packs))
            return Task.FromResult(toolOutput);

        var result = _engine.EvaluateOnAfterToolCall(
            packs, toolOutput, context.Request.Query, context.AgentId, tenantId);
        StoreLastExecution(context, result);

        if (result.TriggeredRules.Count > 0)
        {
            _logger.LogDebug(
                "Rule packs applied {Count} OnAfterToolCall rules for tenant {TenantId}, agent {AgentId}, tool {ToolName}, isError={IsError}",
                result.TriggeredRules.Count, tenantId, context.AgentId, toolName, isError);
        }

        return Task.FromResult(result.ModifiedText);
    }

    public Task OnAfterResponseAsync(AgentHookContext context, AgentResponse response, CancellationToken ct)
    {
        var tenantId = context.Tenant.TenantId;
        if (tenantId <= 0 || !TryGetPacks(context, out var packs))
            return Task.CompletedTask;

        var result = _engine.EvaluateOnAfterResponse(
            packs, response.Content, context.Request.Query, context.AgentId, tenantId);
        StoreLastExecution(context, result);

        if (result.TriggeredRules.Count > 0)
        {
            response.Metadata["rulePackAfterResponseTriggeredRules"] = result.TriggeredRules;
            if (!string.Equals(result.ModifiedText, response.Content, StringComparison.Ordinal))
                response.Metadata["rulePackAfterResponseSuggestedContent"] = result.ModifiedText;

            _logger.LogDebug(
                "Rule packs applied {Count} OnAfterResponse rules for tenant {TenantId}, agent {AgentId}",
                result.TriggeredRules.Count, tenantId, context.AgentId);
        }

        if (result.Blocked)
            response.Metadata["rulePackAfterResponseBlocked"] = true;

        return Task.CompletedTask;
    }

    public Task<ErrorRecoveryAction> OnErrorAsync(
        AgentHookContext context, string? toolName, Exception exception, CancellationToken ct)
    {
        var tenantId = context.Tenant.TenantId;
        if (tenantId <= 0 || !TryGetPacks(context, out var packs))
            return Task.FromResult(ErrorRecoveryAction.Continue);

        var result = _engine.EvaluateOnError(
            packs, toolName, exception, context.Request.Query, context.AgentId, tenantId);
        context.RulePackLastErrorAction   = result.Action.ToString();
        context.RulePackLastTriggeredCount = result.TriggeredRules.Count;
        context.RulePackLastTriggeredRules = result.TriggeredRules
            .Select(r => $"{r.RuleType}:{r.Action}")
            .ToArray();
        context.RulePackLastBlocked        = result.Action == ErrorRecoveryAction.Abort;
        context.RulePackLastFilteredCount  = null;

        if (result.TriggeredRules.Count > 0)
        {
            _logger.LogDebug(
                "Rule packs selected {Action} in OnError for tenant {TenantId}, agent {AgentId}, tool {ToolName}",
                result.Action, tenantId, context.AgentId, toolName ?? "llm");
        }

        return Task.FromResult(result.Action);
    }

    private static bool TryGetPacks(AgentHookContext context, out List<ResolvedRulePack> packs)
    {
        if (context.State.TryGetValue(PacksStateKey, out var packsObj)
            && packsObj is List<ResolvedRulePack> resolvedPacks
            && resolvedPacks.Count > 0)
        {
            packs = resolvedPacks;
            return true;
        }

        packs = [];
        return false;
    }

    private static void StoreLastExecution(AgentHookContext context, RuleEvalResult result)
    {
        context.RulePackLastTriggeredCount = result.TriggeredRules.Count;
        context.RulePackLastTriggeredRules = result.TriggeredRules
            .Select(r => $"{r.RuleType}:{r.Action}")
            .ToArray();
        context.RulePackLastBlocked       = result.Blocked;
        context.RulePackLastFilteredCount = null;
        context.RulePackLastErrorAction   = null;
    }
}
