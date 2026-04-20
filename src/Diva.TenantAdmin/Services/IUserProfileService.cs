using Diva.Core.Models;
using Diva.Infrastructure.Data.Entities;

namespace Diva.TenantAdmin.Services;

public interface IUserProfileService
{
    /// <summary>Upsert profile on every authenticated login. Non-fatal — logged if it fails.</summary>
    Task UpsertOnLoginAsync(TenantContext tenant, CancellationToken ct = default);

    /// <summary>Returns false if user is disabled. Cached for 5 minutes.</summary>
    Task<bool> IsActiveAsync(int tenantId, string userId, CancellationToken ct = default);

    Task<List<UserProfileEntity>> GetForTenantAsync(int tenantId, string? search = null, string? role = null, CancellationToken ct = default);
    Task<UserProfileEntity?> GetByUserIdAsync(int tenantId, string userId, CancellationToken ct = default);
    Task<UserProfileEntity?> GetByIdAsync(int tenantId, int id, CancellationToken ct = default);
    Task UpdateAsync(int tenantId, int id, UpdateUserProfileDto dto, CancellationToken ct = default);
    Task SetActiveAsync(int tenantId, int id, bool isActive, CancellationToken ct = default);
}

public record UpdateUserProfileDto(
    string DisplayName,
    string? AvatarUrl,
    string[] AgentAccessOverrides,
    string? MetadataJson);
