using Diva.Infrastructure.Data.Entities;

namespace Diva.TenantAdmin.Services;

public interface ITenantBusinessRulesService
{
    // ── Business rules ─────────────────────────────────────────────────────────
    /// <summary>
    /// Returns active rules for the tenant and agentType.
    /// When <paramref name="agentId"/> is provided, returns global rules (AgentId=null) plus
    /// rules scoped specifically to that agent. When null, returns only global rules (backward-compat).
    /// </summary>
    Task<List<TenantBusinessRuleEntity>> GetRulesAsync(int tenantId, string agentType, CancellationToken ct, string? agentId = null);
    Task<string> GetPromptInjectionsAsync(int tenantId, string agentType, CancellationToken ct, string? agentId = null);
    Task<TenantBusinessRuleEntity> CreateRuleAsync(int tenantId, CreateRuleDto dto, CancellationToken ct);
    Task<TenantBusinessRuleEntity> UpdateRuleAsync(int tenantId, int ruleId, UpdateRuleDto dto, CancellationToken ct);
    Task DeleteRuleAsync(int tenantId, int ruleId, CancellationToken ct);

    /// <summary>Assigns or clears the rule pack association for a single business rule.</summary>
    Task AssignRuleToPackAsync(int tenantId, int ruleId, int? rulePackId, CancellationToken ct);

    /// <summary>Returns active business rules directly assigned to a specific rule pack, ordered by OrderInPack.</summary>
    Task<List<TenantBusinessRuleEntity>> GetRulesForPackAsync(int tenantId, int rulePackId, CancellationToken ct);

    // ── Prompt overrides ──────────────────────────────────────────────────────
    Task<List<TenantPromptOverrideEntity>> GetPromptOverridesAsync(int tenantId, string agentType, CancellationToken ct, string? agentId = null);
    /// <summary>Admin listing: returns all overrides for tenant. Optional exact filters; null = no filter.</summary>
    Task<List<TenantPromptOverrideEntity>> ListAllPromptOverridesAsync(int tenantId, string? agentType, string? agentId, CancellationToken ct);
    Task<TenantPromptOverrideEntity> CreatePromptOverrideAsync(int tenantId, CreatePromptOverrideDto dto, CancellationToken ct);
    Task<TenantPromptOverrideEntity> UpdatePromptOverrideAsync(int tenantId, int overrideId, UpdatePromptOverrideDto dto, CancellationToken ct);
    Task DeletePromptOverrideAsync(int tenantId, int overrideId, CancellationToken ct);

    // ── Group rule templates ──────────────────────────────────────────────────
    /// <summary>Returns group rule templates available to a tenant (IsTemplate=true, via group membership). Includes activation status.</summary>
    Task<List<GroupRuleTemplateDto>> GetAvailableGroupRuleTemplatesAsync(int tenantId, CancellationToken ct);

    /// <summary>Activates a group rule template for a tenant — creates a tenant business rule cloned from the group rule.</summary>
    Task<TenantBusinessRuleEntity> ActivateGroupRuleAsync(int tenantId, int groupRuleId, CancellationToken ct);

    /// <summary>Deactivates a previously activated group rule template for a tenant (sets IsActive=false on the cloned rule).</summary>
    Task DeactivateGroupRuleAsync(int tenantId, int groupRuleId, CancellationToken ct);

    // ── Cache ─────────────────────────────────────────────────────────────────
    void InvalidateCache(int tenantId, string agentType, string? agentId = null);
}

/// <summary>DTO representing a group-level rule template and whether the tenant has activated it.</summary>
public record GroupRuleTemplateDto(
    int Id,
    int GroupId,
    string GroupName,
    string AgentType,
    string RuleCategory,
    string RuleKey,
    string? PromptInjection,
    int Priority,
    string HookPoint,
    string HookRuleType,
    string? Pattern,
    string? Replacement,
    string? ToolName,
    int OrderInPack,
    bool StopOnMatch,
    int MaxEvaluationMs,
    bool IsActivated,
    int? ActivatedRuleId);

// ── DTOs ──────────────────────────────────────────────────────────────────────

public record CreateRuleDto(
    string AgentType,
    string RuleCategory,
    string RuleKey,
    string? PromptInjection,
    string? RuleValueJson = null,
    int Priority = 100,
    string? AgentId = null,
    int? RulePackId = null,
    string HookPoint = "OnInit",
    string HookRuleType = "inject_prompt",
    string? Pattern = null,
    string? Replacement = null,
    string? ToolName = null,
    int OrderInPack = 0,
    bool StopOnMatch = false,
    int MaxEvaluationMs = 100);

public record UpdateRuleDto(
    string RuleCategory,
    string RuleKey,
    string? PromptInjection,
    string? RuleValueJson,
    bool IsActive,
    int Priority,
    string? AgentId = null,
    int? RulePackId = null,
    string HookPoint = "OnInit",
    string HookRuleType = "inject_prompt",
    string? Pattern = null,
    string? Replacement = null,
    string? ToolName = null,
    int OrderInPack = 0,
    bool StopOnMatch = false,
    int MaxEvaluationMs = 100);

public record CreatePromptOverrideDto(
    string AgentType,
    string Section,
    string CustomText,
    string MergeMode = "Append",
    string? AgentId = null);

public record UpdatePromptOverrideDto(
    string CustomText,
    string MergeMode,
    bool IsActive);

public record AssignRuleToPackDto(int? RulePackId);

public record ValidateBusinessRuleDto(string? HookPoint, string? HookRuleType);

