using Diva.Infrastructure.Data;
using Diva.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Diva.TenantAdmin.Services;

/// <summary>
/// Provides per-tenant business rules with 5-minute memory cache.
/// Singleton-safe: uses IDatabaseProviderFactory per call (no scoped DbContext stored).
/// </summary>
public sealed class TenantBusinessRulesService : ITenantBusinessRulesService
{
    private readonly IDatabaseProviderFactory _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TenantBusinessRulesService> _logger;

    public TenantBusinessRulesService(
        IDatabaseProviderFactory db,
        IMemoryCache cache,
        ILogger<TenantBusinessRulesService> logger)
    {
        _db     = db;
        _cache  = cache;
        _logger = logger;
    }

    // ── Business rules ─────────────────────────────────────────────────────────

    public async Task<List<TenantBusinessRuleEntity>> GetRulesAsync(
        int tenantId, string agentType, CancellationToken ct, string? agentId = null)
    {
        var key = RulesCacheKey(tenantId, agentType, agentId);
        if (_cache.TryGetValue(key, out List<TenantBusinessRuleEntity>? cached) && cached is not null)
            return cached;

        using var db = _db.CreateDbContext();
        var query = db.BusinessRules
            .Where(r => r.TenantId == tenantId && r.IsActive)
            .Where(r => r.AgentType == agentType || r.AgentType == "*");

        // When agentId is provided: include global rules (AgentId=null) + agent-specific rules.
        // When agentId is null: include only global rules (backward-compat — never leaks scoped rules).
        query = agentId is not null
            ? query.Where(r => r.AgentId == null || r.AgentId == agentId)
            : query.Where(r => r.AgentId == null);

        var rules = await query
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.RuleCategory)
            .ToListAsync(ct);

