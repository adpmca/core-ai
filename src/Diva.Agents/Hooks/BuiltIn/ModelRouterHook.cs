using Diva.Core.Models;

namespace Diva.Agents.Hooks.BuiltIn;

/// <summary>
/// Built-in smart model-routing hook. Reads routing config from agent Variables and
/// applies heuristic-based model selection before each iteration.
///
/// Order=4 — runs after StaticModelSwitcherHook (Order=3). No-op if an override is
/// already set by a higher-priority hook, or if model_router_mode is absent or "off".
///
/// Required Variables:
///   model_router_mode              — "smart" | "tool_downgrade_only" | "off" (default: "off")
///   model_router_fast_model        — model ID for cheap/fast iterations (same provider)
///   model_router_strong_model      — model ID for quality/recovery iterations (same provider)
///   model_router_fast_config_id    — LlmConfigId for fast model (any provider; takes precedence over model)
///   model_router_strong_config_id  — LlmConfigId for strong model (any provider; takes precedence over model)
/// </summary>
public sealed class ModelRouterHook : IOnBeforeIterationHook
{
    public int Order => 4;

    private const string LastHadToolsKey = "__last_had_tool_calls";
    private const string IsFinalKey      = "__is_final_iteration";

    public Task OnBeforeIterationAsync(AgentHookContext context, int iteration, CancellationToken ct)
    {
        // Already overridden by a higher-priority hook — skip
        if (HasOverride(context)) return Task.CompletedTask;

        var mode = context.Variables.GetValueOrDefault("model_router_mode", "off");
        if (string.Equals(mode, "off", StringComparison.OrdinalIgnoreCase))
            return Task.CompletedTask;

        bool hadTools      = context.State.TryGetValue(LastHadToolsKey, out var v) && v is true;
        bool isFinal       = context.State.TryGetValue(IsFinalKey,       out var f) && f is true;
        bool wasTruncated  = context.WasTruncated;
        bool isStuck       = context.ConsecutiveFailures >= 2;

        bool isSmart           = string.Equals(mode, "smart",               StringComparison.OrdinalIgnoreCase);
        bool isToolDowngrade   = string.Equals(mode, "tool_downgrade_only", StringComparison.OrdinalIgnoreCase);

        // Resolve config IDs (preferred) and model strings (fallback)
        int?    fastCfgId    = TryParseInt(context.Variables.GetValueOrDefault("model_router_fast_config_id"));
        int?    strongCfgId  = TryParseInt(context.Variables.GetValueOrDefault("model_router_strong_config_id"));
        string? fastModel    = context.Variables.GetValueOrDefault("model_router_fast_model");
        string? strongModel  = context.Variables.GetValueOrDefault("model_router_strong_model");

        bool hasFast   = fastCfgId.HasValue   || !string.IsNullOrEmpty(fastModel);
        bool hasStrong = strongCfgId.HasValue  || !string.IsNullOrEmpty(strongModel);

        if (!hasFast && !hasStrong) return Task.CompletedTask;

        if (isSmart)
        {
            if (isStuck && hasStrong)
            {
                Apply(context, strongCfgId, strongModel, "failure_upgrade");
            }
            else if (isFinal && hasStrong)
            {
                Apply(context, strongCfgId, strongModel, "smart_router");
            }
            else if (hadTools && hasFast)
            {
                Apply(context, fastCfgId, fastModel, "smart_router");
            }
            else if (wasTruncated && hasFast)
            {
                Apply(context, fastCfgId, fastModel, "smart_router");
            }
        }
        else if (isToolDowngrade && hadTools && hasFast)
        {
            Apply(context, fastCfgId, fastModel, "smart_router");
        }

        return Task.CompletedTask;
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

    private static int? TryParseInt(string? value) =>
        int.TryParse(value, out var result) ? result : null;
}
