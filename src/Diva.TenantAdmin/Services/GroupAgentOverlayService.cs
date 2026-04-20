using Diva.Infrastructure.Data;
using Diva.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Diva.TenantAdmin.Services;

/// <summary>
/// Singleton-safe implementation of <see cref="IGroupAgentOverlayService"/>.
/// Creates fresh DbContexts per call via IDatabaseProviderFactory.
/// Cache key: "group_overlay_{tenantId}", 5-minute TTL.
/// </summary>
public sealed class GroupAgentOverlayService : IGroupAgentOverlayService
{
    private readonly IDatabaseProviderFactory _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<GroupAgentOverlayService> _logger;

    public GroupAgentOverlayService(
        IDatabaseProviderFactory db,
        IMemoryCache cache,
        ILogger<GroupAgentOverlayService> logger)
    {
        _db     = db;
        _cache  = cache;
        _logger = logger;
    }

    public async Task<List<TenantGroupAgentOverlayEntity>> GetOverlaysAsync(
        int tenantId, CancellationToken ct)
    {
        var key = CacheKey(tenantId);
        if (_cache.TryGetValue(key, out List<TenantGroupAgentOverlayEntity>? cached) && cached is not null)
            return cached;

        using var db = _db.CreateDbContext();
        var list = await db.GroupAgentOverlays
            .Where(o => o.TenantId == tenantId)
            .ToListAsync(ct);

        _cache.Set(key, list, TimeSpan.FromMinutes(5));
        return list;
    }

    public async Task<TenantGroupAgentOverlayEntity?> GetOverlayAsync(
        int tenantId, string groupTemplateId, CancellationToken ct)
    {
        var overlays = await GetOverlaysAsync(tenantId, ct);
        return overlays.FirstOrDefault(o => o.GroupTemplateId == groupTemplateId);
    }

    public async Task<TenantGroupAgentOverlayEntity> ApplyTemplateAsync(
        int tenantId, string groupTemplateId, ApplyGroupAgentOverlayDto dto, CancellationToken ct)
    {
        using var db = _db.CreateDbContext();

        // Load template — must be accessible by navigating through groups
        var template = await db.GroupAgentTemplates
            .FirstOrDefaultAsync(t => t.Id == groupTemplateId, ct)
            ?? throw new InvalidOperationException($"Group agent template '{groupTemplateId}' not found.");

        // Verify tenant is a member of the template's group
        var isMember = await db.TenantGroupMembers
            .AnyAsync(m => m.GroupId == template.GroupId && m.TenantId == tenantId, ct);
        if (!isMember)
            throw new InvalidOperationException(
                $"Tenant {tenantId} is not a member of group {template.GroupId}.");

        // Upsert: update if exists, create if not
        var existing = await db.GroupAgentOverlays
            .IgnoreQueryFilters()   // use explicit TenantId filter below (admin context may bypass global filter)
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.GroupTemplateId == groupTemplateId, ct);

        if (existing is not null)
        {
            ApplyDto(existing, dto);
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Updated group agent overlay for tenant {TenantId}, template {TemplateId}",
                tenantId, groupTemplateId);
            InvalidateCache(tenantId);
            return existing;
        }

        var overlay = new TenantGroupAgentOverlayEntity
        {
            TenantId        = tenantId,
            GroupTemplateId = groupTemplateId,
            GroupId         = template.GroupId,
            IsEnabled       = dto.IsEnabled,
            SystemPromptAddendum  = dto.SystemPromptAddendum,
            ModelId               = dto.ModelId,
            Temperature           = dto.Temperature,
            ExtraToolBindingsJson = dto.ExtraToolBindingsJson,
            CustomVariablesJson   = dto.CustomVariablesJson,
            LlmConfigId           = dto.LlmConfigId,
            MaxOutputTokens       = dto.MaxOutputTokens,
        };
        db.GroupAgentOverlays.Add(overlay);
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Created group agent overlay for tenant {TenantId}, template {TemplateId}",
            tenantId, groupTemplateId);
        InvalidateCache(tenantId);
        return overlay;
    }

    public async Task<TenantGroupAgentOverlayEntity> UpdateOverlayAsync(
        int tenantId, string overlayGuid, UpdateGroupAgentOverlayDto dto, CancellationToken ct)
    {
        using var db = _db.CreateDbContext();
        var overlay = await db.GroupAgentOverlays
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.Guid == overlayGuid, ct)
            ?? throw new InvalidOperationException($"Overlay '{overlayGuid}' not found for tenant {tenantId}.");

        overlay.IsEnabled             = dto.IsEnabled;
        overlay.SystemPromptAddendum  = dto.SystemPromptAddendum;
        overlay.ModelId               = dto.ModelId;
        overlay.Temperature           = dto.Temperature;
        overlay.ExtraToolBindingsJson = dto.ExtraToolBindingsJson;
        overlay.CustomVariablesJson   = dto.CustomVariablesJson;
        overlay.LlmConfigId           = dto.LlmConfigId;
        overlay.MaxOutputTokens       = dto.MaxOutputTokens;

        await db.SaveChangesAsync(ct);
        InvalidateCache(tenantId);
        return overlay;
    }

    public async Task RemoveOverlayAsync(int tenantId, string overlayGuid, CancellationToken ct)
    {
        using var db = _db.CreateDbContext();
        var overlay = await db.GroupAgentOverlays
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.Guid == overlayGuid, ct)
            ?? throw new InvalidOperationException($"Overlay '{overlayGuid}' not found for tenant {tenantId}.");

        db.GroupAgentOverlays.Remove(overlay);
        await db.SaveChangesAsync(ct);
        InvalidateCache(tenantId);
        _logger.LogInformation("Removed group agent overlay {Guid} for tenant {TenantId}", overlayGuid, tenantId);
    }

    public void InvalidateCache(int tenantId)
    {
        _cache.Remove(CacheKey(tenantId));
        _logger.LogDebug("Group overlay cache invalidated for tenant {TenantId}", tenantId);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void ApplyDto(TenantGroupAgentOverlayEntity entity, ApplyGroupAgentOverlayDto dto)
    {
        entity.IsEnabled             = dto.IsEnabled;
        entity.SystemPromptAddendum  = dto.SystemPromptAddendum;
        entity.ModelId               = dto.ModelId;
        entity.Temperature           = dto.Temperature;
        entity.ExtraToolBindingsJson = dto.ExtraToolBindingsJson;
        entity.CustomVariablesJson   = dto.CustomVariablesJson;
        entity.LlmConfigId           = dto.LlmConfigId;
        entity.MaxOutputTokens       = dto.MaxOutputTokens;
    }

    private static string CacheKey(int tenantId) => $"group_overlay_{tenantId}";
}
