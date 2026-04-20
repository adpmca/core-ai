using Diva.Infrastructure.Data;
using Diva.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Diva.TenantAdmin.Services;

/// <summary>
/// DTOs for Rule Pack CRUD operations.
/// </summary>
public record CreateRulePackDto(
    string Name,
    string? Description,
    int? GroupId,
    int Priority = 100,
    bool IsMandatory = false,
    string? AppliesToJson = null,
    string? ActivationCondition = null,
    int? ParentPackId = null,
    int MaxEvaluationMs = 500);

public record UpdateRulePackDto(
    string Name,
    string? Description,
    string Version,
    int Priority,
    bool IsEnabled,
    bool IsMandatory,
    string? AppliesToJson,
    string? ActivationCondition,
    int MaxEvaluationMs);

public record CreateHookRuleDto(
    string HookPoint,
    string RuleType,
    string? Pattern,
    string? Instruction,
    string? Replacement,
    string? ToolName,
    int OrderInPack = 1,
    bool StopOnMatch = false,
    int MaxEvaluationMs = 100,
    string MatchTarget = "query");

public record UpdateHookRuleDto(
    string HookPoint,
    string RuleType,
    string? Pattern,
    string? Instruction,
    string? Replacement,
    string? ToolName,
    int OrderInPack,
    bool IsEnabled,
    bool StopOnMatch,
    int MaxEvaluationMs,
    string MatchTarget = "query");

public record RulePackTestRequest(string SampleQuery, string SampleResponse);

public record RulePackTestResult(
    string ModifiedPrompt,
    string ModifiedResponse,
    List<RulePackTestResult.TriggeredRule> RulesTriggered,
    bool Blocked)
{
    public record TriggeredRule(int RuleId, string RuleType, string Action);
}

/// <summary>
/// CRUD service for Rule Packs and their child rules.
/// Singleton-safe: uses IDatabaseProviderFactory per call.
/// </summary>
public interface IRulePackService
{
    Task<List<HookRulePackEntity>> GetPacksAsync(int tenantId, CancellationToken ct);
    Task<HookRulePackEntity?> GetPackWithRulesAsync(int tenantId, int packId, CancellationToken ct);
    Task<HookRulePackEntity> CreatePackAsync(int tenantId, CreateRulePackDto dto, CancellationToken ct);
    Task<HookRulePackEntity> UpdatePackAsync(int tenantId, int packId, UpdateRulePackDto dto, CancellationToken ct);
    Task DeletePackAsync(int tenantId, int packId, CancellationToken ct);
    Task<HookRulePackEntity> ClonePackAsync(int tenantId, int sourcePackId, string newName, CancellationToken ct);

    Task<HookRuleEntity> AddRuleAsync(int tenantId, int packId, CreateHookRuleDto dto, CancellationToken ct);
    Task<HookRuleEntity> UpdateRuleAsync(int tenantId, int packId, int ruleId, UpdateHookRuleDto dto, CancellationToken ct);
    Task DeleteRuleAsync(int tenantId, int packId, int ruleId, CancellationToken ct);
    Task ReorderRulesAsync(int tenantId, int packId, int[] ruleIds, CancellationToken ct);

    /// <summary>Get starter packs (TenantId=0) that can be cloned by tenants.</summary>
    Task<List<HookRulePackEntity>> GetStarterPacksAsync(CancellationToken ct);

    void InvalidateCache(int tenantId);
}

public sealed class RulePackService : IRulePackService
{
    private readonly IDatabaseProviderFactory _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<RulePackService> _logger;

    public RulePackService(
        IDatabaseProviderFactory db,
        IMemoryCache cache,
        ILogger<RulePackService> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    public async Task<List<HookRulePackEntity>> GetPacksAsync(int tenantId, CancellationToken ct)
    {
        var key = CacheKey(tenantId);
        if (_cache.TryGetValue(key, out List<HookRulePackEntity>? cached) && cached is not null)
            return cached;

        using var db = _db.CreateDbContext();
        var packs = await db.RulePacks
            .Where(p => p.TenantId == tenantId)
            .Include(p => p.Rules.Where(r => r.IsEnabled).OrderBy(r => r.OrderInPack))
            .OrderBy(p => p.Priority)
            .AsNoTracking()
            .ToListAsync(ct);

        _cache.Set(key, packs, TimeSpan.FromMinutes(5));
        return packs;
    }

    public async Task<HookRulePackEntity?> GetPackWithRulesAsync(int tenantId, int packId, CancellationToken ct)
    {
        using var db = _db.CreateDbContext();
        return await db.RulePacks
            .Where(p => p.TenantId == tenantId && p.Id == packId)
            .Include(p => p.Rules.OrderBy(r => r.OrderInPack))
            .Include(p => p.ParentPack)
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);
    }

