namespace Diva.Infrastructure.LiteLLM;

/// <summary>
/// Resolves the effective LLM configuration for a given tenant + agent definition
/// by merging the hierarchy bottom-up:
///
/// Default path (agentLlmConfigId is null):
///   1. PlatformLlmConfigEntity (global DB defaults, fallback to IOptions&lt;LlmOptions&gt; if not seeded)
///   2. GroupLlmConfigEntity (unnamed default, first group ordered by GroupId ASC)
///   3. TenantLlmConfigEntity (unnamed default per-tenant)
///   4. agentModelId (per-agent model-only override)
///
/// Named config path (agentLlmConfigId is not null):
///   1. PlatformLlmConfigEntity (baseline defaults)
///   2. Named config looked up by ID — first in TenantLlmConfigs, then GroupLlmConfigs
///   3. agentModelId (per-agent model-only override on top of named config)
/// </summary>
public interface ILlmConfigResolver
{
    /// <param name="agentLlmConfigId">When set, use a specific named config by ID (bypasses group→tenant default chain).</param>
    Task<ResolvedLlmConfig> ResolveAsync(int tenantId, int? agentLlmConfigId, string? agentModelId, CancellationToken ct);

    /// <summary>Evicts the cached config for the given tenant (call after updating any level).</summary>
    void InvalidateForTenant(int tenantId);

    /// <summary>Evicts the cached platform config (call after updating PlatformLlmConfig).</summary>
    void InvalidatePlatform();
}

public sealed record ResolvedLlmConfig(
    string Provider,
    string ApiKey,
    string Model,
    string? Endpoint,
    string? DeploymentName,
    IReadOnlyList<string> AvailableModels);
