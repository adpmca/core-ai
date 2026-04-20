namespace Diva.Infrastructure.Groups;

/// <summary>
/// Returns the active group IDs that a tenant belongs to.
/// 5-minute TTL backed by IMemoryCache — avoids per-request DB hits on the hot path
/// (prompt building, agent listing, scheduler dispatch).
/// </summary>
public interface IGroupMembershipCache
{
    Task<IReadOnlyList<int>> GetGroupIdsForTenantAsync(int tenantId, CancellationToken ct);
    void InvalidateForTenant(int tenantId);

    /// <summary>Evicts cached membership for every tenant currently in the group.</summary>
    Task InvalidateForGroupAsync(int groupId, CancellationToken ct);
}