    public async Task<HookRulePackEntity> CreatePackAsync(int tenantId, CreateRulePackDto dto, CancellationToken ct)
    {
        using var db = _db.CreateDbContext();
        var entity = new HookRulePackEntity
        {
            TenantId = tenantId,
            Name = dto.Name,
            Description = dto.Description,
            GroupId = dto.GroupId,
            Priority = dto.Priority,
            IsMandatory = dto.IsMandatory,
            AppliesToJson = dto.AppliesToJson,
            ActivationCondition = dto.ActivationCondition,
            ParentPackId = dto.ParentPackId,
            MaxEvaluationMs = dto.MaxEvaluationMs,
            IsEnabled = true,
        };
        db.RulePacks.Add(entity);
        await db.SaveChangesAsync(ct);
        InvalidateCache(tenantId);
        _logger.LogInformation("Created rule pack '{Name}' (id={Id}) for tenant {TenantId}", dto.Name, entity.Id, tenantId);
        return entity;
    }

    public async Task<HookRulePackEntity> UpdatePackAsync(int tenantId, int packId, UpdateRulePackDto dto, CancellationToken ct)
    {
        using var db = _db.CreateDbContext();
        var entity = await db.RulePacks
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Id == packId, ct)
            ?? throw new InvalidOperationException($"Pack {packId} not found for tenant {tenantId}");

        entity.Name = dto.Name;
        entity.Description = dto.Description;
        entity.Version = dto.Version;
        entity.Priority = dto.Priority;
        entity.IsEnabled = dto.IsEnabled;
        entity.IsMandatory = dto.IsMandatory;
        entity.AppliesToJson = dto.AppliesToJson;
        entity.ActivationCondition = dto.ActivationCondition;
        entity.MaxEvaluationMs = dto.MaxEvaluationMs;