        _cache.Set(key, rules, TimeSpan.FromMinutes(5));
        return rules;
    }

    public async Task<string> GetPromptInjectionsAsync(
        int tenantId, string agentType, CancellationToken ct, string? agentId = null)
    {
        var rules = await GetRulesAsync(tenantId, agentType, ct, agentId);
        var injections = rules
            .Where(r => !string.IsNullOrWhiteSpace(r.PromptInjection))
            .Select(r => $"- {r.PromptInjection!.Trim()}")
            .ToList();

        return injections.Count == 0
            ? string.Empty
            : "## Business Rules\n\n" + string.Join("\n", injections);
    }

    public async Task<TenantBusinessRuleEntity> CreateRuleAsync(
        int tenantId, CreateRuleDto dto, CancellationToken ct)
    {
        // Validate hook point + rule type compatibility before saving
        var (valid, allowedTypes) = RulePackRuleCompatibility.ValidateBusinessRule(dto.HookPoint, dto.HookRuleType);
        if (!valid)
            throw new InvalidOperationException(
                $"Invalid HookRuleType '{dto.HookRuleType}' for HookPoint '{dto.HookPoint}'. " +
                $"Allowed: {string.Join(", ", allowedTypes)}");

        // Validate RulePackId belongs to this tenant before assigning
        if (dto.RulePackId.HasValue)
            await ValidatePackOwnership(tenantId, dto.RulePackId.Value, ct);

        using var db = _db.CreateDbContext();
        var entity = new TenantBusinessRuleEntity
        {
            Guid            = System.Guid.NewGuid().ToString(),
            TenantId        = tenantId,
            AgentType       = dto.AgentType,
            AgentId         = dto.AgentId,
            RuleCategory    = dto.RuleCategory,
            RuleKey         = dto.RuleKey,
            PromptInjection = dto.PromptInjection,
            RuleValueJson   = dto.RuleValueJson,
            Priority        = dto.Priority,
            IsActive        = true,
            RulePackId      = dto.RulePackId,
            HookPoint       = dto.HookPoint,
            HookRuleType    = dto.HookRuleType,
            Pattern         = dto.Pattern,
            Replacement     = dto.Replacement,
            ToolName        = dto.ToolName,
            OrderInPack     = dto.OrderInPack,
            StopOnMatch     = dto.StopOnMatch,
            MaxEvaluationMs = dto.MaxEvaluationMs,
        };
        db.BusinessRules.Add(entity);
        await db.SaveChangesAsync(ct);
        InvalidateCache(tenantId, dto.AgentType, dto.AgentId);
        if (dto.RulePackId.HasValue) InvalidatePackCache(tenantId);
        _logger.LogInformation("Created rule {Key} for tenant {TenantId}", dto.RuleKey, tenantId);
        return entity;
    }

    public async Task<TenantBusinessRuleEntity> UpdateRuleAsync(
        int tenantId, int ruleId, UpdateRuleDto dto, CancellationToken ct)
    {
        // Validate hook point + rule type compatibility before saving
        var (valid, allowedTypes) = RulePackRuleCompatibility.ValidateBusinessRule(dto.HookPoint, dto.HookRuleType);
        if (!valid)
            throw new InvalidOperationException(
                $"Invalid HookRuleType '{dto.HookRuleType}' for HookPoint '{dto.HookPoint}'. " +
                $"Allowed: {string.Join(", ", allowedTypes)}");

        // Validate RulePackId belongs to this tenant before assigning
        if (dto.RulePackId.HasValue)
            await ValidatePackOwnership(tenantId, dto.RulePackId.Value, ct);

        using var db = _db.CreateDbContext();
        var entity = await db.BusinessRules
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == ruleId, ct)
            ?? throw new InvalidOperationException($"Rule {ruleId} not found for tenant {tenantId}");

        var oldPackId = entity.RulePackId;
        entity.RuleCategory    = dto.RuleCategory;
        entity.RuleKey         = dto.RuleKey;
        entity.PromptInjection = dto.PromptInjection;
        entity.RuleValueJson   = dto.RuleValueJson;
        entity.Priority        = dto.Priority;
        entity.IsActive        = dto.IsActive;
        entity.AgentId         = dto.AgentId;
        entity.RulePackId      = dto.RulePackId;
        entity.HookPoint       = dto.HookPoint;
        entity.HookRuleType    = dto.HookRuleType;
        entity.Pattern         = dto.Pattern;
        entity.Replacement     = dto.Replacement;
        entity.ToolName        = dto.ToolName;
        entity.OrderInPack     = dto.OrderInPack;
        entity.StopOnMatch     = dto.StopOnMatch;
        entity.MaxEvaluationMs = dto.MaxEvaluationMs;

        await db.SaveChangesAsync(ct);
        InvalidateCache(tenantId, entity.AgentType, entity.AgentId);
        // Invalidate rule pack cache if pack assignment changed (T5: cross-cache invalidation)
        if (oldPackId != dto.RulePackId) InvalidatePackCache(tenantId);
        return entity;
    }

    public async Task DeleteRuleAsync(int tenantId, int ruleId, CancellationToken ct)
    {
        using var db = _db.CreateDbContext();
        var entity = await db.BusinessRules
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == ruleId, ct)
            ?? throw new InvalidOperationException($"Rule {ruleId} not found for tenant {tenantId}");

        var packId = entity.RulePackId;
        db.BusinessRules.Remove(entity);
        await db.SaveChangesAsync(ct);
        InvalidateCache(tenantId, entity.AgentType, entity.AgentId);
        if (packId.HasValue) InvalidatePackCache(tenantId);
    }

    public async Task<List<TenantBusinessRuleEntity>> GetRulesForPackAsync(
        int tenantId, int rulePackId, CancellationToken ct)
    {
        using var db = _db.CreateDbContext();
        return await db.BusinessRules
            .Where(r => r.TenantId == tenantId && r.RulePackId == rulePackId && r.IsActive)
            .OrderBy(r => r.OrderInPack)
            .ThenBy(r => r.Priority)
            .ToListAsync(ct);
    }

    public async Task AssignRuleToPackAsync(int tenantId, int ruleId, int? rulePackId, CancellationToken ct)
    {
        if (rulePackId.HasValue)
            await ValidatePackOwnership(tenantId, rulePackId.Value, ct);

        using var db = _db.CreateDbContext();
        var entity = await db.BusinessRules
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == ruleId, ct)
            ?? throw new InvalidOperationException($"Rule {ruleId} not found for tenant {tenantId}");

        var oldPackId = entity.RulePackId;
        entity.RulePackId = rulePackId;
        await db.SaveChangesAsync(ct);

        InvalidateCache(tenantId, entity.AgentType, entity.AgentId);
        if (oldPackId != rulePackId) InvalidatePackCache(tenantId);
    }

    private async Task ValidatePackOwnership(int tenantId, int rulePackId, CancellationToken ct)
    {
        using var db = _db.CreateDbContext();
        var exists = await db.RulePacks
            .IgnoreQueryFilters()
            .AnyAsync(p => p.Id == rulePackId && p.TenantId == tenantId, ct);
        if (!exists)
            throw new InvalidOperationException(
                $"Rule pack {rulePackId} not found for tenant {tenantId}.");
    }

    // ── Prompt overrides ──────────────────────────────────────────────────────

    public async Task<List<TenantPromptOverrideEntity>> GetPromptOverridesAsync(
        int tenantId, string agentType, CancellationToken ct, string? agentId = null)
    {
        var key = OverridesCacheKey(tenantId, agentType);
        if (_cache.TryGetValue(key, out List<TenantPromptOverrideEntity>? cached) && cached is not null)
            return cached;

        using var db = _db.CreateDbContext();
        var overrides = await db.PromptOverrides
            .Where(o => o.TenantId == tenantId && o.IsActive)
            .Where(o => o.AgentType == agentType || o.AgentType == "*")
            .Where(o => o.AgentId == null || o.AgentId == agentId)
            .OrderBy(o => o.Section)
            .ToListAsync(ct);

        _cache.Set(key, overrides, TimeSpan.FromMinutes(5));
        return overrides;
    }

    public async Task<List<TenantPromptOverrideEntity>> ListAllPromptOverridesAsync(
        int tenantId, string? agentType, string? agentId, CancellationToken ct)
    {
        using var db = _db.CreateDbContext();
        var q = db.PromptOverrides.Where(o => o.TenantId == tenantId);
        if (!string.IsNullOrEmpty(agentType) && agentType != "*")
            q = q.Where(o => o.AgentType == agentType);
        if (!string.IsNullOrEmpty(agentId))
            q = q.Where(o => o.AgentId == agentId);
        return await q.OrderBy(o => o.AgentType).ThenBy(o => o.Section).ToListAsync(ct);
    }

    public async Task<TenantPromptOverrideEntity> CreatePromptOverrideAsync(
        int tenantId, CreatePromptOverrideDto dto, CancellationToken ct)
    {
        using var db = _db.CreateDbContext();
        var entity = new TenantPromptOverrideEntity
        {
            TenantId   = tenantId,
            AgentType  = dto.AgentType,
            AgentId    = dto.AgentId,
            Section    = dto.Section,
            CustomText = dto.CustomText,
            MergeMode  = dto.MergeMode,
            IsActive   = true,
        };
        db.PromptOverrides.Add(entity);
        await db.SaveChangesAsync(ct);
        InvalidateCache(tenantId, dto.AgentType);
        return entity;
    }

    public async Task<TenantPromptOverrideEntity> UpdatePromptOverrideAsync(
        int tenantId, int overrideId, UpdatePromptOverrideDto dto, CancellationToken ct)
    {
        using var db = _db.CreateDbContext();
        var entity = await db.PromptOverrides
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.Id == overrideId, ct)
            ?? throw new InvalidOperationException($"Override {overrideId} not found for tenant {tenantId}");

        entity.CustomText = dto.CustomText;
        entity.MergeMode  = dto.MergeMode;
        entity.IsActive   = dto.IsActive;

        await db.SaveChangesAsync(ct);
        InvalidateCache(tenantId, entity.AgentType);
        return entity;
    }

    public async Task DeletePromptOverrideAsync(int tenantId, int overrideId, CancellationToken ct)
    {
        using var db = _db.CreateDbContext();
        var entity = await db.PromptOverrides
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.Id == overrideId, ct)
            ?? throw new InvalidOperationException($"Override {overrideId} not found for tenant {tenantId}");

        db.PromptOverrides.Remove(entity);
        await db.SaveChangesAsync(ct);
        InvalidateCache(tenantId, entity.AgentType);
    }

    // ── Group rule templates ──────────────────────────────────────────────────

    public async Task<List<GroupRuleTemplateDto>> GetAvailableGroupRuleTemplatesAsync(
        int tenantId, CancellationToken ct)
    {
        using var db = _db.CreateDbContext();

        // Find which groups this tenant belongs to
        var groupIds = await db.TenantGroupMembers
            .Where(m => m.TenantId == tenantId)
            .Select(m => m.GroupId)
            .ToListAsync(ct);

        if (groupIds.Count == 0)
            return [];

        // Fetch all active template rules from those groups, with group name
        var templates = await db.GroupBusinessRules
            .Include(r => r.Group)
            .Where(r => groupIds.Contains(r.GroupId) && r.IsTemplate && r.IsActive)
            .OrderBy(r => r.GroupId).ThenBy(r => r.Priority).ThenBy(r => r.RuleCategory)
            .ToListAsync(ct);

        if (templates.Count == 0)
            return [];

        // Find which have already been activated (cloned) for this tenant
        var templateIds = templates.Select(t => t.Id).ToList();
        var activated = await db.BusinessRules
            .Where(r => r.TenantId == tenantId && r.SourceGroupRuleId != null
                        && templateIds.Contains(r.SourceGroupRuleId!.Value) && r.IsActive)
            .Select(r => new { r.SourceGroupRuleId, r.Id })
            .ToListAsync(ct);

        var activatedMap = activated.ToDictionary(a => a.SourceGroupRuleId!.Value, a => a.Id);

        return templates.Select(t => new GroupRuleTemplateDto(
            t.Id, t.GroupId, t.Group.Name, t.AgentType,
            t.RuleCategory, t.RuleKey, t.PromptInjection, t.Priority,
            t.HookPoint, t.HookRuleType, t.Pattern, t.Replacement, t.ToolName,
            t.OrderInPack, t.StopOnMatch, t.MaxEvaluationMs,
            activatedMap.ContainsKey(t.Id), activatedMap.TryGetValue(t.Id, out var rid) ? rid : null
        )).ToList();
    }

    public async Task<TenantBusinessRuleEntity> ActivateGroupRuleAsync(
        int tenantId, int groupRuleId, CancellationToken ct)
    {
        using var db = _db.CreateDbContext();

        var groupRule = await db.GroupBusinessRules
            .Include(r => r.Group)
            .FirstOrDefaultAsync(r => r.Id == groupRuleId, ct)
            ?? throw new InvalidOperationException($"Group rule {groupRuleId} not found.");

        if (!groupRule.IsTemplate)
            throw new InvalidOperationException($"Group rule {groupRuleId} is not a template.");

        // Check tenant is a member of the group
        var isMember = await db.TenantGroupMembers
            .AnyAsync(m => m.GroupId == groupRule.GroupId && m.TenantId == tenantId, ct);
        if (!isMember)
            throw new InvalidOperationException("Tenant is not a member of the group owning this template.");

        // Check not already activated
        var existing = await db.BusinessRules
            .FirstOrDefaultAsync(r => r.TenantId == tenantId
                && r.SourceGroupRuleId == groupRuleId && r.IsActive, ct);
        if (existing is not null)
            return existing;

        var entity = new TenantBusinessRuleEntity
        {
            Guid            = System.Guid.NewGuid().ToString(),
            TenantId        = tenantId,
            AgentType       = groupRule.AgentType,
            RuleCategory    = groupRule.RuleCategory,
            RuleKey         = groupRule.RuleKey,
            PromptInjection = groupRule.PromptInjection,
            RuleValueJson   = groupRule.RuleValueJson,
            IsActive        = true,
            Priority        = groupRule.Priority,
            HookPoint       = groupRule.HookPoint,
            HookRuleType    = groupRule.HookRuleType,
            Pattern         = groupRule.Pattern,
            Replacement     = groupRule.Replacement,
            ToolName        = groupRule.ToolName,
            OrderInPack     = groupRule.OrderInPack,
            StopOnMatch     = groupRule.StopOnMatch,
            MaxEvaluationMs = groupRule.MaxEvaluationMs,
            SourceGroupRuleId = groupRuleId,
        };
        db.BusinessRules.Add(entity);
        await db.SaveChangesAsync(ct);
        InvalidateCache(tenantId, groupRule.AgentType);
        _logger.LogInformation("Tenant {TenantId} activated group rule template {GroupRuleId}", tenantId, groupRuleId);
        return entity;
    }

    public async Task DeactivateGroupRuleAsync(int tenantId, int groupRuleId, CancellationToken ct)
    {
        using var db = _db.CreateDbContext();

        var rules = await db.BusinessRules
            .Where(r => r.TenantId == tenantId && r.SourceGroupRuleId == groupRuleId && r.IsActive)
            .ToListAsync(ct);

        if (rules.Count == 0)
            return;

        foreach (var r in rules)
            r.IsActive = false;

        await db.SaveChangesAsync(ct);

        // Invalidate cache for each affected agentType
        foreach (var agentType in rules.Select(r => r.AgentType).Distinct())
            InvalidateCache(tenantId, agentType);

        _logger.LogInformation("Tenant {TenantId} deactivated group rule template {GroupRuleId}", tenantId, groupRuleId);
    }

    // ── Cache ─────────────────────────────────────────────────────────────────

    public void InvalidateCache(int tenantId, string agentType, string? agentId = null)
    {
        _cache.Remove(RulesCacheKey(tenantId, agentType));
        _cache.Remove(RulesCacheKey(tenantId, "*"));
        if (agentId is not null)
        {
            _cache.Remove(RulesCacheKey(tenantId, agentType, agentId));
            _cache.Remove(RulesCacheKey(tenantId, "*", agentId));
        }
        _cache.Remove(OverridesCacheKey(tenantId, agentType));
        _cache.Remove(OverridesCacheKey(tenantId, "*"));
        _logger.LogDebug("Cache invalidated for tenant {TenantId} agentType {AgentType} agentId {AgentId}",
            tenantId, agentType, agentId);
    }

    // T5: cross-cache invalidation — when a business rule's pack assignment changes,
    // the RulePackEngine's pack cache must also be invalidated so the next resolve is fresh.
    private void InvalidatePackCache(int tenantId)
        => _cache.Remove($"resolved_packs_{tenantId}");

    private static string RulesCacheKey(int tenantId, string agentType, string? agentId = null)
        => agentId is null ? $"rules_{tenantId}_{agentType}" : $"rules_{tenantId}_{agentType}_{agentId}";
    private static string OverridesCacheKey(int tenantId, string agentType) => $"overrides_{tenantId}_{agentType}";
}
