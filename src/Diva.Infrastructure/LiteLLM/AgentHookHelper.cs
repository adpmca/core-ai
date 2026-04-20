using System.Text.Json;
using Diva.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Diva.Infrastructure.LiteLLM;

/// <summary>
/// Static helpers for merging archetype defaults with per-agent overrides when
/// resolving hook configuration and template variables at run time.
/// </summary>
internal static class AgentHookHelper
{
    /// <summary>
    /// Builds a resolved variable dictionary from archetype defaults and any
    /// per-agent custom variable JSON (<c>{"key":"value",...}</c>).
    /// </summary>
    internal static Dictionary<string, string> MergeVariables(
        AgentArchetype? archetype, string? customVariablesJson, ILogger? logger = null)
    {
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Archetypes don't carry default variables — only templates use {{}} placeholders.
        if (!string.IsNullOrWhiteSpace(customVariablesJson))
        {
            try
            {
                var custom = JsonSerializer.Deserialize<Dictionary<string, string>>(customVariablesJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (custom is not null)
                    foreach (var (k, v) in custom) vars[k] = v;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "CustomVariablesJson is malformed — agent will run with no custom variables");
            }
        }
        return vars;
    }

    /// <summary>
    /// Merges archetype default hook config with per-agent hook JSON overrides.
    /// Agent values win on collision. Always adds the built-in platform hooks.
    /// </summary>
    internal static Dictionary<string, string> MergeHookConfig(
        Dictionary<string, string>? archetypeHooks, string? agentHooksJson, ILogger? logger = null)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (archetypeHooks is not null)
            foreach (var (k, v) in archetypeHooks) merged[k] = v;
        if (!string.IsNullOrWhiteSpace(agentHooksJson))
        {
            try
            {
                var agentHooks = JsonSerializer.Deserialize<Dictionary<string, string>>(agentHooksJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (agentHooks is not null)
                    foreach (var (k, v) in agentHooks) merged[k] = v; // Agent overrides archetype
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "HooksJson is malformed — agent will run with archetype defaults only");
            }
        }
        // Always register the built-in platform hooks for every agent.
        merged.TryAdd("__rule_packs__", "TenantRulePackHook");
        merged.TryAdd("__static_model_switcher__", "StaticModelSwitcherHook");
        merged.TryAdd("__model_router__", "ModelRouterHook");
        return merged;
    }
}