        await db.SaveChangesAsync(ct);
        InvalidateCache(tenantId);
        return entity;
    }

    public async Task DeletePackAsync(int tenantId, int packId, CancellationToken ct)
    {
        using var db = _db.CreateDbContext();
        var entity = await db.RulePacks
            .Include(p => p.Rules)
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Id == packId, ct)
            ?? throw new InvalidOperationException($"Pack {packId} not found for tenant {tenantId}");

        if (entity.IsMandatory)
            throw new InvalidOperationException("Cannot delete a mandatory pack");

        db.HookRules.RemoveRange(entity.Rules);
        db.RulePacks.Remove(entity);
        await db.SaveChangesAsync(ct);
        InvalidateCache(tenantId);
    }

    public async Task<HookRulePackEntity> ClonePackAsync(int tenantId, int sourcePackId, string newName, CancellationToken ct)
    {
        using var db = _db.CreateDbContext();

        // Source can be starter pack (TenantId=0) or own tenant pack
        var source = await db.RulePacks
            .IgnoreQueryFilters()
            .Include(p => p.Rules)
            .FirstOrDefaultAsync(p => p.Id == sourcePackId, ct)
            ?? throw new InvalidOperationException($"Source pack {sourcePackId} not found");

        var clone = new HookRulePackEntity
        {
            TenantId = tenantId,
            Name = newName,
            Description = source.Description,
            Priority = source.Priority,
            IsMandatory = false, // Cloned packs are never mandatory
            AppliesToJson = source.AppliesToJson,
            ActivationCondition = source.ActivationCondition,
            ParentPackId = source.Id,
            MaxEvaluationMs = source.MaxEvaluationMs,
            IsEnabled = true,
            Version = "1.0",
        };

        foreach (var rule in source.Rules)
        {
            clone.Rules.Add(new HookRuleEntity
            {
                HookPoint = rule.HookPoint,
                RuleType = rule.RuleType,
                Pattern = rule.Pattern,
                Instruction = rule.Instruction,
                Replacement = rule.Replacement,
                ToolName = rule.ToolName,
                OrderInPack = rule.OrderInPack,
                StopOnMatch = rule.StopOnMatch,
                IsEnabled = rule.IsEnabled,
                MatchTarget = rule.MatchTarget,
                MaxEvaluationMs = rule.MaxEvaluationMs,
            });
        }

        db.RulePacks.Add(clone);
        await db.SaveChangesAsync(ct);
        InvalidateCache(tenantId);
        _logger.LogInformation("Cloned pack {SourceId} as '{NewName}' (id={CloneId}) for tenant {TenantId}",
            sourcePackId, newName, clone.Id, tenantId);
        return clone;
    }

    public async Task<HookRuleEntity> AddRuleAsync(int tenantId, int packId, CreateHookRuleDto dto, CancellationToken ct)
    {
        using var db = _db.CreateDbContext();
        // Verify pack belongs to tenant
        var pack = await db.RulePacks.FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Id == packId, ct)
            ?? throw new InvalidOperationException($"Pack {packId} not found for tenant {tenantId}");

        RulePackRuleCompatibility.ValidateOrThrow(dto.HookPoint, dto.RuleType);

        var entity = new HookRuleEntity
        {
            PackId = packId,
            HookPoint = dto.HookPoint,
            RuleType = dto.RuleType,
            Pattern = dto.Pattern,
            Instruction = dto.Instruction,
            Replacement = dto.Replacement,
            ToolName = dto.ToolName,
            OrderInPack = dto.OrderInPack,
            StopOnMatch = dto.StopOnMatch,
            MaxEvaluationMs = dto.MaxEvaluationMs,
            MatchTarget = dto.MatchTarget,
            IsEnabled = true,
        };
        db.HookRules.Add(entity);
        await db.SaveChangesAsync(ct);
        InvalidateCache(tenantId);
        return entity;
    }

    public async Task<HookRuleEntity> UpdateRuleAsync(int tenantId, int packId, int ruleId, UpdateHookRuleDto dto, CancellationToken ct)
    {
        using var db = _db.CreateDbContext();
        var rule = await db.HookRules
            .Include(r => r.Pack)
            .FirstOrDefaultAsync(r => r.Id == ruleId && r.PackId == packId && r.Pack.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException($"Rule {ruleId} not found in pack {packId}");

        RulePackRuleCompatibility.ValidateOrThrow(dto.HookPoint, dto.RuleType);

        rule.HookPoint = dto.HookPoint;
        rule.RuleType = dto.RuleType;
        rule.Pattern = dto.Pattern;
        rule.Instruction = dto.Instruction;
        rule.Replacement = dto.Replacement;
        rule.ToolName = dto.ToolName;
        rule.OrderInPack = dto.OrderInPack;
        rule.IsEnabled = dto.IsEnabled;
        rule.StopOnMatch = dto.StopOnMatch;
        rule.MaxEvaluationMs = dto.MaxEvaluationMs;
        rule.MatchTarget = dto.MatchTarget;

        await db.SaveChangesAsync(ct);
        InvalidateCache(tenantId);
        return rule;
    }

    public async Task DeleteRuleAsync(int tenantId, int packId, int ruleId, CancellationToken ct)
    {
        using var db = _db.CreateDbContext();
        var rule = await db.HookRules
            .Include(r => r.Pack)
            .FirstOrDefaultAsync(r => r.Id == ruleId && r.PackId == packId && r.Pack.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException($"Rule {ruleId} not found in pack {packId}");

        db.HookRules.Remove(rule);
        await db.SaveChangesAsync(ct);
        InvalidateCache(tenantId);
    }

    public async Task ReorderRulesAsync(int tenantId, int packId, int[] ruleIds, CancellationToken ct)
    {
        using var db = _db.CreateDbContext();
        var rules = await db.HookRules
            .Include(r => r.Pack)
            .Where(r => r.PackId == packId && r.Pack.TenantId == tenantId)
            .ToListAsync(ct);

        for (var i = 0; i < ruleIds.Length; i++)
        {
            var rule = rules.FirstOrDefault(r => r.Id == ruleIds[i]);
            if (rule is not null)
                rule.OrderInPack = i + 1;
        }

        await db.SaveChangesAsync(ct);
        InvalidateCache(tenantId);
    }

    public async Task<List<HookRulePackEntity>> GetStarterPacksAsync(CancellationToken ct)
    {
        const string key = "starter_packs";
        if (_cache.TryGetValue(key, out List<HookRulePackEntity>? cached) && cached is not null)
            return cached;

        using var db = _db.CreateDbContext();
        var packs = await db.RulePacks
            .IgnoreQueryFilters()
            .Where(p => p.TenantId == 0)
            .Include(p => p.Rules.OrderBy(r => r.OrderInPack))
            .OrderBy(p => p.Priority)
            .AsNoTracking()
            .ToListAsync(ct);

        _cache.Set(key, packs, TimeSpan.FromMinutes(15));
        return packs;
    }

    public void InvalidateCache(int tenantId)
    {
        _cache.Remove(CacheKey(tenantId));
        _cache.Remove("starter_packs");
        _logger.LogDebug("Rule pack cache invalidated for tenant {TenantId}", tenantId);
    }

    private static string CacheKey(int tenantId) => $"rulepacks_{tenantId}";
}
