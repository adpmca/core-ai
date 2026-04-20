using Diva.Core.Models;

namespace Diva.Infrastructure.Auth;

/// <summary>
/// Minimal interface for what TenantContextMiddleware needs from the user profile system.
/// Implemented by UserProfileService (Diva.TenantAdmin) — registered in Program.cs.
/// Defined here to avoid a circular project reference (Infrastructure → TenantAdmin).
/// </summary>
public interface IUserLoginTracker
{
    /// <summary>Upsert user profile on login. Non-fatal — failure is logged and swallowed.</summary>
    Task UpsertOnLoginAsync(TenantContext tenant, CancellationToken ct = default);

    /// <summary>Returns false if admin has disabled this user. Cached for 5 minutes.</summary>
    Task<bool> IsActiveAsync(int tenantId, string userId, CancellationToken ct = default);
}
