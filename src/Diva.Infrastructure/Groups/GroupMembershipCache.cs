using Diva.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Diva.Infrastructure.Groups;

/// <summary>
/// IMemoryCache-backed group membership lookup with 5-minute TTL.
/// Singleton-safe: creates a new DbContext per call via IDatabaseProviderFactory.
/// </summary>
public sealed class GroupMembershipCache : IGroupMembershipCache
{
    private readonly IDatabaseProviderFactory _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<GroupMembershipCache> _logger;

    public GroupMembershipCache(
        IDatabaseProviderFactory db,
        IMemoryCache cache,
        ILogger<GroupMembershipCache> logger)
    {
        _db     = db;
        _cache  = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<int>> GetGroupIdsForTenantAsync(int tenantId, CancellationToken ct)
    {
        var key = CacheKey(tenantId);
        if (_cache.TryGetValue(key, out IReadOnlyList<int>? cached) && cached is not null)
            return cached;

        using var db  = _db.CreateDbContext();
        var groupIds  = await db.TenantGroupMembers
            .Where(m => m.TenantId == tenantId)
            .Join(db.TenantGroups,
                m => m.GroupId,
                g => g.Id,
                (m, g) => new { m.GroupId, g.IsActive })
            .Where(x => x.IsActive)
            .Select(x => x.GroupId)
            .ToListAsync(ct);

        IReadOnlyList<int> result = groupIds.AsReadOnly();
        _cache.Set(key, result, TimeSpan.FromMinutes(5));
        _logger.LogDebug("GroupMembershipCache: tenant {TenantId} belongs to groups [{GroupIds}]",
            tenantId, string.Join(",", groupIds));
        return result;
    }

    public void InvalidateForTenant(int tenantId)
    {
        _cache.Remove(CacheKey(tenantId));
        _logger.LogDebug("GroupMembershipCache: invalidated tenant {TenantId}", tenantId);
    }

    public async Task InvalidateForGroupAsync(int groupId, CancellationToken ct)
    {
        using var db  = _db.CreateDbContext();
        var tenantIds = await db.TenantGroupMembers
            .Where(m => m.GroupId == groupId)
            .Select(m => m.TenantId)
            .ToListAsync(ct);

        foreach (var tenantId in tenantIds)
            _cache.Remove(CacheKey(tenantId));

        _logger.LogDebug("GroupMembershipCache: invalidated {Count} tenants for group {GroupId}",
            tenantIds.Count, groupId);
    }

    private static string CacheKey(int tenantId) => $"group_members_{tenantId}";
}
