using System.Text.Json;
using Diva.Core.Configuration;
using Diva.Core.Models;
using Microsoft.Extensions.Logging;

namespace Diva.Agents.Hooks.BuiltIn;

/// <summary>
/// Built-in hook that reads <see cref="ModelSwitchingOptions"/> from the agent definition
/// (injected as Variables["__model_switching_json"] by the runner) and sets
/// <see cref="AgentHookContext.LlmConfigIdOverride"/> or <see cref="AgentHookContext.ModelOverride"/>
/// before each iteration based on the iteration phase.
///
/// Order=3 — runs after TenantRulePackHook (Order=2) so Rule Pack rules take precedence.
/// If an override is already set by a higher-priority hook this hook is a no-op.
/// </summary>
public sealed class StaticModelSwitcherHook : IOnBeforeIterationHook
{
    private readonly ILogger<StaticModelSwitcherHook> _logger;

    public StaticModelSwitcherHook(ILogger<StaticModelSwitcherHook> logger)
    {
        _logger = logger;
    }

    public int Order => 3;

    private const string OptsKey = "__model_switching_opts";

    public Task OnBeforeIterationAsync(AgentHookContext context, int iteration, CancellationToken ct)
    {
        // Already overridden by a higher-priority hook — skip
        if (HasOverride(context)) return Task.CompletedTask;

        var opts = GetOrParseOptions(context);
        if (opts is null) return Task.CompletedTask;

        bool hadTools = context.LastHadToolCalls;
        bool isFinal  = context.IsFinalIteration;

        // Priority: failure upgrade > final response > tool iteration
        string?  targetModel  = null;
        int?     targetCfgId  = null;
        string   reason       = "static_config";

        if (opts.UpgradeAfterFailures > 0 && context.ConsecutiveFailures >= opts.UpgradeAfterFailures)
        {
            targetCfgId  = opts.UpgradeOnFailuresLlmConfigId;
            targetModel  = opts.UpgradeOnFailuresModel;
            reason       = "failure_upgrade";
        }
        else if (isFinal)
        {
            targetCfgId = opts.FinalResponseLlmConfigId;
            targetModel = opts.FinalResponseModel;
        }
        else if (hadTools)
        {
            targetCfgId = opts.ToolIterationLlmConfigId;
            targetModel = opts.ToolIterationModel;
        }

        Apply(context, targetCfgId, targetModel, reason);

        // Replan model; runner reads typed properties before CallReplanAsync
        if (opts.ReplanLlmConfigId.HasValue)
            context.ReplanConfigId = opts.ReplanLlmConfigId.Value;
        else if (!string.IsNullOrEmpty(opts.ReplanModel))
            context.ReplanModel = opts.ReplanModel;

        return Task.CompletedTask;
    }

    private ModelSwitchingOptions? GetOrParseOptions(AgentHookContext ctx)
    {
        if (ctx.State.TryGetValue(OptsKey, out var cached))
            return cached as ModelSwitchingOptions;

        if (!ctx.Variables.TryGetValue("__model_switching_json", out var json) ||
            string.IsNullOrWhiteSpace(json))
        {
            ctx.State[OptsKey] = null;
            return null;
        }

        try
        {
            var opts = JsonSerializer.Deserialize<ModelSwitchingOptions>(
                json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            ctx.State[OptsKey] = opts;
            return opts;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "StaticModelSwitcherHook: failed to parse ModelSwitchingOptions JSON for agent {AgentId}", ctx.AgentId);
            ctx.State[OptsKey] = null;
            return null;
        }
    }

    private static void Apply(AgentHookContext ctx, int? cfgId, string? model, string reason)
    {
        if (cfgId.HasValue)
        {
            ctx.LlmConfigIdOverride = cfgId.Value;
            ctx.ModelSwitchReason   = reason;
        }
        else if (!string.IsNullOrEmpty(model))
        {
            ctx.ModelOverride     = model;
            ctx.ModelSwitchReason = reason;
        }
    }

    private static bool HasOverride(AgentHookContext ctx) =>
        ctx.LlmConfigIdOverride.HasValue || !string.IsNullOrEmpty(ctx.ModelOverride);
}
